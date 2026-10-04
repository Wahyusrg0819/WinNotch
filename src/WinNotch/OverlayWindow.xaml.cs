using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinNotch.Core;
using WinNotch.Modules;
using WinNotch.Native;

namespace WinNotch;

public partial class OverlayWindow : Window
{
    private readonly App host;
    private IntPtr handle;
    private IntPtr returnFocus;
    private bool hover;
    private bool timerTab, updatingAudio, updatingBrightness;
    private string panel = "media";
    private NotchState shownState = NotchState.Idle;
    private readonly DispatcherTimer timelineTimer;
    private readonly DispatcherTimer clockTimer;
    private readonly MenuItem pauseItem;
    private readonly MenuItem timerItem;
    internal ContextMenu TrayMenu => Notch.ContextMenu;

    public OverlayWindow(App host)
    {
        this.host = host;
        InitializeComponent();
        Width = DisplayPolicy.CanvasWidth; Height = DisplayPolicy.CanvasHeight;
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => host.OpenSettings();
        var player = new MenuItem { Header = "Open player" };
        player.Click += (_, _) => Expand(false);
        timerItem = new MenuItem { Header = "Open timer" };
        timerItem.Click += (_, _) => Expand(true);
        pauseItem = new MenuItem { Header = "Pause WinNotch" };
        pauseItem.Click += (_, _) => host.TogglePause();
        var exit = new MenuItem { Header = "Exit WinNotch" };
        var exclude = new MenuItem();
        string exclusionTarget = "";
        menu.Opened += (_, _) =>
        {
            exclusionTarget = host.LastAppName;
            exclude.Header = exclusionTarget.Length > 0 ? $"Hide in {exclusionTarget}" : "Hide in this app";
            exclude.IsEnabled = DesktopPolicy.NormalizeApps(new[] { exclusionTarget }).Length > 0;
        };
        exclude.Click += (_, _) => host.ExcludeApp(exclusionTarget);
        exit.Click += (_, _) => host.Shutdown();
        var restart = new MenuItem { Header = "Restart WinNotch" };
        restart.Click += (_, _) => host.Restart();
        menu.Items.Add(player); menu.Items.Add(timerItem); menu.Items.Add(settings); menu.Items.Add(exclude); menu.Items.Add(pauseItem); menu.Items.Add(new Separator()); menu.Items.Add(restart); menu.Items.Add(exit);
        Notch.ContextMenu = menu;
        timelineTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => host.Media.RefreshTimeline(), Dispatcher);
        timelineTimer.Stop();
        clockTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        clockTimer.Tick += (_, _) => UpdateClock();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
            SetPassive(true);
            Reposition();
        };
        Closed += (_, _) => { timelineTimer.Stop(); clockTimer.Stop(); };
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x21 && host.State.Current.State != NotchState.Expanded) { handled = true; return new IntPtr(3); }
        if (message is 0x2E0 or 0x7E) Dispatcher.BeginInvoke(Reposition);
        return IntPtr.Zero;
    }

    private void SetPassive(bool passive)
    {
        if (handle == IntPtr.Zero) return;
        var style = Win32.GetWindowLongPtr(handle, Win32.GwlExStyle).ToInt64() | Win32.ToolWindow;
        style = passive ? style | Win32.NoActivate : style & ~Win32.NoActivate;
        Win32.SetWindowLongPtr(handle, Win32.GwlExStyle, new IntPtr(style));
    }

    public void Reposition()
    {
        if (handle == IntPtr.Zero) return;
        var primary = host.TargetMonitor;
        // Move into the target display first. WPF then receives WM_DPICHANGED and
        // the next placement uses this window's effective DPI, not a monitor query.
        if (Win32.MonitorFromWindow(handle, 2) != primary.Handle)
        {
            var initial = DisplayPolicy.PlaceOverlay(primary.Info.Monitor.Bounds, Win32.GetDpiForWindow(handle));
            Win32.SetWindowPos(handle, IntPtr.Zero, initial.Left, initial.Top, initial.Width, initial.Height, 0x14);
        }
        var dpi = Win32.GetDpiForWindow(handle);
        var bounds = DisplayPolicy.PlaceOverlay(primary.Info.Monitor.Bounds, dpi);
        Win32.SetWindowPos(handle, host.Settings.AlwaysOnTop ? new IntPtr(-1) : new IntPtr(-2),
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x10);
        var density = (dpi == 0 ? 96 : dpi) / 96d;
        var fit = Math.Min(host.Settings.Scale, Math.Min(bounds.Width / density / 390, (bounds.Height / density - Notch.Margin.Top) / 240));
        Notch.LayoutTransform = new ScaleTransform(Math.Max(0.1, fit), Math.Max(0.1, fit));
    }

    public void ApplySettings()
    {
        Topmost = host.Settings.AlwaysOnTop;
        Notch.LayoutTransform = new ScaleTransform(host.Settings.Scale, host.Settings.Scale);
        Notch.Margin = new Thickness(0, host.Settings.Floating ? 8 : 0, 0, 0);
        Notch.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : host.Settings.TrueBlack ? Brushes.Black : new SolidColorBrush(Color.FromRgb(5, 5, 5));
        Notch.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.Transparent;
        Notch.BorderThickness = new Thickness(SystemParameters.HighContrast ? 1 : 0);
        Reposition();
        TimerTab.Visibility = host.Settings.Timer ? Visibility.Visible : Visibility.Collapsed;
        timerItem.IsEnabled = host.Settings.Timer;
        VolumeControls.Visibility = host.Settings.Volume ? Visibility.Visible : Visibility.Collapsed;
        DisplayTab.Visibility = host.Settings.Brightness ? Visibility.Visible : Visibility.Collapsed;
        NotificationTab.Visibility = host.Settings.Notifications ? Visibility.Visible : Visibility.Collapsed;
        if (!host.Settings.Timer) timerTab = false;
        SelectPanel(panel);
        Render(host.State.Current);
    }

    public void Render(NotchPresentation presentation)
    {
        var previous = shownState;
        shownState = presentation.State;
        pauseItem.Header = host.State.Paused ? "Resume WinNotch" : "Pause WinNotch";
        if (shownState == NotchState.Hidden) { timelineTimer.Stop(); clockTimer.Stop(); SetPassive(true); Hide(); return; }
        if (!IsVisible) Show();
        var expanded = shownState == NotchState.Expanded;
        SetPassive(!expanded);
        if (previous == NotchState.Expanded && !expanded && Win32.GetForegroundWindow() == handle && returnFocus != IntPtr.Zero)
            Win32.SetForegroundWindow(returnFocus);
        UpdateTimelineTimer();
        IdleContent.Visibility = shownState == NotchState.Idle ? Visibility.Visible : Visibility.Collapsed;
        UpdateClock();
        CompactContent.Visibility = shownState == NotchState.Compact ? Visibility.Visible : Visibility.Collapsed;
        PeekContent.Visibility = shownState == NotchState.Peek ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (presentation.Event is { } e)
        {
            PeekIcon.Text = e.Glyph; PeekTitle.Text = e.Title; PeekDetail.Text = e.Detail;
            PeekLevel.Visibility = e.Level.HasValue ? Visibility.Visible : Visibility.Collapsed;
            PeekLevel.Value = e.Level ?? 0;
        }
        var (width, height) = shownState switch
        {
            NotchState.Expanded => (390d, 240d),
            NotchState.Compact => (268d, 42d),
            NotchState.Peek => (280d, presentation.Event?.Level != null ? 46d : 40d),
            _ => hover ? (138d, 32d) : (126d, 30d)
        };
        Notch.CornerRadius = host.Settings.Floating ? new CornerRadius(expanded ? 23 : 18) : new CornerRadius(0, 0, expanded ? 23 : 16, expanded ? 23 : 16);
        AnimateSize(width, height, expanded ? 280 : 200);
        if (previous != shownState && CanAnimate)
        {
            var content = expanded ? ExpandedContent : shownState == NotchState.Peek ? PeekContent : shownState == NotchState.Compact ? CompactContent : IdleContent;
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130 / host.Settings.AnimationSpeed)));
        }
    }

    private bool CanAnimate => host.Settings.Animations && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    private void AnimateSize(double width, double height, int milliseconds)
    {
        if (!CanAnimate)
        {
            Notch.BeginAnimation(WidthProperty, null); Notch.BeginAnimation(HeightProperty, null);
            Notch.Width = width; Notch.Height = height; return;
        }
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Notch.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(milliseconds / host.Settings.AnimationSpeed)) { EasingFunction = easing });
        Notch.BeginAnimation(HeightProperty, new DoubleAnimation(height, TimeSpan.FromMilliseconds(milliseconds / host.Settings.AnimationSpeed)) { EasingFunction = easing });
    }

    public void UpdateMedia(MediaSnapshot? media)
    {
        CompactArt.Source = ExpandedArt.Source = media?.Artwork;
        CompactTitle.Text = media?.Title ?? "No media playing";
        TrackTitle.Text = media?.Title ?? (host.Settings.Media ? "A little space for your music." : "Your quiet corner.");
        TrackArtist.Text = media == null ? (host.Settings.Media ? host.Media.Status : "Media is turned off in settings.") : string.IsNullOrWhiteSpace(media.Artist) ? "Windows media session" : media.Artist;
        TrackTitle.ToolTip = media?.Title;
        TrackArtist.ToolTip = media?.Artist;
        PlayButton.Content = CompactPlay.Content = media?.Playing == true ? "\uE769" : "\uE768";
        PlayButton.IsEnabled = CompactPlay.IsEnabled = media?.CanPlayPause == true;
        PreviousButton.IsEnabled = media?.CanPrevious == true;
        NextButton.IsEnabled = media?.CanNext == true;
        Elapsed.Text = FormatTime(media?.Position ?? TimeSpan.Zero);
        Duration.Text = FormatTime(media?.Duration ?? TimeSpan.Zero);
        Timeline.Value = media?.Duration.TotalSeconds > 0 ? media.Position.TotalSeconds / media.Duration.TotalSeconds : 0;
        UpdateTimelineTimer();
    }

    private void UpdateTimelineTimer()
    {
        if (shownState == NotchState.Expanded && panel == "media" && host.Settings.Media && host.Media.Current?.Playing == true) timelineTimer.Start(); else timelineTimer.Stop();
    }

    private void SelectPanel(bool showTimer)
        => SelectPanel(showTimer ? "timer" : "media");

    private void SelectPanel(string selected)
    {
        if ((selected == "timer" && !host.Settings.Timer) || (selected == "brightness" && !host.Settings.Brightness) || (selected == "notification" && !host.Settings.Notifications)) selected = "media";
        panel = selected;
        timerTab = selected == "timer";
        TimerPanel.Visibility = timerTab ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = selected == "media" ? Visibility.Visible : Visibility.Collapsed;
        BrightnessPanel.Visibility = selected == "brightness" ? Visibility.Visible : Visibility.Collapsed;
        NotificationPanel.Visibility = selected == "notification" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (tab, name) in new[] { (MediaTab, "media"), (TimerTab, "timer"), (DisplayTab, "brightness"), (NotificationTab, "notification") })
        {
            tab.SetResourceReference(BackgroundProperty, selected == name ? "CardBrush" : "CanvasBrush");
            tab.SetResourceReference(ForegroundProperty, selected == name ? "AccentBrush" : "MutedBrush");
        }
        UpdateTimelineTimer();
    }

    private void UpdateClock()
    {
        var show = host.Settings.Clock && shownState == NotchState.Idle;
        IdleMark.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        IdleClock.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { clockTimer.Stop(); return; }
        var now = DateTime.Now;
        IdleClock.Text = now.ToString("t"); IdleClock.ToolTip = now.ToString("D");
        clockTimer.Interval = TimeSpan.FromMilliseconds(60_000 - now.Second * 1000 - now.Millisecond + 20);
        clockTimer.Start();
    }

    public void UpdateBrightness(BrightnessSnapshot brightness)
    {
        updatingBrightness = true;
        try
        {
            BrightnessSlider.IsEnabled = host.Settings.Brightness && brightness.Available;
            BrightnessSlider.Value = brightness.Percent;
            BrightnessPercent.Text = brightness.Available ? $"{brightness.Percent}%" : "—";
            BrightnessStatus.Text = brightness.Available ? "Set a comfortable brightness." : "Brightness control is unavailable on this screen. External monitors are not supported yet.";
        }
        finally { updatingBrightness = false; }
    }

    public void UpdateNotification()
    {
        var preview = host.LatestNotification;
        NotificationApp.Text = preview?.App ?? "NOTIFICATION PREVIEWS";
        NotificationTitle.Text = preview?.Title ?? "Nothing new here.";
        NotificationBody.Text = preview?.Body ?? "New notifications appear after access is allowed in Settings. The originals stay in Windows.";
        DismissNotificationButton.Visibility = preview == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void UpdateTimer()
    {
        var timer = host.Timer;
        var active = host.Settings.Timer && timer.IsActive;
        var complete = timer.Status == TimerStatus.Completed;
        var moveFocus = complete && PauseTimerButton.IsKeyboardFocusWithin;
        var paused = timer.Status == TimerStatus.Paused;
        var seconds = (int)Math.Ceiling(timer.Remaining.TotalSeconds);
        var countdown = $"{seconds / 60:00}:{seconds % 60:00}";
        CompactMedia.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        CompactTimer.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        CompactTimerLabel.Text = timer.Label + (complete ? " complete" : paused ? " · Paused" : "");
        CompactCountdown.Text = complete ? "Done" : countdown;
        TimerSetup.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        TimerActive.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        TimerPhase.Text = timer.Label;
        TimerCountdown.Text = countdown;
        TimerStatusText.Text = complete ? "Session complete. Take your next step." : paused ? "Paused · Continue when you are ready." : timer.Mode == TimerMode.Custom ? "Time for what matters." : $"Focus sessions completed: {timer.FocusSessions}";
        PauseTimerButton.Content = paused ? "Resume" : "Pause";
        PauseTimerButton.Visibility = complete ? Visibility.Collapsed : Visibility.Visible;
        NextTimerButton.Visibility = complete && timer.Mode != TimerMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        NextTimerButton.Content = timer.NextMode switch { TimerMode.ShortBreak => "Start break · 5m", TimerMode.LongBreak => "Long break · 15m", _ => "Start focus · 25m" };
        CancelTimerButton.Content = complete ? "Done" : "Cancel";
        if (complete && shownState == NotchState.Expanded) SelectPanel(true);
        if (moveFocus) { if (timer.Mode == TimerMode.Custom) CancelTimerButton.Focus(); else NextTimerButton.Focus(); }
    }

    public void UpdateAudio(AudioSnapshot audio)
    {
        if (VolumeSlider == null) return;
        updatingAudio = true;
        try
        {
            VolumeSlider.IsEnabled = MuteButton.IsEnabled = host.Settings.Volume && audio.Available;
            VolumeSlider.Value = audio.Percent;
            VolumePercent.Text = !audio.Available ? "No output" : audio.Muted ? "Muted" : $"{audio.Percent}%";
            MuteButton.Content = audio.Muted ? "\uE74F" : "\uE767";
            MuteButton.ToolTip = audio.Muted ? "Unmute" : "Mute";
            System.Windows.Automation.AutomationProperties.SetName(MuteButton, audio.Muted ? "Unmute audio" : "Mute audio");
            VolumeControls.ToolTip = audio.Available ? "System volume" : "Connect an audio output to adjust volume.";
        }
        finally { updatingAudio = false; }
    }

    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    public void Expand(bool? showTimer = null)
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground != handle) returnFocus = foreground;
        if (host.State.Paused) host.TogglePause();
        SelectPanel(showTimer ?? host.Timer.IsActive);
        host.State.Expand();
        if (host.State.Current.State != NotchState.Expanded) return;
        SetPassive(false); Activate();
        if (timerTab) { if (!host.Timer.IsActive) TimerMinutes.Focus(); else if (host.Timer.Status == TimerStatus.Completed) CancelTimerButton.Focus(); else PauseTimerButton.Focus(); }
        else if (PlayButton.IsEnabled) PlayButton.Focus(); else MediaTab.Focus();
    }
    private void OnNotchClick(object sender, MouseButtonEventArgs e)
    {
        if (shownState == NotchState.Expanded) return;
        var notification = host.State.Current.Event?.Key == "notification";
        Expand();
        if (notification && host.State.Current.State == NotchState.Expanded) { SelectPanel("notification"); DismissNotificationButton.Focus(); }
    }
    private void OnEnter(object sender, MouseEventArgs e) { hover = true; if (shownState == NotchState.Idle) Render(host.State.Current); }
    private void OnLeave(object sender, MouseEventArgs e) { hover = false; if (shownState == NotchState.Idle) Render(host.State.Current); }
    private void OnDeactivated(object? sender, EventArgs e) { if (Notch.ContextMenu?.IsOpen != true) host.State.Collapse(); }
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { host.State.Collapse(); e.Handled = true; } }
    private async void OnPlay(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("play"); }
    private async void OnPrevious(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("previous"); }
    private async void OnNext(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("next"); }
    private void OnSettings(object sender, RoutedEventArgs e) { e.Handled = true; host.OpenSettings(); }
    private void OnMediaTab(object sender, RoutedEventArgs e) => SelectPanel(false);
    private void OnTimerTab(object sender, RoutedEventArgs e) => SelectPanel(true);
    private void OnDisplayTab(object sender, RoutedEventArgs e) => SelectPanel("brightness");
    private void OnNotificationTab(object sender, RoutedEventArgs e) => SelectPanel("notification");
    private void OnDismissNotification(object sender, RoutedEventArgs e) { host.DismissNotification(); NotificationTab.Focus(); }
    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!updatingBrightness && IsInitialized && host.Settings.Brightness) host.Brightness.SetBrightness(e.NewValue);
    }
    private void OnStartTimer(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TimerMinutes.Text, out var minutes) || minutes is < 1 or > 180)
        { TimerHint.Text = "Enter a whole number from 1 to 180."; TimerMinutes.Focus(); return; }
        host.Timer.Start(TimeSpan.FromMinutes(minutes)); PauseTimerButton.Focus();
    }
    private void OnTimerPreset(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string mode } && Enum.TryParse<TimerMode>(mode, out var value))
        { host.Timer.StartPomodoro(value); PauseTimerButton.Focus(); }
    }
    private void OnPauseTimer(object sender, RoutedEventArgs e) => host.Timer.TogglePause();
    private void OnNextTimer(object sender, RoutedEventArgs e) { host.Timer.NextPomodoro(); PauseTimerButton.Focus(); }
    private void OnCancelTimer(object sender, RoutedEventArgs e)
    { host.Timer.Cancel(); TimerHint.Text = "Choose a preset or enter 1–180 minutes."; TimerMinutes.Focus(); }
    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingAudio || !IsInitialized || !host.Settings.Volume) return;
        if (!host.Audio.SetVolume(e.NewValue)) AudioControlFailed();
    }
    private void OnMute(object sender, RoutedEventArgs e)
    {
        if (host.Settings.Volume && !host.Audio.SetMuted(!host.Audio.Current.Muted)) AudioControlFailed();
    }
    private void AudioControlFailed()
    {
        UpdateAudio(host.Audio.Current);
        VolumePercent.Text = "Retry";
        VolumeControls.ToolTip = "Couldn't change audio. Check the output device and try again.";
    }
}

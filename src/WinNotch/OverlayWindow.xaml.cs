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
    private NotchState shownState = NotchState.Idle;
    private readonly DispatcherTimer timelineTimer;
    private readonly DispatcherTimer clockTimer;
    private readonly MenuItem pauseItem;
    private readonly MenuItem timerItem;
    internal ContextMenu TrayMenu => Notch.ContextMenu;
    internal ExpandedControls? ExpandedContent { get; private set; }
    internal bool HasExpandedContent => ExpandedContent != null;

    private void EnsureExpandedContent()
    {
        // Load the interactive controls once, on first expansion; keep them for later openings.
        if (HasExpandedContent) return;
        ExpandedContent = new ExpandedControls(host);
        ((Grid)Notch.Child).Children.Add(ExpandedContent);
        ExpandedContent.ApplySettings();
        ExpandedContent.UpdateAudio(host.Audio.Current);
    }

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
        var agenda = new MenuItem { Header = "Open agenda" };
        agenda.Click += (_, _) => host.OpenAgenda();
        menu.Items.Insert(2, agenda);
        var downloads = new MenuItem { Header = "Open downloads" };
        menu.Opened += (_, _) => downloads.IsEnabled = host.Settings.Downloads;
        downloads.Click += (_, _) => { Expand(false); if (host.State.Current.State == NotchState.Expanded) { ExpandedContent!.SelectPanel("downloads"); ExpandedContent.FocusPanel(); } };
        menu.Items.Insert(3, downloads);
        var bluetooth = new MenuItem { Header = "Open Bluetooth" };
        menu.Opened += (_, _) => bluetooth.IsEnabled = host.Settings.Bluetooth;
        bluetooth.Click += (_, _) => { Expand(false); if (host.State.Current.State == NotchState.Expanded) { ExpandedContent!.SelectPanel("bluetooth"); ExpandedContent.FocusPanel(); } };
        menu.Items.Insert(4, bluetooth);
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
        Reposition();
        timerItem.IsEnabled = host.Settings.Timer;
        ExpandedContent?.ApplySettings();
        Render(host.State.Current);
    }

    public void Render(NotchPresentation presentation)
    {
        var previous = shownState;
        shownState = presentation.State;
        pauseItem.Header = host.State.Paused ? "Resume WinNotch" : "Pause WinNotch";
        if (shownState == NotchState.Hidden) { timelineTimer.Stop(); clockTimer.Stop(); host.Bluetooth.SetPanelVisible(false); SetPassive(true); Hide(); return; }
        if (!IsVisible) Show();
        var expanded = shownState == NotchState.Expanded;
        var glass = host.Settings.Theme == AccentTheme.ObsidianGlass && !SystemParameters.HighContrast;
        // Keep the attached compact notch dark; the expanded or floating surface carries the glass tint.
        Notch.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush
            : glass && (expanded || host.Settings.Floating) ? (Brush)host.Resources["GlassSurfaceBrush"]
            : host.Settings.TrueBlack || glass ? Brushes.Black : (Brush)host.Resources["NotchBaseBrush"];
        Notch.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : glass ? (Brush)host.Resources["GlassEdgeBrush"] : Brushes.Transparent;
        Notch.BorderThickness = SystemParameters.HighContrast ? new Thickness(1) : glass ? new Thickness(1, host.Settings.Floating ? 1 : 0, 1, 1) : new Thickness(0);
        if (expanded) EnsureExpandedContent();
        SetPassive(!expanded);
        if (previous == NotchState.Expanded && !expanded && Win32.GetForegroundWindow() == handle && returnFocus != IntPtr.Zero)
            Win32.SetForegroundWindow(returnFocus);
        UpdateTimelineTimer();
        IdleContent.Visibility = shownState == NotchState.Idle ? Visibility.Visible : Visibility.Collapsed;
        UpdateClock();
        CompactContent.Visibility = shownState == NotchState.Compact ? Visibility.Visible : Visibility.Collapsed;
        PeekContent.Visibility = shownState == NotchState.Peek ? Visibility.Visible : Visibility.Collapsed;
        if (ExpandedContent != null) ExpandedContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
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
            FrameworkElement content = expanded ? ExpandedContent! : shownState == NotchState.Peek ? PeekContent : shownState == NotchState.Compact ? CompactContent : IdleContent;
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
        CompactArt.Source = media?.Artwork;
        CompactTitle.Text = media?.Title ?? "No media playing";
        CompactPlay.Content = media?.Playing == true ? "\uE769" : "\uE768";
        CompactPlay.IsEnabled = media?.CanPlayPause == true;
        ExpandedContent?.UpdateMedia(media);
        UpdateTimelineTimer();
    }

    internal void UpdateTimelineTimer()
    {
        if (shownState == NotchState.Expanded && ExpandedContent?.IsMediaPanel == true && host.Settings.Media && host.Media.Current?.Playing == true) timelineTimer.Start(); else timelineTimer.Stop();
        host.Bluetooth.SetPanelVisible(shownState == NotchState.Expanded && ExpandedContent?.IsBluetoothPanel == true && host.Settings.Bluetooth);
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

    public void UpdateBrightness(BrightnessSnapshot brightness) => ExpandedContent?.UpdateBrightness(brightness);
    public void UpdateNotification() => ExpandedContent?.UpdateNotification();
    public void UpdateCalendar() => ExpandedContent?.UpdateCalendar();
    public void UpdateDownloads() => ExpandedContent?.UpdateDownloads();
    public void UpdateBluetooth() => ExpandedContent?.UpdateBluetooth();
    public void UpdateAudio(AudioSnapshot audio) => ExpandedContent?.UpdateAudio(audio);

    public void UpdateTimer()
    {
        var timer = host.Timer;
        var active = host.Settings.Timer && timer.IsActive;
        var complete = timer.Status == TimerStatus.Completed;
        var paused = timer.Status == TimerStatus.Paused;
        var seconds = (int)Math.Ceiling(timer.Remaining.TotalSeconds);
        CompactMedia.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        CompactTimer.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        CompactTimerLabel.Text = timer.Label + (complete ? " complete" : paused ? " · Paused" : "");
        CompactCountdown.Text = complete ? "Done" : $"{seconds / 60:00}:{seconds % 60:00}";
        ExpandedContent?.UpdateTimer();
    }

    public void Expand(bool? showTimer = null)
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground != handle) returnFocus = foreground;
        if (host.State.Paused) host.TogglePause();
        host.State.Expand();
        if (host.State.Current.State != NotchState.Expanded) return;
        ExpandedContent!.SelectPanel(showTimer ?? host.Timer.IsActive);
        SetPassive(false); Activate();
        ExpandedContent.FocusPanel();
    }
    private void OnNotchClick(object sender, MouseButtonEventArgs e)
    {
        if (shownState == NotchState.Expanded) return;
        var notification = host.State.Current.Event?.Key == "notification";
        var calendar = host.State.Current.Event?.Key == "calendar";
        var download = host.State.Current.Event?.Key == "download";
        Expand();
        if (notification && host.State.Current.State == NotchState.Expanded) ExpandedContent!.ShowNotification();
        if (calendar && host.State.Current.State == NotchState.Expanded) { ExpandedContent!.SelectPanel("calendar"); ExpandedContent.FocusPanel(); }
        if (download && host.State.Current.State == NotchState.Expanded) { ExpandedContent!.SelectPanel("downloads"); ExpandedContent.FocusPanel(); }
    }
    private void OnEnter(object sender, MouseEventArgs e) { hover = true; if (shownState == NotchState.Idle) Render(host.State.Current); }
    private void OnLeave(object sender, MouseEventArgs e) { hover = false; if (shownState == NotchState.Idle) Render(host.State.Current); }
    private void OnDeactivated(object? sender, EventArgs e) { if (Notch.ContextMenu?.IsOpen != true) host.State.Collapse(); }
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { host.State.Collapse(); e.Handled = true; } }
    private async void OnPlay(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("play"); }
}

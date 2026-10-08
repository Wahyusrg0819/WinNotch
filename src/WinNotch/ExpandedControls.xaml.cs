using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Core;
using WinNotch.Modules;

namespace WinNotch;

public partial class ExpandedControls : UserControl
{
    private readonly App host;
    private bool timerTab, updatingAudio, updatingBrightness;
    private string panel = "media";
    private FrameworkElement? activePanel;
    private readonly Dictionary<string, FrameworkElement> namedControls = new();
    internal int LoadedPanelCount => ControlsRoot.Children.Count - 2;
    public new object FindName(string name) => namedControls.TryGetValue(name, out var control) ? control : base.FindName(name);
    private T Control<T>(string name) where T : FrameworkElement => (T)namedControls[name];
    private void RememberControls(FrameworkElement root)
    {
        if (root.Name.Length > 0) namedControls[root.Name] = root;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is FrameworkElement element) RememberControls(element);
    }
    internal bool IsMediaPanel => panel == "media";
    internal bool IsBluetoothPanel => panel == "bluetooth";

    internal ExpandedControls(App host)
    {
        this.host = host;
        InitializeComponent();
    }

    internal void ApplySettings()
    {
        TimerTab.Visibility = host.Settings.Timer ? Visibility.Visible : Visibility.Collapsed;
        VolumeControls.Visibility = host.Settings.Volume ? Visibility.Visible : Visibility.Collapsed;
        DisplayTab.Visibility = host.Settings.Brightness ? Visibility.Visible : Visibility.Collapsed;
        NotificationTab.Visibility = host.Settings.Notifications ? Visibility.Visible : Visibility.Collapsed;
        CalendarTab.Visibility = host.Settings.Calendar ? Visibility.Visible : Visibility.Collapsed;
        DownloadsTab.Visibility = host.Settings.Downloads ? Visibility.Visible : Visibility.Collapsed;
        BluetoothTab.Visibility = host.Settings.Bluetooth ? Visibility.Visible : Visibility.Collapsed;
        SelectPanel(panel);
    }

    internal void FocusPanel()
    {
        if (panel == "calendar") Control<Button>("ManageAgendaButton").Focus();
        else if (panel == "downloads") { if (Control<Button>("OpenDownloadsButton").IsEnabled) Control<Button>("OpenDownloadsButton").Focus(); else Control<Button>("RetryDownloadsButton").Focus(); }
        else if (panel == "bluetooth") { if (Control<Button>("ToggleBluetoothButton").IsEnabled) Control<Button>("ToggleBluetoothButton").Focus(); else Control<Button>("BluetoothSettingsButton").Focus(); }
        else if (panel == "brightness") { if (Control<Slider>("BrightnessSlider").IsEnabled) Control<Slider>("BrightnessSlider").Focus(); else DisplayTab.Focus(); }
        else if (panel == "notification") { if (Control<Button>("DismissNotificationButton").IsVisible) Control<Button>("DismissNotificationButton").Focus(); else NotificationTab.Focus(); }
        else if (timerTab) { if (!host.Timer.IsActive) Control<TextBox>("TimerMinutes").Focus(); else if (host.Timer.Status == TimerStatus.Completed) Control<Button>("CancelTimerButton").Focus(); else Control<Button>("PauseTimerButton").Focus(); }
        else if (Control<Button>("PlayButton").IsEnabled) Control<Button>("PlayButton").Focus(); else MediaTab.Focus();
    }

    internal void ShowNotification()
    {
        SelectPanel("notification"); Control<Button>("DismissNotificationButton").Focus();
    }

    public void UpdateMedia(MediaSnapshot? media)
    {
        if (!namedControls.ContainsKey("MediaPanel")) return;
        Control<Image>("ExpandedArt").Source = media?.Artwork;
        Control<TextBlock>("TrackTitle").Text = media?.Title ?? (host.Settings.Media ? "A little space for your music." : "Your quiet corner.");
        Control<TextBlock>("TrackArtist").Text = media == null ? (host.Settings.Media ? host.Media.Status : "Media is turned off in settings.") : string.IsNullOrWhiteSpace(media.Artist) ? "Windows media session" : media.Artist;
        Control<TextBlock>("TrackTitle").ToolTip = media?.Title;
        Control<TextBlock>("TrackArtist").ToolTip = media?.Artist;
        Control<Button>("PlayButton").Content = media?.Playing == true ? "\uE769" : "\uE768";
        Control<Button>("PlayButton").IsEnabled = media?.CanPlayPause == true;
        Control<Button>("PreviousButton").IsEnabled = media?.CanPrevious == true;
        Control<Button>("NextButton").IsEnabled = media?.CanNext == true;
        Control<TextBlock>("Elapsed").Text = FormatTime(media?.Position ?? TimeSpan.Zero);
        Control<TextBlock>("Duration").Text = FormatTime(media?.Duration ?? TimeSpan.Zero);
        Control<ProgressBar>("Timeline").Value = media?.Duration.TotalSeconds > 0 ? media.Position.TotalSeconds / media.Duration.TotalSeconds : 0;
        host.Overlay.UpdateTimelineTimer();
    }

    internal void SelectPanel(bool showTimer)
        => SelectPanel(showTimer ? "timer" : "media");

    internal void SelectPanel(string selected)
    {
        if ((selected == "timer" && !host.Settings.Timer) || (selected == "brightness" && !host.Settings.Brightness) || (selected == "notification" && !host.Settings.Notifications) || (selected == "calendar" && !host.Settings.Calendar) || (selected == "downloads" && !host.Settings.Downloads) || (selected == "bluetooth" && !host.Settings.Bluetooth)) selected = "media";
        if (activePanel != null && selected == panel) { host.Overlay.UpdateTimelineTimer(); return; }
        panel = selected;
        timerTab = selected == "timer";
        if (activePanel != null) activePanel.Visibility = Visibility.Collapsed;
        var key = selected switch { "timer" => "TimerPanel", "brightness" => "BrightnessPanel", "notification" => "NotificationPanel",
            "calendar" => "CalendarPanel", "downloads" => "DownloadsPanel", "bluetooth" => "BluetoothPanel", _ => "MediaPanel" };
        // WPF defers compiled resource creation until lookup, then reuses the same controls and input.
        activePanel = (FrameworkElement)Resources[key];
        if (activePanel.Parent == null) { RememberControls(activePanel); ControlsRoot.Children.Add(activePanel); }
        activePanel.Visibility = Visibility.Visible;
        switch (selected)
        {
            case "timer": UpdateTimer(); break;
            case "brightness": UpdateBrightness(host.Brightness.Current); break;
            case "notification": UpdateNotification(); break;
            case "calendar": UpdateCalendar(); break;
            case "downloads": UpdateDownloads(); break;
            case "bluetooth": UpdateBluetooth(); break;
            default: UpdateMedia(host.Settings.Media ? host.Media.Current : null); break;
        }
        foreach (var (tab, name) in new[] { (MediaTab, "media"), (TimerTab, "timer"), (DisplayTab, "brightness"), (NotificationTab, "notification"), (CalendarTab, "calendar"), (DownloadsTab, "downloads"), (BluetoothTab, "bluetooth") })
        {
            tab.SetResourceReference(BackgroundProperty, selected == name ? "CardBrush" : "CanvasBrush");
            tab.SetResourceReference(ForegroundProperty, selected == name ? "AccentBrush" : "MutedBrush");
        }
        host.Overlay.UpdateTimelineTimer();
    }

    public void UpdateBrightness(BrightnessSnapshot brightness)
    {
        if (!namedControls.ContainsKey("BrightnessPanel")) return;
        updatingBrightness = true;
        try
        {
            Control<Slider>("BrightnessSlider").IsEnabled = host.Settings.Brightness && brightness.Available;
            Control<Slider>("BrightnessSlider").Value = brightness.Percent;
            Control<TextBlock>("BrightnessPercent").Text = brightness.Available ? $"{brightness.Percent}%" : "—";
            Control<TextBlock>("BrightnessStatus").Text = brightness.Available ? "Set a comfortable brightness." : "Brightness control is unavailable on this screen. External monitors are not supported yet.";
        }
        finally { updatingBrightness = false; }
    }

    public void UpdateNotification()
    {
        if (!namedControls.ContainsKey("NotificationPanel")) return;
        var preview = host.LatestNotification;
        Control<TextBlock>("NotificationApp").Text = preview?.App ?? "NOTIFICATION PREVIEWS";
        Control<TextBlock>("NotificationTitle").Text = preview?.Title ?? "Nothing new here.";
        Control<TextBlock>("NotificationBody").Text = preview?.Body ?? "New notifications appear after access is allowed in Settings. The originals stay in Windows.";
        Control<Button>("DismissNotificationButton").Visibility = preview == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void UpdateCalendar()
    {
        if (!namedControls.ContainsKey("CalendarPanel")) return;
        var next = host.Calendar.Next;
        Control<TextBlock>("CalendarTitle").Text = host.Calendar.LoadFailed ? "Agenda unavailable" : next?.Title ?? "A little room in your day.";
        Control<TextBlock>("CalendarTitle").ToolTip = next?.Title;
        Control<TextBlock>("CalendarWhen").Text = host.Calendar.LoadFailed ? "The saved agenda could not be read. Open Manage agenda for details." : host.CalendarError.Length > 0 ? host.CalendarError : next?.When ?? "Add an event to see what is coming next.";
    }
    public void UpdateDownloads()
    {
        if (!namedControls.ContainsKey("DownloadsPanel")) return;
        Control<TextBlock>("DownloadsStatus").Text = host.Downloads.Status; Control<TextBlock>("DownloadsStatus").ToolTip = host.Downloads.Status;
        if (!ReferenceEquals(Control<ItemsControl>("DownloadsList").ItemsSource, host.Downloads.Items)) Control<ItemsControl>("DownloadsList").ItemsSource = host.Downloads.Items;
        Control<TextBlock>("DownloadsEmpty").Visibility = host.Downloads.Items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Control<TextBlock>("DownloadsEmpty").Text = host.Downloads.IsWatching ? "No download activity yet." : "Choose a downloads folder in Settings.";
        Control<Button>("OpenDownloadsButton").IsEnabled = host.Downloads.IsWatching;
        Control<Button>("RetryDownloadsButton").Visibility = host.Downloads.IsWatching ? Visibility.Collapsed : Visibility.Visible;
    }

    public void UpdateBluetooth()
    {
        if (!namedControls.ContainsKey("BluetoothPanel")) return;
        var bluetooth = host.Bluetooth;
        Control<TextBlock>("BluetoothStatus").Text = bluetooth.IsReading ? "Refreshing Bluetooth…" : bluetooth.Status;
        if (!ReferenceEquals(Control<ItemsControl>("BluetoothList").ItemsSource, bluetooth.Current.Devices)) Control<ItemsControl>("BluetoothList").ItemsSource = bluetooth.Current.Devices;
        Control<TextBlock>("BluetoothEmpty").Visibility = bluetooth.Current.Devices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Control<TextBlock>("BluetoothActionStatus").Text = bluetooth.ActionStatus;
        Control<TextBlock>("BluetoothActionStatus").Visibility = bluetooth.ActionStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Control<Button>("ToggleBluetoothButton").Content = bluetooth.IsChanging ? "Waiting…" : bluetooth.IsOn ? "Turn off" : "Turn on";
        Control<Button>("ToggleBluetoothButton").IsEnabled = bluetooth.CanChange;
        System.Windows.Automation.AutomationProperties.SetName(Control<Button>("ToggleBluetoothButton"), bluetooth.IsOn ? "Turn Bluetooth off" : "Turn Bluetooth on");
        Control<Button>("RefreshBluetoothButton").IsEnabled = host.Settings.Bluetooth && !bluetooth.IsReading && !bluetooth.IsChanging;
    }

    public void UpdateTimer()
    {
        var timer = host.Timer;
        if (timer.Status == TimerStatus.Completed && !timerTab && host.Settings.Timer && host.State.Current.State == NotchState.Expanded) SelectPanel(true);
        if (!namedControls.ContainsKey("TimerPanel")) return;
        var active = host.Settings.Timer && timer.IsActive;
        var complete = timer.Status == TimerStatus.Completed;
        var moveFocus = complete && Control<Button>("PauseTimerButton").IsKeyboardFocusWithin;
        var paused = timer.Status == TimerStatus.Paused;
        var seconds = (int)Math.Ceiling(timer.Remaining.TotalSeconds);
        var countdown = $"{seconds / 60:00}:{seconds % 60:00}";
        Control<StackPanel>("TimerSetup").Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        Control<StackPanel>("TimerActive").Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        Control<TextBlock>("TimerPhase").Text = timer.Label;
        Control<TextBlock>("TimerCountdown").Text = countdown;
        Control<TextBlock>("TimerStatusText").Text = complete ? "Session complete. Take your next step." : paused ? "Paused · Continue when you are ready." : timer.Mode == TimerMode.Custom ? "Time for what matters." : $"Focus sessions completed: {timer.FocusSessions}";
        Control<Button>("PauseTimerButton").Content = paused ? "Resume" : "Pause";
        Control<Button>("PauseTimerButton").Visibility = complete ? Visibility.Collapsed : Visibility.Visible;
        Control<Button>("NextTimerButton").Visibility = complete && timer.Mode != TimerMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        Control<Button>("NextTimerButton").Content = timer.NextMode switch { TimerMode.ShortBreak => "Start break · 5m", TimerMode.LongBreak => "Long break · 15m", _ => "Start focus · 25m" };
        Control<Button>("CancelTimerButton").Content = complete ? "Done" : "Cancel";
        if (moveFocus) { if (timer.Mode == TimerMode.Custom) Control<Button>("CancelTimerButton").Focus(); else Control<Button>("NextTimerButton").Focus(); }
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
    private async void OnPlay(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("play"); }
    private async void OnPrevious(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("previous"); }
    private async void OnNext(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("next"); }
    private void OnSettings(object sender, RoutedEventArgs e) { e.Handled = true; host.OpenSettings(); }
    private void OnMediaTab(object sender, RoutedEventArgs e) => SelectPanel(false);
    private void OnTimerTab(object sender, RoutedEventArgs e) => SelectPanel(true);
    private void OnDisplayTab(object sender, RoutedEventArgs e) => SelectPanel("brightness");
    private void OnNotificationTab(object sender, RoutedEventArgs e) => SelectPanel("notification");
    private void OnCalendarTab(object sender, RoutedEventArgs e) => SelectPanel("calendar");
    private void OnManageAgenda(object sender, RoutedEventArgs e) => host.OpenAgenda();
    private void OnDownloadsTab(object sender, RoutedEventArgs e) => SelectPanel("downloads");
    private void OnBluetoothTab(object sender, RoutedEventArgs e) => SelectPanel("bluetooth");
    private async void OnToggleBluetooth(object sender, RoutedEventArgs e) => await host.Bluetooth.ToggleAsync();
    private async void OnRefreshBluetooth(object sender, RoutedEventArgs e) => await host.Bluetooth.RefreshAsync();
    private void OnBluetoothSettings(object sender, RoutedEventArgs e) => host.OpenBluetoothSettings();
    private void OnOpenDownloads(object sender, RoutedEventArgs e) => host.OpenDownloadsFolder();
    private void OnRetryDownloads(object sender, RoutedEventArgs e) => host.Downloads.Configure(host.Settings.Downloads, host.Settings.DownloadsFolder, true);
    private void OnDismissNotification(object sender, RoutedEventArgs e) { host.DismissNotification(); NotificationTab.Focus(); }
    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!updatingBrightness && IsInitialized && host.Settings.Brightness) host.Brightness.SetBrightness(e.NewValue);
    }
    private void OnStartTimer(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Control<TextBox>("TimerMinutes").Text, out var minutes) || minutes is < 1 or > 180)
        { Control<TextBlock>("TimerHint").Text = "Enter a whole number from 1 to 180."; Control<TextBox>("TimerMinutes").Focus(); return; }
        host.Timer.Start(TimeSpan.FromMinutes(minutes)); Control<Button>("PauseTimerButton").Focus();
    }
    private void OnTimerPreset(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string mode } && Enum.TryParse<TimerMode>(mode, out var value))
        { host.Timer.StartPomodoro(value); Control<Button>("PauseTimerButton").Focus(); }
    }
    private void OnPauseTimer(object sender, RoutedEventArgs e) => host.Timer.TogglePause();
    private void OnNextTimer(object sender, RoutedEventArgs e) { host.Timer.NextPomodoro(); Control<Button>("PauseTimerButton").Focus(); }
    private void OnCancelTimer(object sender, RoutedEventArgs e)
    { host.Timer.Cancel(); Control<TextBlock>("TimerHint").Text = "Choose a preset or enter 1–180 minutes."; Control<TextBox>("TimerMinutes").Focus(); }
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

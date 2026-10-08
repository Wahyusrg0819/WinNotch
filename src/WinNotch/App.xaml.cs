using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinNotch.Core;
using WinNotch.Modules;
using WinNotch.Native;

namespace WinNotch;

public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? showSettingsSignal;
    private RegisteredWaitHandle? signalRegistration;
    private TrayIcon? tray;
    private ForegroundService? foreground;
    private DispatcherTimer? expiryTimer;
    private DispatcherTimer? countdownTick;
    private DispatcherTimer? calendarTick;
    private SettingsWindow? settingsWindow;
    private AgendaWindow? agendaWindow;
    private bool fullscreen, exiting;
    public NotchSettings Settings { get; private set; } = SettingsStore.Load();
    public NotchStateManager State { get; } = new();
    public MediaService Media { get; private set; } = null!;
    public BatteryService Battery { get; private set; } = null!;
    public AudioService Audio { get; private set; } = null!;
    public BrightnessService Brightness { get; private set; } = null!;
    public NotificationService Notifications { get; private set; } = null!;
    public DownloadService Downloads { get; private set; } = null!;
    internal BluetoothService Bluetooth { get; set; } = null!;
    public NotificationPreview? LatestNotification { get; private set; }
    public CountdownTimer Timer { get; } = new();
    internal LocalCalendar Calendar { get; private set; } = null!;
    internal string CalendarError { get; private set; } = "";
    internal bool CalendarPolling => calendarTick?.IsEnabled == true;
    public OverlayWindow Overlay { get; private set; } = null!;
    internal bool IsTrayRegistered => tray?.IsRegistered == true;
    internal (IntPtr Handle, Win32.MonitorInfo Info) TargetMonitor => foreground?.TargetMonitor ?? Win32.SelectMonitor(Settings, Win32.GetForegroundWindow());
    internal string LastAppName => foreground?.LastAppName ?? "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var restartIndex = Array.IndexOf(e.Args, "--restart-from");
        if (restartIndex >= 0 && restartIndex + 1 < e.Args.Length && int.TryParse(e.Args[restartIndex + 1], out var parentId) && parentId != Environment.ProcessId)
        {
            try { using var parent = Process.GetProcessById(parentId); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (ArgumentException) { }
            catch (TimeoutException) { Shutdown(1); return; }
        }
        mutex = new Mutex(true, @"Local\WinNotch.Singleton", out var first);
        if (!first)
        {
            try { using var signal = EventWaitHandle.OpenExisting(@"Local\WinNotch.OpenSettings"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WinNotch.OpenSettings");
        signalRegistration = ThreadPool.RegisterWaitForSingleObject(showSettingsSignal, (_, _) => Dispatcher.BeginInvoke(OpenSettings), null, -1, false);
        try
        {
            // This small translucent scene avoids a large GPU allocation by using WPF's software renderer.
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            ApplySystemColors();
            SystemParameters.StaticPropertyChanged += OnSystemPreference;
            Media = new MediaService(Dispatcher);
            Battery = new BatteryService(Dispatcher);
            Audio = new AudioService(Dispatcher);
            Brightness = new BrightnessService(Dispatcher);
            Notifications = new NotificationService(Dispatcher);
            Downloads = new DownloadService(Dispatcher);
            Bluetooth = new BluetoothService(Dispatcher);
            var smokeIndex = Array.IndexOf(e.Args, "--smoke-test");
            var fixtureIndex = Array.FindIndex(e.Args, argument => argument is "--smoke-test" or "--performance-test");
            var calendarPath = fixtureIndex >= 0 ? Path.Combine(fixtureIndex + 1 < e.Args.Length ? e.Args[fixtureIndex + 1] : Path.Combine(AppContext.BaseDirectory, "smoke-test"), $"calendar-{Guid.NewGuid():N}.json")
                : Path.Combine(SettingsStore.DataDirectory, "agenda.json");
            Calendar = new LocalCalendar(calendarPath);
            Overlay = new OverlayWindow(this);
            MainWindow = Overlay;
            State.Visibility = Settings.Visibility;
            State.FullscreenBehavior = Settings.EffectiveFullscreen;
            State.Changed += presentation =>
            {
                Overlay.Render(presentation); UpdateExpiryTimer();
                if (!State.AcceptsEvent(80) && LatestNotification != null) DismissNotification();
            };
            expiryTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background,
                (_, _) => { State.Refresh(); UpdateExpiryTimer(); }, Dispatcher);
            expiryTimer.Stop();
            countdownTick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Timer.Tick(), Dispatcher);
            countdownTick.Stop();
            calendarTick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(15) };
            calendarTick.Tick += (_, _) => RefreshCalendar();
            Timer.Changed += OnTimer;
            Timer.Completed += () => Publish("timer", "\uE916", $"{Timer.Label} complete", "Open to continue", 90, 6);
            Media.Changed += OnMedia;
            Battery.Changed += OnBattery;
            Audio.Changed += OnAudio;
            Brightness.Changed += (brightness, changed) =>
            {
                Overlay.UpdateBrightness(brightness);
                if (Settings.Brightness && changed && brightness.Available)
                    Publish("brightness", "\uE706", "Brightness", $"{brightness.Percent}%", 50, 2, brightness.Percent);
            };
            Notifications.Changed += OnNotification;
            Downloads.Changed += () => Overlay.UpdateDownloads();
            Bluetooth.Changed += () => Overlay.UpdateBluetooth();
            Downloads.Completed += files =>
            {
                if (Settings.Downloads) Publish("download", "\uE896", "File ready", files.Length == 1 ? files[0].Name : $"{files.Length} files · Open Downloads", 65, 5);
            };
            Overlay.Show();
            Overlay.ApplySettings();
            Overlay.UpdateMedia(null);
            Overlay.UpdateAudio(Audio.Current);
            Overlay.UpdateTimer();
            Brightness.SetEnabled(Settings.Brightness);
            Bluetooth.SetEnabled(Settings.Bluetooth);
            if (!e.Args.Contains("--smoke-test")) Notifications.SetEnabled(Settings.Notifications);
            Media.SetEnabled(Settings.Media);
            CreateTray();
            foreground = new ForegroundService(Dispatcher, new System.Windows.Interop.WindowInteropHelper(Overlay).Handle);
            foreground.Settings = Settings;
            foreground.Changed += (hidden, maximized) =>
            {
                fullscreen = hidden; State.MaximizedApp = maximized;
                State.Fullscreen = hidden;
                State.Suppressed = foreground.IsExcluded || foreground.IsPresentation; State.Refresh();
                if (!State.AcceptsEvent(80)) DismissNotification();
            };
            foreground.DisplayChanged += Overlay.Reposition;
            foreground.Check();
            if (smokeIndex < 0) Downloads.Configure(Settings.Downloads, Settings.DownloadsFolder);
            RefreshCalendar();
            await Media.StartAsync();
            if (exiting) return;
            if (e.Args.Contains("--smoke-test"))
            {
                var index = Array.IndexOf(e.Args, "--smoke-test");
                var output = index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "smoke-test");
                await SmokeTest.RunAsync(this, output);
                Shutdown();
            }
            else if (e.Args.Contains("--performance-test"))
            {
                var index = Array.IndexOf(e.Args, "--performance-test");
                var output = index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "performance-test");
                await PerformanceValidation.RunAsync(this, output); Shutdown();
            }
            else if (e.Args.Contains("--bluetooth-read-test"))
            {
                var index = Array.IndexOf(e.Args, "--bluetooth-read-test");
                var output = index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "bluetooth-test");
                Shutdown(await BluetoothValidation.ReadLiveAsync(output) ? 0 : 1);
            }
            else if (e.Args.Contains("--notification-test"))
            {
                var index = Array.IndexOf(e.Args, "--notification-test");
                if (index + 2 >= e.Args.Length) throw new ArgumentException("--notification-test needs an output directory and the test sender script path.");
                OpenSettings();
                await NotificationValidation.RunLiveAsync(this, e.Args[index + 1], e.Args[index + 2]);
                Shutdown();
            }
            else if (e.Args.Contains("--settings")) OpenSettings();
        }
        catch (Exception ex)
        {
            SettingsStore.Log("app.startup", ex);
            MessageBox.Show($"WinNotch couldn't start ({ex.GetType().Name}).\nDiagnostics: {SettingsStore.DataDirectory}", "WinNotch", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void UpdateExpiryTimer() { if (State.HasEvents) expiryTimer?.Start(); else expiryTimer?.Stop(); }
    private void Publish(string key, string glyph, string title, string detail, int priority, double seconds = 3, double? level = null)
    {
        State.Publish(key, glyph, title, detail, priority, TimeSpan.FromSeconds(seconds), level); UpdateExpiryTimer();
    }
    private void OnMedia(MediaSnapshot? media, bool trackChanged)
    {
        State.HasMedia = Settings.Media && media != null;
        Overlay.UpdateMedia(Settings.Media ? media : null);
        if (Settings.Media && trackChanged && media != null) Publish("media", "\uE8D6", media.Title, "", 30);
        State.Refresh();
    }
    private void OnAudio(AudioSnapshot audio, bool changed)
    {
        Overlay.UpdateAudio(audio);
        if (Settings.Volume && changed && audio.Available)
            Publish("volume", audio.Muted ? "\uE74F" : "\uE767", audio.Muted ? "Muted" : "Volume", audio.Muted ? "" : $"{audio.Percent}%", 50, 2, audio.Muted ? 0 : audio.Percent);
    }
    private void OnTimer()
    {
        State.HasTimer = Settings.Timer && Timer.IsActive;
        if (Timer.Status == TimerStatus.Running) countdownTick?.Start(); else countdownTick?.Stop();
        if (Timer.Status != TimerStatus.Completed) State.Remove("timer");
        Overlay.UpdateTimer();
        State.Refresh();
    }
    private void OnBattery(BatterySnapshot previous, BatterySnapshot battery)
    {
        if (!Settings.Battery || !battery.Present) return;
        if (battery.Percent <= 15 && previous.Percent > 15 && !battery.PluggedIn)
            Publish("battery", "\uE7BA", "Battery running low", $"{battery.Percent}%", 100, 5);
        else if (battery.PluggedIn != previous.PluggedIn)
            Publish("battery", "\uE945", battery.PluggedIn ? (battery.Charging ? "Charging" : "Plugged in") : "On battery", $"{battery.Percent}%", 70);
        else if (battery.Percent == 100 && previous.Percent < 100 && battery.PluggedIn)
            Publish("battery", "\uE73E", "Fully charged", "100%", 70);
        else if (battery.Saver != previous.Saver)
            Publish("battery", "\uE8BE", battery.Saver ? "Battery saver on" : "Battery saver off", $"{battery.Percent}%", 70);
    }
    private void CreateTray()
    {
        tray = new TrayIcon(Overlay, Overlay.TrayMenu, OpenSettings);
    }
    public void TogglePause()
    {
        State.Paused = !State.Paused; State.Refresh();
        if (State.Paused) DismissNotification();
    }
    public void ApplySettings(NotchSettings settings)
    {
        Settings = settings;
        if (foreground != null) { foreground.Settings = settings; foreground.Check(true, true); }
        Media.SetEnabled(settings.Media);
        State.Visibility = settings.Visibility;
        State.FullscreenBehavior = settings.EffectiveFullscreen;
        State.HasMedia = settings.Media && Media.Current != null;
        State.Fullscreen = fullscreen;
        State.Suppressed = foreground?.IsExcluded == true || foreground?.IsPresentation == true;
        if (!settings.Media) State.Remove("media");
        if (!settings.Battery) State.Remove("battery");
        if (!settings.Volume) State.Remove("volume");
        if (!settings.Timer) Timer.Cancel();
        if (!settings.Brightness) State.Remove("brightness");
        Brightness.SetEnabled(settings.Brightness);
        Notifications.SetEnabled(settings.Notifications);
        State.Remove("download");
        Downloads.Configure(settings.Downloads, settings.DownloadsFolder);
        Bluetooth.SetEnabled(settings.Bluetooth);
        RefreshCalendar();
        if (!settings.Notifications || !State.AcceptsEvent(80)) DismissNotification();
        ApplySystemColors();
        State.Refresh(); Overlay.ApplySettings(); Overlay.UpdateMedia(settings.Media ? Media.Current : null);
        Overlay.UpdateAudio(Audio.Current); Overlay.UpdateTimer(); Overlay.UpdateBrightness(Brightness.Current); Overlay.UpdateNotification();
    }
    internal void OnNotification(NotificationPreview? preview)
    {
        if (preview == null) { DismissNotification(); return; }
        if (!Settings.Notifications || !State.AcceptsEvent(80)) return;
        LatestNotification = preview;
        Overlay.UpdateNotification();
        Publish("notification", "\uE7E7", preview.App, preview.Title, 80, 5);
    }
    internal void DismissNotification()
    {
        LatestNotification = null; State.Remove("notification"); Overlay.UpdateNotification();
    }
    internal void RefreshCalendar()
    {
        State.Remove("calendar");
        if (Settings.Calendar && !Calendar.LoadFailed)
        {
            try
            {
                var due = Calendar.TakeDueReminders();
                CalendarError = "";
                if (due.Length > 0)
                {
                    var entry = due[0];
                    Publish("calendar", "\uE787", entry.Title, $"{entry.StartsAt.ToLocalTime():HH:mm}" + (due.Length > 1 ? $" · +{due.Length - 1} more in Agenda" : " · Starting soon"), 80, 8);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (CalendarError.Length == 0) SettingsStore.Log("calendar.reminder", ex);
                CalendarError = "Could not save reminder status. Check folder permissions; WinNotch will retry.";
            }
        }
        if (Settings.Calendar && !Calendar.LoadFailed && Calendar.Next != null) calendarTick?.Start(); else calendarTick?.Stop();
        Overlay.UpdateCalendar();
    }
    public void OpenAgenda()
    {
        if (exiting) return;
        State.Collapse();
        if (agendaWindow == null) { agendaWindow = new AgendaWindow(this); agendaWindow.Closed += (_, _) => agendaWindow = null; }
        agendaWindow.Show(); agendaWindow.WindowState = WindowState.Normal; agendaWindow.Activate();
    }
    internal void OpenDownloadsFolder()
    {
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
            info.ArgumentList.Add(Downloads.Folder);
            using var process = Process.Start(info);
        }
        catch (Exception ex)
        {
            SettingsStore.Log("downloads.openFolder", ex);
            MessageBox.Show("Could not open the folder. Check the Downloads folder in Settings.", "WinNotch", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    internal void OpenBluetoothSettings()
    {
        try { using var process = Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); }
        catch (Exception ex)
        {
            SettingsStore.Log("bluetooth.openSettings", ex);
            MessageBox.Show("Open Windows Settings > Bluetooth & devices.", "WinNotch", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    public void Restart()
    {
        if (Timer.IsActive && MessageBox.Show("Restarting will cancel the current timer. Restart WinNotch?", "WinNotch", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException();
            using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, Arguments = $"--restart-from {Environment.ProcessId} --background" });
            if (process == null) throw new InvalidOperationException();
            Shutdown();
        }
        catch (Exception ex)
        {
            SettingsStore.Log("app.restart", ex);
            MessageBox.Show("WinNotch could not restart. Your current session is still open.", "WinNotch", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    internal void ExcludeApp(string app)
    {
        try
        {
            var updated = Settings with { ExcludedApps = DesktopPolicy.NormalizeApps(Settings.ExcludedApps.Append(app)) };
            SettingsStore.Save(updated); ApplySettings(updated);
            settingsWindow?.ReloadExclusions();
        }
        catch (Exception ex) { SettingsStore.Log("settings.exclude", ex); OpenSettings(); }
    }
    public void OpenSettings()
    {
        if (exiting || Overlay == null) return;
        State.Collapse();
        if (settingsWindow == null) { settingsWindow = new SettingsWindow(this); settingsWindow.Closed += (_, _) => settingsWindow = null; }
        settingsWindow.Show(); settingsWindow.WindowState = WindowState.Normal; settingsWindow.Activate();
    }
    private void OnSystemPreference(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation))
            Dispatcher.BeginInvoke(() => { ApplySystemColors(); Overlay?.ApplySettings(); });
    }
    private void ApplySystemColors()
    {
        var contrast = SystemParameters.HighContrast;
        var glass = Settings.Theme == AccentTheme.ObsidianGlass;
        System.Windows.Media.Brush Brush(string hex) => (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
        Resources["CanvasBrush"] = contrast ? SystemColors.WindowBrush : Brush(glass ? "#10151D" : "#101112");
        Resources["CardBrush"] = contrast ? SystemColors.WindowBrush : Brush(glass ? "#303E50" : "#191B1D");
        Resources["TextBrush"] = contrast ? SystemColors.WindowTextBrush : Brush("#F4F5F5");
        Resources["MutedBrush"] = contrast ? SystemColors.WindowTextBrush : Brush(glass ? "#B1BECE" : "#999FA3");
        Resources["AccentBrush"] = contrast ? SystemColors.HighlightBrush : Brush(Settings.Theme switch { AccentTheme.Ice or AccentTheme.ObsidianGlass => "#AAD9FF", AccentTheme.Amber => "#FFD396", _ => "#C8F7AD" });
        Resources["LineBrush"] = contrast ? SystemColors.WindowTextBrush : Brush(glass ? "#435369" : "#2C2F31");
        Resources["ButtonBrush"] = contrast ? SystemColors.WindowBrush : glass ? Resources["GlassButtonBrush"] : Brush("#222528");
        Resources["ButtonLineBrush"] = contrast ? SystemColors.WindowTextBrush : glass ? Resources["GlassEdgeBrush"] : System.Windows.Media.Brushes.Transparent;
        Resources["ButtonBorderThickness"] = new Thickness(contrast || glass ? 1 : 0);
        Resources["SidebarBrush"] = contrast ? SystemColors.WindowBrush : Brush(glass ? "#171F2A" : "#151719");
        Resources["SelectionBrush"] = contrast ? SystemColors.WindowBrush : Brush(glass ? "#303E50" : "#252C24");
        Resources["AccentSurfaceBrush"] = contrast ? SystemColors.WindowBrush : Brush(glass ? "#31465C" : "#253320");
        Resources["ArtworkSurfaceBrush"] = contrast ? SystemColors.WindowBrush : Brush(glass ? "#273444" : "#202520");
    }
    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        SystemParameters.StaticPropertyChanged -= OnSystemPreference;
        signalRegistration?.Unregister(null); showSettingsSignal?.Dispose();
        expiryTimer?.Stop(); countdownTick?.Stop(); calendarTick?.Stop(); foreground?.Dispose(); Media?.Dispose(); Battery?.Dispose(); Audio?.Dispose(); Brightness?.Dispose(); Notifications?.Dispose(); Downloads?.Dispose();
        Bluetooth?.Dispose(); tray?.Dispose();
        mutex?.Dispose(); base.OnExit(e);
    }
}

using System;
using System.IO;
using System.Linq;
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
    private SettingsWindow? settingsWindow;
    private bool fullscreen, exiting;
    public NotchSettings Settings { get; private set; } = SettingsStore.Load();
    public NotchStateManager State { get; } = new();
    public MediaService Media { get; private set; } = null!;
    public BatteryService Battery { get; private set; } = null!;
    public AudioService Audio { get; private set; } = null!;
    public OverlayWindow Overlay { get; private set; } = null!;
    internal bool IsTrayRegistered => tray?.IsRegistered == true;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
            Overlay = new OverlayWindow(this);
            MainWindow = Overlay;
            State.Visibility = Settings.Visibility;
            State.Changed += presentation => { Overlay.Render(presentation); UpdateExpiryTimer(); };
            expiryTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background,
                (_, _) => { State.Refresh(); UpdateExpiryTimer(); }, Dispatcher);
            expiryTimer.Stop();
            Media.Changed += OnMedia;
            Battery.Changed += OnBattery;
            Audio.Changed += OnAudio;
            Overlay.Show();
            Overlay.ApplySettings();
            Overlay.UpdateMedia(null);
            Media.SetEnabled(Settings.Media);
            CreateTray();
            foreground = new ForegroundService(Dispatcher, new System.Windows.Interop.WindowInteropHelper(Overlay).Handle);
            foreground.Changed += (hidden, maximized) =>
            {
                fullscreen = hidden; State.MaximizedApp = maximized;
                State.Suppressed = Settings.HideInFullscreen && hidden; State.Refresh();
            };
            foreground.DisplayChanged += Overlay.Reposition;
            foreground.Check();
            await Media.StartAsync();
            if (exiting) return;
            if (e.Args.Contains("--smoke-test"))
            {
                var index = Array.IndexOf(e.Args, "--smoke-test");
                var output = index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "smoke-test");
                await SmokeTest.RunAsync(this, output);
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
        if (Settings.Volume && changed && audio.Available)
            Publish("volume", audio.Muted ? "\uE74F" : "\uE767", audio.Muted ? "Muted" : "Volume", audio.Muted ? "" : $"{audio.Percent}%", 50, 2, audio.Muted ? 0 : audio.Percent);
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
    }
    public void ApplySettings(NotchSettings settings)
    {
        Settings = settings;
        Media.SetEnabled(settings.Media);
        State.Visibility = settings.Visibility;
        State.HasMedia = settings.Media && Media.Current != null;
        State.Suppressed = settings.HideInFullscreen && fullscreen;
        if (!settings.Media) State.Remove("media");
        if (!settings.Battery) State.Remove("battery");
        if (!settings.Volume) State.Remove("volume");
        State.Refresh(); Overlay.ApplySettings(); Overlay.UpdateMedia(settings.Media ? Media.Current : null);
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
        System.Windows.Media.Brush Brush(string hex) => (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
        Resources["CanvasBrush"] = contrast ? SystemColors.WindowBrush : Brush("#101112");
        Resources["CardBrush"] = contrast ? SystemColors.WindowBrush : Brush("#191B1D");
        Resources["TextBrush"] = contrast ? SystemColors.WindowTextBrush : Brush("#F4F5F5");
        Resources["MutedBrush"] = contrast ? SystemColors.WindowTextBrush : Brush("#999FA3");
        Resources["AccentBrush"] = contrast ? SystemColors.HighlightBrush : Brush("#C8F7AD");
        Resources["LineBrush"] = contrast ? SystemColors.WindowTextBrush : Brush("#2C2F31");
    }
    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        SystemParameters.StaticPropertyChanged -= OnSystemPreference;
        signalRegistration?.Unregister(null); showSettingsSignal?.Dispose();
        expiryTimer?.Stop(); foreground?.Dispose(); Media?.Dispose(); Battery?.Dispose(); Audio?.Dispose();
        tray?.Dispose();
        mutex?.Dispose(); base.OnExit(e);
    }
}

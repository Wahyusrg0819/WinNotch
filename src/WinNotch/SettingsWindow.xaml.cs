using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using WinNotch.Core;
using WinNotch.Native;

namespace WinNotch;

public partial class SettingsWindow : Window
{
    private readonly App host;
    private readonly DispatcherTimer statusTimer;
    public SettingsWindow(App host)
    {
        this.host = host;
        InitializeComponent();
        SourceInitialized += (_, _) => { var dark = 1; Win32.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        var s = host.Settings;
        StartupCheck.IsChecked = SettingsStore.StartupEnabled();
        TopmostCheck.IsChecked = s.AlwaysOnTop; FullscreenCheck.IsChecked = s.HideInFullscreen;
        AnimationCheck.IsChecked = s.Animations; MediaCheck.IsChecked = s.Media;
        BatteryCheck.IsChecked = s.Battery; VolumeCheck.IsChecked = s.Volume;
        ShapeSelect.SelectedIndex = s.Floating ? 1 : 0;
        SizeSelect.SelectedIndex = s.Scale < 1 ? 0 : s.Scale > 1 ? 2 : 1;
        VisibilitySelect.SelectedIndex = (int)s.Visibility;
        UpdateStatus();
        statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => UpdateStatus(), Dispatcher);
        Closed += (_, _) => statusTimer.Stop();
    }

    private void UpdateStatus()
    {
        var battery = host.Battery.Current;
        var audio = host.Audio.Current;
        DeviceStatus.Text = (battery.Present ? $"Battery {battery.Percent}%{(battery.PluggedIn ? " · Plugged in" : "")}" : "Desktop power") +
            (audio.Available ? $"   /   {(audio.Muted ? "Audio muted" : $"Volume {audio.Percent}%")}" : "   /   No audio output");
        MediaStatus.Text = host.Settings.Media ? host.Media.Status : "Media module is turned off.";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var settings = new NotchSettings
        {
            AlwaysOnTop = TopmostCheck.IsChecked == true, HideInFullscreen = FullscreenCheck.IsChecked == true,
            Animations = AnimationCheck.IsChecked == true, Floating = ShapeSelect.SelectedIndex == 1,
            Scale = SizeSelect.SelectedIndex switch { 0 => 0.85, 2 => 1.15, _ => 1 },
            Visibility = (VisibilityMode)Math.Max(0, VisibilitySelect.SelectedIndex),
            Media = MediaCheck.IsChecked == true, Battery = BatteryCheck.IsChecked == true, Volume = VolumeCheck.IsChecked == true
        };
        try
        {
            if (SettingsStore.StartupEnabled() != (StartupCheck.IsChecked == true)) SettingsStore.SetStartup(StartupCheck.IsChecked == true);
            SettingsStore.Save(settings);
            host.ApplySettings(settings);
            SaveStatus.Text = "Saved. Make yourself at home.";
        }
        catch (Exception ex) { SettingsStore.Log("settings.save", ex); SaveStatus.Text = "Couldn't save. Check folder permissions."; }
    }
    private void OnPlayer(object sender, RoutedEventArgs e) { host.Overlay.Expand(); }
}

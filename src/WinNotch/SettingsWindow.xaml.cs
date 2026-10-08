using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using WinNotch.Core;
using WinNotch.Native;
using System.Linq;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WinNotch;

public partial class SettingsWindow : Window
{
    private readonly App host;
    private readonly DispatcherTimer statusTimer;
    private string downloadsFolder = "";
    public SettingsWindow(App host)
    {
        this.host = host;
        InitializeComponent();
        SourceInitialized += (_, _) => { var dark = 1; Win32.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        var s = host.Settings;
        StartupCheck.IsChecked = SettingsStore.StartupEnabled();
        TopmostCheck.IsChecked = s.AlwaysOnTop; FullscreenSelect.SelectedIndex = (int)s.EffectiveFullscreen;
        AnimationCheck.IsChecked = s.Animations; MediaCheck.IsChecked = s.Media;
        BatteryCheck.IsChecked = s.Battery; VolumeCheck.IsChecked = s.Volume;
        TimerCheck.IsChecked = s.Timer;
        CalendarCheck.IsChecked = s.Calendar;
        BluetoothCheck.IsChecked = s.Bluetooth;
        DownloadsCheck.IsChecked = s.Downloads; downloadsFolder = s.DownloadsFolder; UpdateDownloadsFolder();
        NotificationsCheck.IsChecked = s.Notifications; BrightnessCheck.IsChecked = s.Brightness;
        ClockCheck.IsChecked = s.Clock; TrueBlackCheck.IsChecked = s.TrueBlack;
        ThemeSelect.SelectedIndex = (int)s.Theme;
        AnimationSpeedSelect.SelectedIndex = s.AnimationSpeed < 1 ? 0 : s.AnimationSpeed > 1 ? 2 : 1;
        ShapeSelect.SelectedIndex = s.Floating ? 1 : 0;
        SizeSelect.SelectedIndex = s.Scale < 1 ? 0 : s.Scale > 1 ? 2 : 1;
        VisibilitySelect.SelectedIndex = (int)s.Visibility;
        RefreshDisplays(s.Display == DisplayMode.FollowActive ? "@follow" : s.Display == DisplayMode.Selected ? s.SelectedDisplay : "@primary");
        ReloadExclusions();
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
        NotificationStatus.Text = host.Notifications.Status;
        DownloadsSettingsStatus.Text = host.Downloads.Status;
        BluetoothSettingsStatus.Text = host.Bluetooth.Status;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var display = (DisplaySelect.SelectedItem as ComboBoxItem)?.Tag as string ?? "@primary";
        var names = ExclusionsText.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (names.Any(name => DesktopPolicy.NormalizeApps(new[] { name }).Length == 0))
        { SaveStatus.Text = "Use executable names ending in .exe. WinNotch and shared Windows app hosts cannot be excluded."; return; }
        var settings = host.Settings with
        {
            AlwaysOnTop = TopmostCheck.IsChecked == true,
            Fullscreen = (FullscreenMode)Math.Max(0, FullscreenSelect.SelectedIndex),
            HideInFullscreen = FullscreenSelect.SelectedIndex != (int)FullscreenMode.Show,
            Animations = AnimationCheck.IsChecked == true, Floating = ShapeSelect.SelectedIndex == 1,
            Scale = SizeSelect.SelectedIndex switch { 0 => 0.85, 2 => 1.15, _ => 1 },
            Visibility = (VisibilityMode)Math.Max(0, VisibilitySelect.SelectedIndex),
            Media = MediaCheck.IsChecked == true, Battery = BatteryCheck.IsChecked == true, Volume = VolumeCheck.IsChecked == true, Timer = TimerCheck.IsChecked == true,
            Notifications = NotificationsCheck.IsChecked == true, Brightness = BrightnessCheck.IsChecked == true,
            Clock = ClockCheck.IsChecked == true, TrueBlack = TrueBlackCheck.IsChecked == true,
            Calendar = CalendarCheck.IsChecked == true,
            Bluetooth = BluetoothCheck.IsChecked == true,
            Downloads = DownloadsCheck.IsChecked == true, DownloadsFolder = downloadsFolder,
            Theme = (AccentTheme)Math.Max(0, ThemeSelect.SelectedIndex),
            AnimationSpeed = AnimationSpeedSelect.SelectedIndex switch { 0 => 0.7, 2 => 1.4, _ => 1 },
            Display = display == "@follow" ? DisplayMode.FollowActive : display == "@primary" ? DisplayMode.Primary : DisplayMode.Selected,
            SelectedDisplay = display.StartsWith('@') ? host.Settings.SelectedDisplay : display,
            ExcludedApps = DesktopPolicy.NormalizeApps(names)
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
    private void OnPlayer(object sender, RoutedEventArgs e) { host.Overlay.Expand(false); }
    private void OnAgenda(object sender, RoutedEventArgs e) => host.OpenAgenda();
    private void OnBluetoothSettings(object sender, RoutedEventArgs e) => host.OpenBluetoothSettings();
    private void UpdateDownloadsFolder() => DownloadsFolderText.Text = downloadsFolder.Length == 0 ? $"Windows Downloads · {Win32.DownloadsFolder()}" : downloadsFolder;
    private void OnChooseDownloads(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose the downloads folder", Multiselect = false };
        if (picker.ShowDialog(this) == true) { downloadsFolder = picker.FolderName; UpdateDownloadsFolder(); }
    }
    private void OnDefaultDownloads(object sender, RoutedEventArgs e) { downloadsFolder = ""; UpdateDownloadsFolder(); }
    private async void OnNotificationAccess(object sender, RoutedEventArgs e)
    {
        NotificationAccessButton.IsEnabled = false;
        try { await host.Notifications.RequestAccessAsync(); UpdateStatus(); }
        finally { NotificationAccessButton.IsEnabled = true; }
    }
    private void OnNotificationPrivacy(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true }); }
        catch (Exception ex) { SettingsStore.Log("settings.notificationPrivacy", ex); SaveStatus.Text = "Open Windows Settings > Privacy & security > Notifications."; }
    }
    internal void ReloadExclusions() => ExclusionsText.Text = string.Join(Environment.NewLine, host.Settings.ExcludedApps);
    private void RefreshDisplays(string selected)
    {
        DisplaySelect.Items.Clear();
        DisplaySelect.Items.Add(new ComboBoxItem { Content = "Primary monitor", Tag = "@primary" });
        DisplaySelect.Items.Add(new ComboBoxItem { Content = "Follow active window", Tag = "@follow" });
        foreach (var display in Win32.Displays())
            DisplaySelect.Items.Add(new ComboBoxItem { Content = $"{display.Info.Device.Replace(@"\\.\", "")} · {display.Info.Monitor.Width} × {display.Info.Monitor.Height}{((display.Info.Flags & 1) != 0 ? " · Primary" : "")}", Tag = display.Info.Device });
        var match = DisplaySelect.Items.Cast<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            match = new ComboBoxItem { Content = $"{selected} · Disconnected (using primary)", Tag = selected };
            DisplaySelect.Items.Add(match);
        }
        DisplaySelect.SelectedItem = match;
    }
    private void OnRefreshDisplays(object sender, RoutedEventArgs e) => RefreshDisplays((DisplaySelect.SelectedItem as ComboBoxItem)?.Tag as string ?? "@primary");
    private void OnAddApplication(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = "Choose an application to hide WinNotch in" };
        if (picker.ShowDialog(this) == true)
            ExclusionsText.Text = string.Join(Environment.NewLine, DesktopPolicy.NormalizeApps(ExclusionsText.Text.Split('\n').Append(picker.FileName)));
    }
}

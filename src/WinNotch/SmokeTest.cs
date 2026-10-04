using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinNotch.Core;
using WinNotch.Native;

namespace WinNotch;

internal static class SmokeTest
{
    internal static async Task RunAsync(App app, string output)
    {
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, object>();
        app.Notifications.Dispose(); // Synthetic fixtures only; never request access or read personal notifications in tests.
        var testSettings = app.Settings with { Visibility = VisibilityMode.Always, Fullscreen = FullscreenMode.Show, HideInFullscreen = false, Display = DisplayMode.Primary, ExcludedApps = Array.Empty<string>(), Notifications = false, Clock = false };
        // Isolate arbitration from real media/battery events arriving during this timed assertion.
        app.ApplySettings(testSettings with { Media = false, Battery = false, Volume = false });
        app.State.Remove("media"); app.State.HasMedia = false; app.State.Refresh();
        await Task.Delay(400);
        var hwnd = new WindowInteropHelper(app.Overlay).Handle;
        var style = Win32.GetWindowLongPtr(hwnd, Win32.GwlExStyle).ToInt64();
        checks["idleNoActivate"] = (style & Win32.NoActivate) != 0;
        checks["toolWindow"] = (style & Win32.ToolWindow) != 0;
        checks["noTaskbar"] = !app.Overlay.ShowInTaskbar;
        checks["nativeTrayRegistered"] = app.IsTrayRegistered;
        var monitors = Win32.Displays();
        checks["controls.monitorEnumeration"] = monitors.Count > 0 && monitors.All(display => !string.IsNullOrWhiteSpace(display.Info.Device) && display.Info.Monitor.Width > 0);
        checks["monitorCount"] = monitors.Count;
        var externalName = Win32.AppName(Win32.GetForegroundWindow());
        if (DesktopPolicy.NormalizeApps(new[] { externalName }).Length > 0)
        {
            app.ApplySettings(app.Settings with { ExcludedApps = new[] { externalName } });
            checks["controls.excludedForegroundHides"] = app.State.Suppressed && !app.Overlay.IsVisible;
            app.ApplySettings(app.Settings with { ExcludedApps = Array.Empty<string>() });
            checks["controls.removingExclusionRestores"] = !app.State.Suppressed && app.Overlay.IsVisible;
        }
        else checks["exclusionNativeStatus"] = "No external foreground app available; policy checks still run.";
        var preferences = testSettings with { Display = DisplayMode.Selected, SelectedDisplay = monitors[0].Info.Device, ExcludedApps = new[] { "test.exe" } };
        var roundTrip = JsonSerializer.Deserialize<NotchSettings>(JsonSerializer.Serialize(preferences));
        checks["controls.desktopPreferencesRoundTrip"] = roundTrip?.Display == DisplayMode.Selected && roundTrip.SelectedDisplay == preferences.SelectedDisplay && roundTrip.ExcludedApps.SequenceEqual(preferences.ExcludedApps);
        app.ApplySettings(app.Settings with { Display = DisplayMode.Selected, SelectedDisplay = monitors[0].Info.Device });
        checks["controls.selectedMonitor"] = app.TargetMonitor.Handle == monitors[0].Handle;
        app.ApplySettings(app.Settings with { SelectedDisplay = "missing-display" });
        checks["controls.disconnectedMonitorFallback"] = app.TargetMonitor.Handle == Win32.PrimaryMonitor().Handle;
        app.ApplySettings(app.Settings with { Display = DisplayMode.FollowActive });
        checks["controls.followMonitorAvailable"] = monitors.Any(display => display.Handle == app.TargetMonitor.Handle);
        app.ApplySettings(testSettings with { Media = false, Battery = false, Volume = false });
        await CheckControlsAsync(app, checks, output);
        await CheckNewModulesAsync(app, checks, output);
        var foreground = Win32.GetForegroundWindow();
        app.State.Publish("battery", "\uE945", "Charging", "72%", 70, TimeSpan.FromMilliseconds(700));
        await Task.Delay(300);
        checks["peekDoesNotStealFocus"] = Win32.GetForegroundWindow() == foreground;
        Capture(app.Overlay, Path.Combine(output, "peek.png"));
        await Task.Delay(650);
        app.State.Refresh();
        checks["eventRestoresPersistentState"] = app.State.Current.State == (app.State.HasMedia ? NotchState.Compact : NotchState.Idle);
        app.ApplySettings(testSettings);
        var beforeExpand = Win32.GetForegroundWindow();
        app.Overlay.Expand();
        await Task.Delay(400);
        checks["expandedInteractive"] = (Win32.GetWindowLongPtr(hwnd, Win32.GwlExStyle).ToInt64() & Win32.NoActivate) == 0;
        Capture(app.Overlay, Path.Combine(output, "expanded.png"));
        app.Overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(app.Overlay), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        checks["escapeCollapses"] = app.State.Current.State != NotchState.Expanded;
        checks["collapseRestoresFocus"] = Win32.GetForegroundWindow() == beforeExpand;
        var settings = new SettingsWindow(app);
        settings.Show();
        await Task.Delay(250);
        Capture(settings, Path.Combine(output, "settings.png"));
        ((ScrollViewer)settings.FindName("PreferencesScroll")).ScrollToEnd();
        await Task.Delay(150);
        Capture(settings, Path.Combine(output, "settings-modules.png"));
        app.Overlay.Expand();
        await Task.Delay(300);
        settings.Activate();
        await Task.Delay(200);
        checks["outsideClickCollapses"] = app.State.Current.State != NotchState.Expanded;
        settings.Close();
        app.State.Suppressed = true; app.State.Refresh();
        checks["suppressedWindowHidden"] = !app.Overlay.IsVisible;
        var monitor = Win32.PrimaryMonitor().Info.Monitor;
        var anchor = new IntPtr(unchecked(((monitor.Bottom - 20) << 16) | ((monitor.Right - 80) & 0xFFFF)));
        Win32.SendMessage(hwnd, 0x8001, anchor, new IntPtr((1 << 16) | 0x401));
        await Task.Delay(200);
        checks["trayMenuWorksWhenHidden"] = app.Overlay.TrayMenu.IsOpen && PresentationSource.FromVisual(app.Overlay.TrayMenu) != null;
        app.Overlay.TrayMenu.IsOpen = false;
        app.State.Suppressed = false; app.State.Refresh();
        await Task.Delay(400);
        Capture(app.Overlay, Path.Combine(output, "idle.png"));
        var primary = Win32.PrimaryMonitor();
        Win32.GetWindowRect(hwnd, out var rect);
        checks["topCentered"] = rect.Top == primary.Info.Monitor.Top && Math.Abs((rect.Left + rect.Right) - (primary.Info.Monitor.Left + primary.Info.Monitor.Right)) <= 2;
        checks["dpi"] = Win32.GetDpiForWindow(hwnd);
        checks["mediaStatus"] = app.Media.Status;
        checks["mediaError"] = app.Media.DiagnosticError ?? "none";
        checks["mediaSessionPresent"] = app.Media.Current != null;
        checks["audioAvailable"] = app.Audio.Current.Available;
        checks["batteryPresent"] = app.Battery.Current.Present;
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(2500);
        process.Refresh();
        checks["samplePostInteractionCpuPercent"] = Math.Round((process.TotalProcessorTime - cpu).TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, 2);
        checks["workingSetMiB"] = Math.Round(process.WorkingSet64 / 1048576d, 1);
        var required = new[] { "idleNoActivate", "toolWindow", "noTaskbar", "nativeTrayRegistered", "peekDoesNotStealFocus", "eventRestoresPersistentState", "expandedInteractive", "escapeCollapses", "collapseRestoresFocus", "outsideClickCollapses", "suppressedWindowHidden", "trayMenuWorksWhenHidden", "topCentered" };
        var passed = required.All(key => checks[key] is true);
        passed &= checks.Where(pair => pair.Key.StartsWith("controls.")).All(pair => pair.Value is true);
        checks["nativeChecksPassed"] = passed;
        await File.WriteAllTextAsync(Path.Combine(output, "smoke-results.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("Native smoke checks failed; inspect smoke-results.json.");
        if (Environment.GetCommandLineArgs().Contains("--restart-test"))
            app.Overlay.TrayMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Restart WinNotch").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static async Task CheckControlsAsync(App app, Dictionary<string, object> checks, string output)
    {
        var saved = app.Settings;
        app.ApplySettings(saved with { Timer = true, Volume = true });
        var overlay = app.Overlay;
        T Find<T>(string name) where T : FrameworkElement => (T)overlay.FindName(name);
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        overlay.Expand(); Click("TimerTab");
        await Task.Delay(300);
        Capture(overlay, Path.Combine(output, "timer-setup.png"));
        var minutes = Find<TextBox>("TimerMinutes");
        minutes.Text = "0"; Click("StartTimerButton");
        checks["controls.timerRejectsInvalidInput"] = !app.Timer.IsActive;
        minutes.Text = "10"; Click("StartTimerButton");
        checks["controls.customTimerStarts"] = app.Timer.Status == TimerStatus.Running && app.Timer.Remaining == TimeSpan.FromMinutes(10);
        Click("PauseTimerButton");
        var remaining = app.Timer.Remaining;
        await Task.Delay(1100);
        checks["controls.pauseHoldsCountdown"] = app.Timer.Status == TimerStatus.Paused && app.Timer.Remaining == remaining;
        Capture(overlay, Path.Combine(output, "timer-paused.png"));
        Click("PauseTimerButton");
        checks["controls.resumeRuns"] = app.Timer.Status == TimerStatus.Running;
        app.State.Collapse();
        await Task.Delay(300);
        checks["controls.timerStaysCompact"] = app.State.Current.State == NotchState.Compact && Find<Grid>("CompactTimer").IsVisible;
        Capture(overlay, Path.Combine(output, "timer-compact.png"));
        app.State.Publish("volume", "\uE767", "Volume", "50%", 50, TimeSpan.FromMilliseconds(300));
        await Task.Delay(650);
        checks["controls.eventReturnsToTimer"] = app.State.Current.State == NotchState.Compact && app.Timer.IsActive;
        overlay.Expand(); Click("CancelTimerButton"); Click("FocusPreset");
        checks["controls.focusPresetStarts"] = app.Timer.Mode == TimerMode.Focus && app.Timer.Remaining == TimeSpan.FromMinutes(25);
        Click("MediaTab");
        checks["controls.mediaAccessibleDuringTimer"] = Find<Grid>("MediaPanel").IsVisible && app.Timer.IsActive;
        Click("TimerTab"); Click("CancelTimerButton");
        // Use a short deadline through the same service; the public UI keeps whole-minute inputs.
        app.Timer.Start(TimeSpan.FromSeconds(1), TimerMode.Focus);
        app.State.Suppressed = true; app.State.Refresh();
        await Task.Delay(1400);
        checks["controls.hiddenCompletionPersists"] = app.Timer.Status == TimerStatus.Completed && !overlay.IsVisible;
        app.State.Suppressed = false; app.State.Refresh();
        checks["controls.completedTimerRestores"] = app.State.Current.State == NotchState.Compact && Find<TextBlock>("CompactCountdown").Text == "Done";
        overlay.Expand();
        await Task.Delay(300);
        checks["controls.volumeVisibleAfterRestore"] = Find<Grid>("VolumeControls").IsVisible && Find<Slider>("VolumeSlider").ActualWidth > 0;
        Capture(overlay, Path.Combine(output, "timer-complete.png"));
        Click("NextTimerButton");
        checks["controls.nextPomodoroStartsBreak"] = app.Timer.Mode == TimerMode.ShortBreak && app.Timer.Remaining == TimeSpan.FromMinutes(5);
        app.ApplySettings(app.Settings with { Timer = false });
        checks["controls.disablingTimerCancels"] = !app.Timer.IsActive && !app.State.HasTimer && !Find<Button>("TimerTab").IsVisible;
        overlay.UpdateAudio(new(false, 0, false));
        checks["controls.noOutputDisablesAudio"] = !Find<Slider>("VolumeSlider").IsEnabled && !Find<Button>("MuteButton").IsEnabled;
        overlay.UpdateAudio(app.Audio.Current);
        checks["controls.audioReflectsSystem"] = Find<Slider>("VolumeSlider").Value == app.Audio.Current.Percent && Find<Slider>("VolumeSlider").IsEnabled == app.Audio.Current.Available;
        app.ApplySettings(saved); app.State.Collapse();
    }
    private static async Task CheckNewModulesAsync(App app, Dictionary<string, object> checks, string output)
    {
        var saved = app.Settings;
        var overlay = app.Overlay;
        T Find<T>(string name) where T : FrameworkElement => (T)overlay.FindName(name);
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var legacy = JsonSerializer.Deserialize<NotchSettings>("{\"HideInFullscreen\":false}");
        checks["controls.legacyFullscreenMigration"] = legacy?.EffectiveFullscreen == FullscreenMode.Show && !legacy.Notifications && !legacy.Clock;
        var configured = saved with { Clock = true, Theme = AccentTheme.Ice, TrueBlack = true, AnimationSpeed = 1.4, Notifications = true, Brightness = true, Volume = true };
        var roundTrip = JsonSerializer.Deserialize<NotchSettings>(JsonSerializer.Serialize(configured));
        checks["controls.newPreferencesRoundTrip"] = roundTrip?.Theme == AccentTheme.Ice && roundTrip.Clock && roundTrip.TrueBlack && roundTrip.Notifications && roundTrip.AnimationSpeed == 1.4;
        app.ApplySettings(configured); app.State.Collapse();
        await Task.Delay(350);
        checks["controls.idleClockVisible"] = Find<TextBlock>("IdleClock").IsVisible && Find<TextBlock>("IdleClock").Text == DateTime.Now.ToString("t");
        checks["controls.themeApplied"] = ((SolidColorBrush)app.Resources["AccentBrush"]).Color == (SystemParameters.HighContrast ? SystemColors.HighlightColor : Color.FromRgb(170, 217, 255));
        checks["controls.blackLevelApplied"] = ((SolidColorBrush)Find<Border>("Notch").Background).Color == (SystemParameters.HighContrast ? SystemColors.WindowColor : Colors.Black);
        Capture(overlay, Path.Combine(output, "clock.png"));
        app.OnNotification(new("WinNotch demo", "A little reminder", "Your next focus session is ready. This is a synthetic preview; no personal notifications were read."));
        await Task.Delay(300);
        checks["controls.notificationPeek"] = app.State.Current.Event?.Key == "notification" && app.State.Current.State == NotchState.Peek;
        overlay.Expand(); Click("NotificationTab");
        await Task.Delay(300);
        checks["controls.notificationDetail"] = Find<Grid>("NotificationPanel").IsVisible && Find<TextBlock>("NotificationTitle").Text == "A little reminder";
        Capture(overlay, Path.Combine(output, "notification.png"));
        Click("DismissNotificationButton");
        checks["controls.dismissClearsPreview"] = app.LatestNotification == null && app.State.Current.Event?.Key != "notification";
        app.OnNotification(new("WinNotch demo", "Private preview", "Test"));
        app.TogglePause();
        checks["controls.pauseClearsNotification"] = app.LatestNotification == null && !overlay.IsVisible;
        app.TogglePause();
        app.State.Fullscreen = true; app.State.FullscreenBehavior = FullscreenMode.CriticalOnly; app.State.Refresh();
        app.OnNotification(new("WinNotch demo", "Should be dropped", "Test"));
        checks["controls.fullscreenDropsNotification"] = app.LatestNotification == null && app.State.Current.State == NotchState.Hidden;
        app.State.Publish("battery", "\uE7BA", "Battery running low", "10%", 100, TimeSpan.FromMilliseconds(500));
        checks["controls.fullscreenCriticalPeek"] = app.State.Current.State == NotchState.Peek && overlay.IsVisible;
        app.State.Remove("battery"); app.ApplySettings(configured);
        overlay.Expand(); Click("DisplayTab");
        await Task.Delay(500);
        overlay.UpdateBrightness(new(false, 0));
        checks["controls.unsupportedBrightnessDisabled"] = !Find<Slider>("BrightnessSlider").IsEnabled;
        overlay.UpdateBrightness(app.Brightness.Current);
        checks["controls.brightnessReflectsDevice"] = Find<Slider>("BrightnessSlider").Value == app.Brightness.Current.Percent && Find<Slider>("BrightnessSlider").IsEnabled == app.Brightness.Current.Available;
        Capture(overlay, Path.Combine(output, "brightness.png"));
        if (Environment.GetCommandLineArgs().Contains("--brightness-test") && app.Brightness.Current.Available)
        {
            // A small real hardware change, independently read back, always restored.
            static int ReadBrightness()
            {
                using var search = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
                using var values = search.Get();
                foreach (System.Management.ManagementObject value in values) { using (value) return Convert.ToInt32(value["CurrentBrightness"]); }
                return -1;
            }
            var original = await Task.Run(ReadBrightness);
            try
            {
                Find<Slider>("BrightnessSlider").Value = original >= 10 ? original - 5 : original + 5;
                await Task.Delay(1200);
                var actual = await Task.Run(ReadBrightness);
                checks["controls.brightnessHardwareWrite"] = actual != original && Math.Abs(actual - original) <= 10 && app.Brightness.Current.Percent == actual;
            }
            finally
            {
                app.Brightness.SetBrightness(original);
                await Task.Delay(1200);
                checks["controls.brightnessHardwareRestored"] = await Task.Run(ReadBrightness) == original;
            }
        }
        app.ApplySettings(configured with { Brightness = false, Notifications = false, Clock = false });
        checks["controls.modulesDisable"] = !Find<Button>("DisplayTab").IsVisible && !Find<Button>("NotificationTab").IsVisible && app.LatestNotification == null;
        checks["controls.restartMenuAvailable"] = overlay.TrayMenu.Items.OfType<MenuItem>().Any(item => item.Header as string == "Restart WinNotch");
        app.ApplySettings(saved); app.State.Collapse();
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}

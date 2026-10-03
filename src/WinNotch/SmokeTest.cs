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
        var testSettings = app.Settings with { Visibility = VisibilityMode.Always, HideInFullscreen = false };
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
        checks["nativeChecksPassed"] = passed;
        await File.WriteAllTextAsync(Path.Combine(output, "smoke-results.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("Native smoke checks failed; inspect smoke-results.json.");
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

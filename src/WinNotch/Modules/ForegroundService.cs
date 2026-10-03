using System;
using System.Windows.Threading;
using Microsoft.Win32;
using WinNotch.Native;

namespace WinNotch.Modules;

public sealed class ForegroundService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Win32.WinEventDelegate callback;
    private readonly IntPtr foregroundHook, locationHook;
    private readonly DispatcherTimer safetyTimer, debounce, restoreTimer;
    private bool hidden, maximized, disposed;
    public event Action<bool, bool>? Changed;
    public event Action? DisplayChanged;

    public ForegroundService(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(70), DispatcherPriority.Background, (_, _) => { debounce!.Stop(); Check(); }, dispatcher);
        debounce.Stop();
        restoreTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => { restoreTimer!.Stop(); Check(true); }, dispatcher);
        restoreTimer.Stop();
        callback = (_, _, hwnd, objectId, _, _, _) =>
        {
            if (disposed || objectId != 0 || hwnd != Win32.GetForegroundWindow()) return;
            dispatcher.BeginInvoke(() => { if (!disposed && !debounce.IsEnabled) debounce.Start(); });
        };
        foregroundHook = Win32.SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2);
        locationHook = Win32.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, callback, 0, 0, 2);
        // Presentation mode can change without a foreground event; a slow watchdog covers it.
        safetyTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Check(), dispatcher);
        SystemEvents.DisplaySettingsChanged += OnDisplay;
    }

    private void OnDisplay(object? sender, EventArgs e) => dispatcher.BeginInvoke(() => { DisplayChanged?.Invoke(); Check(); });

    public void Check(bool allowRestore = false)
    {
        if (disposed) return;
        var hwnd = Win32.GetForegroundWindow();
        Win32.GetWindowThreadProcessId(hwnd, out var process);
        if (process == Environment.ProcessId) return;
        var primary = Win32.PrimaryMonitor();
        var sameMonitor = Win32.MonitorFromWindow(hwnd, 2) == primary.Handle;
        var valid = hwnd != IntPtr.Zero && Win32.IsWindowVisible(hwnd) && !Win32.IsDesktop(hwnd);
        Win32.SHQueryUserNotificationState(out var notificationState);
        var fullscreen = notificationState is 1 or 2 or 3 or 4 || (valid && sameMonitor && Win32.CoversMonitor(hwnd, primary.Info.Monitor));
        var isMaximized = valid && sameMonitor && Win32.IsZoomed(hwnd);
        if (!fullscreen && hidden && !allowRestore)
        {
            if (!restoreTimer.IsEnabled) restoreTimer.Start();
            return;
        }
        if (fullscreen) restoreTimer.Stop();
        if (fullscreen == hidden && maximized == isMaximized) return;
        hidden = fullscreen;
        maximized = isMaximized;
        Changed?.Invoke(hidden, maximized);
    }

    public void Dispose()
    {
        disposed = true;
        safetyTimer.Stop(); debounce.Stop(); restoreTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplay;
        if (foregroundHook != IntPtr.Zero) Win32.UnhookWinEvent(foregroundHook);
        if (locationHook != IntPtr.Zero) Win32.UnhookWinEvent(locationHook);
        GC.KeepAlive(callback);
    }
}

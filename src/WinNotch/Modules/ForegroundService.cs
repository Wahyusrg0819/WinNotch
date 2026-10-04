using System;
using System.Windows.Threading;
using Microsoft.Win32;
using WinNotch.Native;
using WinNotch.Core;

namespace WinNotch.Modules;

public sealed class ForegroundService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly IntPtr overlayWindow;
    private readonly Win32.WinEventDelegate callback;
    private readonly IntPtr foregroundHook, locationHook;
    private readonly DispatcherTimer safetyTimer, debounce, restoreTimer;
    private bool hidden, maximized, disposed;
    public NotchSettings Settings { get; set; } = new();
    public bool IsExcluded { get; private set; }
    public bool IsPresentation { get; private set; }
    public string LastAppName { get; private set; } = "";
    private IntPtr lastExternalWindow;
    internal (IntPtr Handle, Win32.MonitorInfo Info) TargetMonitor { get; private set; } = Win32.PrimaryMonitor();
    public event Action<bool, bool>? Changed;
    public event Action? DisplayChanged;

    public ForegroundService(Dispatcher dispatcher, IntPtr overlayWindow)
    {
        this.dispatcher = dispatcher;
        this.overlayWindow = overlayWindow;
        debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(70), DispatcherPriority.Background, (_, _) => { debounce!.Stop(); Check(); }, dispatcher);
        debounce.Stop();
        restoreTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => { restoreTimer!.Stop(); Check(true); }, dispatcher);
        restoreTimer.Stop();
        callback = (_, _, hwnd, objectId, _, _, _) =>
        {
            if (disposed || hwnd == overlayWindow || objectId != 0 || hwnd != Win32.GetForegroundWindow()) return;
            dispatcher.BeginInvoke(() => { if (!disposed && !debounce.IsEnabled) debounce.Start(); });
        };
        foregroundHook = Win32.SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
        locationHook = Win32.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, callback, 0, 0, 0);
        // Presentation mode can change without a foreground event; a slow watchdog covers it.
        safetyTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Check(), dispatcher);
        SystemEvents.DisplaySettingsChanged += OnDisplay;
    }

    private void OnDisplay(object? sender, EventArgs e) => dispatcher.BeginInvoke(() => { Check(true, true); DisplayChanged?.Invoke(); });

    public void Check(bool allowRestore = false, bool force = false)
    {
        if (disposed) return;
        var hwnd = Win32.GetForegroundWindow();
        // Preserve the underlying app context while interacting with the overlay itself.
        // Other WinNotch windows (settings) must still clear a previous fullscreen state.
        if (hwnd == overlayWindow) { if (!force) return; hwnd = lastExternalWindow; }
        var valid = hwnd != IntPtr.Zero && Win32.IsWindowVisible(hwnd) && !Win32.IsIconic(hwnd) && !Win32.IsDesktop(hwnd);
        var app = valid ? Win32.AppName(hwnd) : "";
        if (app.Length > 0) { lastExternalWindow = hwnd; LastAppName = app; }
        var target = Win32.SelectMonitor(Settings, lastExternalWindow);
        var moved = target.Handle != TargetMonitor.Handle || target.Info.Monitor.Bounds != TargetMonitor.Info.Monitor.Bounds;
        TargetMonitor = target;
        if (moved) DisplayChanged?.Invoke();
        var sameMonitor = Win32.MonitorFromWindow(hwnd, 2) == target.Handle;
        var excluded = DesktopPolicy.IsExcluded(app, Settings.ExcludedApps);
        var notificationState = Win32.SHQueryUserNotificationState(out var queriedState) == 0 ? queriedState : 5;
        var presentation = notificationState is 1 or 4;
        var fullscreen = DisplayPolicy.ShouldHide(notificationState, valid, sameMonitor, valid && Win32.CoversMonitor(hwnd, target.Info.Monitor));
        var isMaximized = valid && sameMonitor && Win32.IsZoomed(hwnd);
        if (!fullscreen && hidden && !allowRestore && excluded == IsExcluded)
        {
            if (!restoreTimer.IsEnabled) restoreTimer.Start();
            return;
        }
        if (fullscreen) restoreTimer.Stop();
        if (!force && fullscreen == hidden && maximized == isMaximized && excluded == IsExcluded && presentation == IsPresentation) return;
        hidden = fullscreen;
        maximized = isMaximized;
        IsExcluded = excluded;
        IsPresentation = presentation;
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

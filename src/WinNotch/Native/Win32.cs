using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using WinNotch.Core;

namespace WinNotch.Native;

internal static class Win32
{
    internal const int GwlExStyle = -20, ToolWindow = 0x80, NoActivate = 0x08000000;
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);
    internal static string DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        var result = SHGetKnownFolderPath(in id, 0x4000, IntPtr.Zero, out var path);
        try { return result >= 0 ? Marshal.PtrToStringUni(path) ?? "" : ""; }
        finally { if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path); }
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; public PixelBounds Bounds => new(Left, Top, Width, Height); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
    {
        public int Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    internal delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect bounds, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    internal delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("shell32.dll")] internal static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal static (IntPtr Handle, MonitorInfo Info) PrimaryMonitor()
    {
        var handle = MonitorFromPoint(new Point(0, 0), 1);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(handle, ref info);
        return (handle, info);
    }

    internal static List<(IntPtr Handle, MonitorInfo Info)> Displays()
    {
        var result = new List<(IntPtr, MonitorInfo)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr dc, ref Rect bounds, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(handle, ref info)) result.Add((handle, info));
            return true;
        }, IntPtr.Zero);
        if (result.Count == 0) result.Add(PrimaryMonitor());
        return result;
    }

    internal static (IntPtr Handle, MonitorInfo Info) SelectMonitor(NotchSettings settings, IntPtr activeWindow)
    {
        var displays = Displays();
        var primary = displays.FirstOrDefault(item => (item.Info.Flags & 1) != 0);
        if (primary.Handle == IntPtr.Zero) primary = displays[0];
        var active = displays.FirstOrDefault(item => item.Handle == MonitorFromWindow(activeWindow, 0));
        var name = DesktopPolicy.SelectDisplay(settings.Display, settings.SelectedDisplay, active.Info.Device,
            displays.Select(item => item.Info.Device).ToArray(), primary.Info.Device);
        return displays.First(item => item.Info.Device == name);
    }

    internal static string AppName(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return "";
        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) return "";
        try
        {
            // Most executable paths fit here; avoid a 64 KiB allocation on every foreground check.
            var path = new StringBuilder(512); uint length = (uint)path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length))
            {
                if (Marshal.GetLastWin32Error() != 122) return ""; // ERROR_INSUFFICIENT_BUFFER
                path.EnsureCapacity(32768); length = (uint)path.Capacity;
                if (!QueryFullProcessImageName(process, 0, path, ref length)) return "";
            }
            return Path.GetFileName(path.ToString()).ToLowerInvariant();
        }
        finally { CloseHandle(process); }
    }

    internal static bool IsDesktop(IntPtr window)
    {
        var name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    internal static bool CoversMonitor(IntPtr window, Rect monitor)
    {
        if (!GetClientRect(window, out var client)) return false;
        var origin = new Point(0, 0);
        if (!ClientToScreen(window, ref origin)) return false;
        return new PixelBounds(origin.X, origin.Y, client.Width, client.Height).Covers(monitor.Bounds);
    }
}

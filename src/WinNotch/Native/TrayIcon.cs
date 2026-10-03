using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace WinNotch.Native;

// The overlay already has an HWND/message loop; a second UI framework isn't needed for the tray.
internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8001;
    private readonly HwndSource source;
    private readonly ContextMenu menu;
    private readonly Action openSettings;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private NotifyIconData data;
    private bool disposed;
    internal bool IsRegistered { get; private set; }

    internal TrayIcon(Window owner, ContextMenu menu, Action openSettings)
    {
        this.menu = menu;
        this.openSettings = openSettings;
        source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle)!;
        data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = source.Handle, Id = 1,
            Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = LoadIcon(),
            Tip = "WinNotch · Your quiet corner", Info = "", InfoTitle = "", Version = 4
        };
        source.AddHook(WindowProc);
        menu.Opened += OnMenuOpened;
        Add();
    }

    private void Add()
    {
        IsRegistered = Shell_NotifyIcon(0, ref data);
        if (IsRegistered) Shell_NotifyIcon(4, ref data);
    }
    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (disposed) return IntPtr.Zero;
        if (message == taskbarCreated) Add();
        if (message != CallbackMessage) return IntPtr.Zero;
        var action = (int)(lParam.ToInt64() & 0xFFFF);
        if (action is 0x7B or 0x401) // context menu or keyboard selection
        {
            menu.PlacementTarget = null;
            menu.Placement = PlacementMode.AbsolutePoint;
            var point = new Win32.Point(unchecked((short)(wParam.ToInt64() & 0xFFFF)), unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF)));
            var transform = source.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var dip = transform.Transform(new Point(point.X, point.Y));
            menu.HorizontalOffset = dip.X; menu.VerticalOffset = dip.Y;
            menu.IsOpen = true;
        }
        else if (action == 0x203) openSettings(); // double click
        handled = true;
        return IntPtr.Zero;
    }
    private void OnMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual(menu) is HwndSource popup) Win32.SetForegroundWindow(popup.Handle);
    }

    private static IntPtr LoadIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinNotch.ico")).Stream;
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); reader.ReadUInt16();
        var count = reader.ReadUInt16();
        for (var i = 0; i < count; i++)
        {
            stream.Position = 6 + 16 * i;
            var width = reader.ReadByte(); reader.ReadByte(); reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt16();
            var length = reader.ReadUInt32(); var offset = reader.ReadUInt32();
            if (width != 32 && i != count - 1) continue;
            stream.Position = offset;
            var image = reader.ReadBytes(checked((int)length));
            var handle = CreateIconFromResourceEx(image, (uint)image.Length, true, 0x30000, 32, 32, 0);
            if (handle != IntPtr.Zero) return handle;
        }
        throw new InvalidOperationException("Unable to load the tray icon.");
    }

    public void Dispose()
    {
        disposed = true; menu.IsOpen = false; menu.Opened -= OnMenuOpened;
        Shell_NotifyIcon(2, ref data); source.RemoveHook(WindowProc); DestroyIcon(data.Icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags; public int Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}

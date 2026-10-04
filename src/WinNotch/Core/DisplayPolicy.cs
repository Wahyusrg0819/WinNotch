using System;

namespace WinNotch.Core;

public readonly record struct PixelBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Covers(PixelBounds other) => Left <= other.Left && Top <= other.Top && Right >= other.Right && Bottom >= other.Bottom;
}

public static class DisplayPolicy
{
    public const double CanvasWidth = 500, CanvasHeight = 310;

    public static PixelBounds PlaceOverlay(PixelBounds monitor, uint dpi)
    {
        var density = (dpi == 0 ? 96 : dpi) / 96d;
        var width = Math.Min(monitor.Width, (int)Math.Round(CanvasWidth * density));
        var height = Math.Min(monitor.Height, (int)Math.Round(CanvasHeight * density));
        return new(monitor.Left + (monitor.Width - width) / 2, monitor.Top, width, height);
    }

    public static bool ShouldHide(int notificationState, bool validWindow, bool onNotchMonitor, bool coversMonitor) =>
        // QUNS_BUSY also occurs with an ordinary window on Windows 11; it isn't fullscreen proof.
        notificationState is 1 or 4 || (validWindow && onNotchMonitor && (notificationState == 3 || coversMonitor));
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinNotch.Core;
using WinNotch.Modules;
using WinNotch.Native;

namespace WinNotch;

public partial class OverlayWindow : Window
{
    private readonly App host;
    private IntPtr handle;
    private IntPtr returnFocus;
    private bool hover;
    private NotchState shownState = NotchState.Idle;
    private readonly DispatcherTimer timelineTimer;
    private readonly MenuItem pauseItem;
    internal ContextMenu TrayMenu => Notch.ContextMenu;

    public OverlayWindow(App host)
    {
        this.host = host;
        InitializeComponent();
        Width = DisplayPolicy.CanvasWidth; Height = DisplayPolicy.CanvasHeight;
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => host.OpenSettings();
        var player = new MenuItem { Header = "Open player" };
        player.Click += (_, _) => Expand();
        pauseItem = new MenuItem { Header = "Pause WinNotch" };
        pauseItem.Click += (_, _) => host.TogglePause();
        var exit = new MenuItem { Header = "Exit WinNotch" };
        exit.Click += (_, _) => host.Shutdown();
        menu.Items.Add(player); menu.Items.Add(settings); menu.Items.Add(pauseItem); menu.Items.Add(new Separator()); menu.Items.Add(exit);
        Notch.ContextMenu = menu;
        timelineTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => host.Media.RefreshTimeline(), Dispatcher);
        timelineTimer.Stop();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
            SetPassive(true);
            Reposition();
        };
        Closed += (_, _) => timelineTimer.Stop();
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x21 && host.State.Current.State != NotchState.Expanded) { handled = true; return new IntPtr(3); }
        if (message is 0x2E0 or 0x7E) Dispatcher.BeginInvoke(Reposition);
        return IntPtr.Zero;
    }

    private void SetPassive(bool passive)
    {
        if (handle == IntPtr.Zero) return;
        var style = Win32.GetWindowLongPtr(handle, Win32.GwlExStyle).ToInt64() | Win32.ToolWindow;
        style = passive ? style | Win32.NoActivate : style & ~Win32.NoActivate;
        Win32.SetWindowLongPtr(handle, Win32.GwlExStyle, new IntPtr(style));
    }

    public void Reposition()
    {
        if (handle == IntPtr.Zero) return;
        var primary = Win32.PrimaryMonitor();
        // Move into the target display first. WPF then receives WM_DPICHANGED and
        // the next placement uses this window's effective DPI, not a monitor query.
        if (Win32.MonitorFromWindow(handle, 2) != primary.Handle)
        {
            var initial = DisplayPolicy.PlaceOverlay(primary.Info.Monitor.Bounds, Win32.GetDpiForWindow(handle));
            Win32.SetWindowPos(handle, IntPtr.Zero, initial.Left, initial.Top, initial.Width, initial.Height, 0x14);
        }
        var dpi = Win32.GetDpiForWindow(handle);
        var bounds = DisplayPolicy.PlaceOverlay(primary.Info.Monitor.Bounds, dpi);
        Win32.SetWindowPos(handle, host.Settings.AlwaysOnTop ? new IntPtr(-1) : new IntPtr(-2),
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x10);
        var density = (dpi == 0 ? 96 : dpi) / 96d;
        var fit = Math.Min(host.Settings.Scale, Math.Min(bounds.Width / density / 390, (bounds.Height / density - Notch.Margin.Top) / 210));
        Notch.LayoutTransform = new ScaleTransform(Math.Max(0.1, fit), Math.Max(0.1, fit));
    }

    public void ApplySettings()
    {
        Topmost = host.Settings.AlwaysOnTop;
        Notch.LayoutTransform = new ScaleTransform(host.Settings.Scale, host.Settings.Scale);
        Notch.Margin = new Thickness(0, host.Settings.Floating ? 8 : 0, 0, 0);
        Notch.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(5, 5, 5));
        Notch.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.Transparent;
        Notch.BorderThickness = new Thickness(SystemParameters.HighContrast ? 1 : 0);
        Reposition();
        Render(host.State.Current);
    }

    public void Render(NotchPresentation presentation)
    {
        var previous = shownState;
        shownState = presentation.State;
        pauseItem.Header = host.State.Paused ? "Resume WinNotch" : "Pause WinNotch";
        if (shownState == NotchState.Hidden) { timelineTimer.Stop(); SetPassive(true); Hide(); return; }
        if (!IsVisible) Show();
        var expanded = shownState == NotchState.Expanded;
        SetPassive(!expanded);
        if (previous == NotchState.Expanded && !expanded && Win32.GetForegroundWindow() == handle && returnFocus != IntPtr.Zero)
            Win32.SetForegroundWindow(returnFocus);
        if (expanded && host.Media.Current?.Playing == true && host.Settings.Media) timelineTimer.Start(); else timelineTimer.Stop();
        IdleContent.Visibility = shownState == NotchState.Idle ? Visibility.Visible : Visibility.Collapsed;
        CompactContent.Visibility = shownState == NotchState.Compact ? Visibility.Visible : Visibility.Collapsed;
        PeekContent.Visibility = shownState == NotchState.Peek ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (presentation.Event is { } e)
        {
            PeekIcon.Text = e.Glyph; PeekTitle.Text = e.Title; PeekDetail.Text = e.Detail;
            PeekLevel.Visibility = e.Level.HasValue ? Visibility.Visible : Visibility.Collapsed;
            PeekLevel.Value = e.Level ?? 0;
        }
        var (width, height) = shownState switch
        {
            NotchState.Expanded => (390d, 210d),
            NotchState.Compact => (268d, 42d),
            NotchState.Peek => (280d, presentation.Event?.Level != null ? 46d : 40d),
            _ => hover ? (138d, 32d) : (126d, 30d)
        };
        Notch.CornerRadius = host.Settings.Floating ? new CornerRadius(expanded ? 23 : 18) : new CornerRadius(0, 0, expanded ? 23 : 16, expanded ? 23 : 16);
        AnimateSize(width, height, expanded ? 280 : 200);
        if (previous != shownState && CanAnimate)
        {
            var content = expanded ? ExpandedContent : shownState == NotchState.Peek ? PeekContent : shownState == NotchState.Compact ? CompactContent : IdleContent;
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
        }
    }

    private bool CanAnimate => host.Settings.Animations && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    private void AnimateSize(double width, double height, int milliseconds)
    {
        if (!CanAnimate)
        {
            Notch.BeginAnimation(WidthProperty, null); Notch.BeginAnimation(HeightProperty, null);
            Notch.Width = width; Notch.Height = height; return;
        }
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Notch.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });
        Notch.BeginAnimation(HeightProperty, new DoubleAnimation(height, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });
    }

    public void UpdateMedia(MediaSnapshot? media)
    {
        CompactArt.Source = ExpandedArt.Source = media?.Artwork;
        CompactTitle.Text = media?.Title ?? "No media playing";
        TrackTitle.Text = media?.Title ?? (host.Settings.Media ? "A little space for your music." : "Your quiet corner.");
        TrackArtist.Text = media == null ? (host.Settings.Media ? host.Media.Status : "Media is turned off in settings.") : string.IsNullOrWhiteSpace(media.Artist) ? "Windows media session" : media.Artist;
        TrackTitle.ToolTip = media?.Title;
        TrackArtist.ToolTip = media?.Artist;
        PlayButton.Content = CompactPlay.Content = media?.Playing == true ? "\uE769" : "\uE768";
        PlayButton.IsEnabled = CompactPlay.IsEnabled = media?.CanPlayPause == true;
        PreviousButton.IsEnabled = media?.CanPrevious == true;
        NextButton.IsEnabled = media?.CanNext == true;
        Elapsed.Text = FormatTime(media?.Position ?? TimeSpan.Zero);
        Duration.Text = FormatTime(media?.Duration ?? TimeSpan.Zero);
        Timeline.Value = media?.Duration.TotalSeconds > 0 ? media.Position.TotalSeconds / media.Duration.TotalSeconds : 0;
        if (shownState == NotchState.Expanded && media?.Playing == true) timelineTimer.Start(); else timelineTimer.Stop();
    }

    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    public void Expand()
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground != handle) returnFocus = foreground;
        if (host.State.Paused) host.TogglePause();
        host.State.Expand();
        if (host.State.Current.State != NotchState.Expanded) return;
        SetPassive(false); Activate();
        if (PlayButton.IsEnabled) PlayButton.Focus(); else ExpandedContent.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    private void OnNotchClick(object sender, MouseButtonEventArgs e) { if (shownState != NotchState.Expanded) Expand(); }
    private void OnEnter(object sender, MouseEventArgs e) { hover = true; if (shownState == NotchState.Idle) Render(host.State.Current); }
    private void OnLeave(object sender, MouseEventArgs e) { hover = false; if (shownState == NotchState.Idle) Render(host.State.Current); }
    private void OnDeactivated(object? sender, EventArgs e) { if (Notch.ContextMenu?.IsOpen != true) host.State.Collapse(); }
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { host.State.Collapse(); e.Handled = true; } }
    private async void OnPlay(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("play"); }
    private async void OnPrevious(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("previous"); }
    private async void OnNext(object sender, RoutedEventArgs e) { e.Handled = true; await host.Media.ControlAsync("next"); }
    private void OnSettings(object sender, RoutedEventArgs e) { e.Handled = true; host.OpenSettings(); }
}

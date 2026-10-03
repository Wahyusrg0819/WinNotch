using WinNotch.Core;

var time = DateTimeOffset.UtcNow;
var state = new NotchStateManager(() => time);
var count = 0;
void Check(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine($"PASS {description}"); count++; }
state.Refresh(); Check(state.Current.State == NotchState.Idle, "Starts idle");
state.HasMedia = true; state.Refresh(); Check(state.Current.State == NotchState.Compact, "Persistent media is compact");
state.Publish("media", "", "Track", "", 30, TimeSpan.FromSeconds(3));
state.Publish("battery", "", "Charging", "", 70, TimeSpan.FromSeconds(3));
state.Publish("volume", "", "Volume", "", 50, TimeSpan.FromSeconds(2));
Check(state.Current.Event?.Key == "battery", "Battery event wins over volume and media");
time += TimeSpan.FromSeconds(3.1); state.Refresh();
Check(state.Current.State == NotchState.Compact && state.Current.Event == null, "Expired events return directly to media");
state.Publish("volume", "", "20", "", 50, TimeSpan.FromSeconds(2));
state.Publish("volume", "", "21", "", 50, TimeSpan.FromSeconds(2));
Check(state.Current.Event?.Title == "21", "Repeated volume updates coalesce");
state.Expand(); Check(state.Current.State == NotchState.Expanded, "Explicit expand wins over transient events");
state.Suppressed = true; state.Refresh(); Check(state.Current.State == NotchState.Hidden, "Fullscreen hides even expanded UI");
state.Publish("battery", "", "Hidden", "", 100, TimeSpan.FromSeconds(10));
state.Suppressed = false; state.Refresh();
Check(state.Current.State == NotchState.Compact && !state.HasEvents, "Fullscreen restoration discards stale events and expansion");
state.HasMedia = false; state.MaximizedApp = true; state.Visibility = VisibilityMode.SmartHide; state.Refresh();
Check(state.Current.State == NotchState.Hidden, "Smart hide protects maximized title bars");
state.Publish("volume", "", "Volume", "", 50, TimeSpan.FromSeconds(2));
Check(state.Current.State == NotchState.Peek, "Active events remain visible in smart hide");
state.Remove("volume"); state.Visibility = VisibilityMode.Always; state.Refresh();
Check(state.Current.State == NotchState.Idle, "Always visible overrides smart hide");
state.Visibility = VisibilityMode.ActiveOnly; state.Refresh(); Check(state.Current.State == NotchState.Hidden, "Active-only hides idle");
state.Expand(); Check(state.Current.State == NotchState.Expanded, "Tray can open player in active-only mode");
state.Paused = true; state.Refresh(); state.Expand(); Check(state.Current.State == NotchState.Hidden, "Pause prevents re-expansion");
state.Paused = false; state.Visibility = VisibilityMode.Always; state.Refresh(); Check(state.Current.State == NotchState.Idle, "Resume clears interaction state");
foreach (var dpi in new uint[] { 96, 120, 144, 192 })
{
    var screen = new PixelBounds(-1920, -100, 1920, 1080);
    var placement = DisplayPolicy.PlaceOverlay(screen, dpi);
    Check(placement.Width == Math.Round(500 * dpi / 96d) && placement.Height == Math.Round(270 * dpi / 96d) && placement.Top == screen.Top &&
        Math.Abs(placement.Left * 2 + placement.Width - (screen.Left * 2 + screen.Width)) <= 1,
        $"Placement centers physical pixels at {dpi / 96d:P0}, including negative display origins");
}
Check(DisplayPolicy.PlaceOverlay(new(0, 0, 800, 450), 192) == new PixelBounds(0, 0, 800, 450), "Small displays clamp the overlay canvas");
Check(DisplayPolicy.PlaceOverlay(new(0, 0, 1920, 1080), 0).Width == 500, "Unknown DPI safely defaults to 96");
Check(DisplayPolicy.ShouldHide(5, true, true, true), "Borderless fullscreen hides on the notch display");
Check(DisplayPolicy.ShouldHide(3, true, true, false), "Exclusive fullscreen shell state hides on the notch display");
Check(!DisplayPolicy.ShouldHide(3, true, false, true), "Secondary-display fullscreen leaves primary notch available");
Check(!DisplayPolicy.ShouldHide(2, true, false, false), "Secondary-display busy state does not hide the primary notch");
Check(!DisplayPolicy.ShouldHide(2, true, true, false), "Busy state alone does not hide an ordinary primary-display window");
Check(DisplayPolicy.ShouldHide(4, false, false, false), "Presentation mode hides globally");
Check(DisplayPolicy.ShouldHide(1, false, false, false), "Locked or inactive desktop hides globally");
Check(!DisplayPolicy.ShouldHide(5, true, true, false), "Ordinary maximized client area is not fullscreen");
Check(!DisplayPolicy.ShouldHide(5, false, true, true), "Invisible or minimized windows cannot trigger geometric fullscreen");
Console.WriteLine($"{count} checks passed.");

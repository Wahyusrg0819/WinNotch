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
    Check(placement.Width == Math.Round(500 * dpi / 96d) && placement.Height == Math.Round(310 * dpi / 96d) && placement.Top == screen.Top &&
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
long elapsedMs = 0;
var timer = new CountdownTimer(() => elapsedMs);
var completions = 0;
timer.Completed += () => completions++;
timer.Start(TimeSpan.FromMinutes(1));
elapsedMs += 12_345; timer.Tick();
Check(timer.Remaining == TimeSpan.FromMilliseconds(47_655), "Timer measures elapsed time, not callback count");
timer.TogglePause(); var pausedAt = timer.Remaining;
elapsedMs += 600_000; timer.Tick();
Check(timer.Status == TimerStatus.Paused && timer.Remaining == pausedAt, "Paused countdown does not consume time");
timer.Start(TimeSpan.FromMinutes(2));
Check(timer.Remaining == pausedAt, "Starting another timer cannot replace a paused session");
timer.TogglePause(); elapsedMs += 47_000; timer.Tick();
Check(timer.Status == TimerStatus.Running && timer.Remaining.TotalMilliseconds == 655, "Resume preserves the unelapsed fraction");
elapsedMs += 20_000; timer.Tick(); timer.Tick();
Check(timer.Status == TimerStatus.Completed && timer.Remaining == TimeSpan.Zero && completions == 1, "Delayed callback completes exactly once, never below zero");
timer.Cancel(); Check(!timer.IsActive && timer.FocusSessions == 0, "Cancel clears completed state and the Pomodoro cycle");
timer.Start(TimeSpan.FromSeconds(1)); elapsedMs += 2000; timer.TogglePause();
Check(timer.Status == TimerStatus.Completed, "Pause at the deadline completes instead of resurrecting the timer");
timer.Cancel();
foreach (var invalid in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(181) })
{
    var rejected = false;
    try { timer.Start(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected && !timer.IsActive, "Invalid duration leaves the timer unchanged");
}
timer.StartPomodoro();
for (var session = 1; session <= 4; session++)
{
    Check(timer.Mode == TimerMode.Focus && timer.Remaining == TimeSpan.FromMinutes(25), $"Pomodoro focus {session} starts at 25 minutes");
    elapsedMs += 25 * 60_000; timer.Tick();
    Check(timer.FocusSessions == session && timer.Status == TimerStatus.Completed, $"Focus {session} waits for explicit continuation");
    timer.NextPomodoro();
    var rest = session == 4 ? 15 : 5;
    Check(timer.Remaining == TimeSpan.FromMinutes(rest) && timer.Mode == (session == 4 ? TimerMode.LongBreak : TimerMode.ShortBreak), $"Focus {session} selects the correct break");
    elapsedMs += rest * 60_000; timer.Tick(); timer.NextPomodoro();
}
Check(timer.Mode == TimerMode.Focus && timer.FocusSessions == 4, "Long break returns to focus without losing the cycle count");
timer.Cancel(); timer.Start(TimeSpan.FromMinutes(10));
elapsedMs += 2 * 60 * 60_000; timer.Tick();
Check(timer.Status == TimerStatus.Completed && timer.FocusSessions == 0, "Large elapsed jump catches up and custom timers do not count as focus sessions");

state.HasMedia = true; state.HasTimer = true; state.Refresh();
Check(state.Current.State == NotchState.Compact, "Active timer remains compact alongside media");
state.Publish("volume", "", "Volume", "", 50, TimeSpan.FromSeconds(2));
time += TimeSpan.FromSeconds(3); state.Refresh();
Check(state.Current.State == NotchState.Compact && state.HasTimer, "Transient events return to the persistent timer");
state.Publish("timer", "", "Timer complete", "", 90, TimeSpan.FromSeconds(6));
state.Publish("battery", "", "Low battery", "", 100, TimeSpan.FromSeconds(3));
Check(state.Current.Event?.Key == "battery", "Critical battery warning wins over timer completion");
time += TimeSpan.FromSeconds(3.1); state.Refresh();
Check(state.Current.Event?.Key == "timer", "Timer completion resumes after critical event expires");
state.Suppressed = true; state.Refresh(); state.Suppressed = false; state.Refresh();
Check(state.Current.State == NotchState.Compact && state.HasTimer, "Fullscreen does not discard persistent timer completion");
state.HasTimer = false; state.Refresh();
Check(state.Current.State == NotchState.Compact && state.HasMedia, "Dismissing the timer restores media");
Console.WriteLine($"{count} checks passed.");
var displays = new[] { "primary", "left", "right" };
Check(DesktopPolicy.SelectDisplay(DisplayMode.Primary, "left", "right", displays, "primary") == "primary", "Primary mode ignores active and saved display");
Check(DesktopPolicy.SelectDisplay(DisplayMode.Selected, "LEFT", "right", displays, "primary") == "left", "Specific display uses saved name case-insensitively");
Check(DesktopPolicy.SelectDisplay(DisplayMode.FollowActive, "left", "right", displays, "primary") == "right", "Follow mode tracks the active display");
Check(DesktopPolicy.SelectDisplay(DisplayMode.Selected, "missing", "right", displays, "primary") == "primary", "Disconnected selected display falls back to primary");
Check(DesktopPolicy.SelectDisplay(DisplayMode.FollowActive, null, null, displays, "primary") == "primary", "No active display safely falls back to primary");
var exclusions = DesktopPolicy.NormalizeApps(new[] { "Chrome.EXE", "chrome.exe", @"C:\Apps\Code.exe", "", "*.exe", "WinNotch.exe", "ApplicationFrameHost.exe" });
Check(exclusions.SequenceEqual(new[] { "chrome.exe", "code.exe" }), "Exclusions normalize names and reject wildcards/self/shared hosts");
Check(DesktopPolicy.IsExcluded("CHROME.EXE", exclusions), "Excluded app matching ignores case");
Check(!DesktopPolicy.IsExcluded("chrome-helper.exe", exclusions) && !DesktopPolicy.IsExcluded(null, exclusions), "Exclusions require an exact executable name");
Check(DesktopPolicy.NormalizeApps(null).Length == 0, "Missing exclusion list is safe for old settings");
Console.WriteLine($"{count} total checks passed.");

state = new NotchStateManager(() => time) { HasMedia = true, Fullscreen = true, FullscreenBehavior = FullscreenMode.CriticalOnly };
state.Refresh();
Check(state.Current.State == NotchState.Hidden, "Critical-only fullscreen hides persistent media");
state.Publish("notification", "", "Private", "", 80, TimeSpan.FromSeconds(5));
state.Publish("timer", "", "Timer", "", 90, TimeSpan.FromSeconds(5));
Check(!state.HasEvents, "Critical-only fullscreen drops notifications and timer popups");
state.Publish("battery", "", "Low battery", "", 100, TimeSpan.FromSeconds(5));
Check(state.Current.State == NotchState.Peek && state.Current.Event?.Key == "battery", "Critical battery alert can peek in fullscreen");
state.Expand(); Check(state.Current.State == NotchState.Peek, "Critical fullscreen alert cannot open an interactive panel");
time += TimeSpan.FromSeconds(6); state.Refresh();
Check(state.Current.State == NotchState.Hidden, "Critical alert expiry restores fullscreen hiding");
state.Fullscreen = false; state.Refresh();
Check(state.Current.State == NotchState.Compact && !state.HasEvents, "Fullscreen exit restores media without replaying dropped events");
state.Publish("notification", "", "Private", "", 80, TimeSpan.FromSeconds(5));
state.Fullscreen = true; state.Refresh();
Check(!state.HasEvents && state.Current.State == NotchState.Hidden, "Entering fullscreen removes an existing noncritical preview");
state.FullscreenBehavior = FullscreenMode.Show; state.Refresh();
Check(state.Current.State == NotchState.Compact, "Show mode preserves ordinary fullscreen activity");
state.Suppressed = true; state.Publish("battery", "", "Low", "", 100, TimeSpan.FromSeconds(5)); state.Refresh();
Check(state.Current.State == NotchState.Hidden && !state.HasEvents, "Hard suppression overrides show mode and critical events");
state.Suppressed = false; state.FullscreenBehavior = FullscreenMode.Hide;
state.Publish("battery", "", "Low", "", 100, TimeSpan.FromSeconds(5));
Check(!state.HasEvents, "Hide-all fullscreen drops even critical events");
var notifications = new NotificationTracker();
var old = new NotificationStamp(1, time.AddMinutes(-1));
var fresh = new NotificationStamp(2, time);
Check(notifications.Update(new[] { old }, time).Length == 0, "Notification startup records a baseline without replaying history");
Check(notifications.Update(new[] { old, fresh }, time).SequenceEqual(new[] { fresh }), "Only a newly arrived notification is emitted");
Check(notifications.Update(new[] { old, fresh }, time).Length == 0, "Repeated notification snapshots do not duplicate previews");
notifications.Update(Array.Empty<NotificationStamp>(), time);
Check(notifications.Update(new[] { old }, time).Length == 0, "Old restored notifications are not replayed");
var reused = new NotificationStamp(1, time);
Check(notifications.Update(new[] { reused }, time).SequenceEqual(new[] { reused }), "Notification identifier reuse is distinguished by creation time");
notifications.Reset();
Check(notifications.Update(new[] { reused }, time).Length == 0, "Re-enabling starts a new baseline");
Console.WriteLine($"{count} total checks passed.");

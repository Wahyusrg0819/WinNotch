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

var calendarFolder = Path.Combine(Path.GetTempPath(), "WinNotch-calendar-check-" + Guid.NewGuid().ToString("N"));
var calendarPath = Path.Combine(calendarFolder, "agenda.json");
try
{
    var calendarNow = new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.FromHours(7));
    var calendar = new LocalCalendar(calendarPath, () => calendarNow);
    Check(!calendar.LoadFailed && calendar.Next == null, "Missing agenda starts empty");
    var later = calendar.Save(null, "Later", calendarNow.AddHours(2), true);
    var early = calendar.Save(null, "  First  ", calendarNow.AddHours(1).ToOffset(TimeSpan.FromHours(-4)), true);
    Check(calendar.Next?.Id == early.Id && calendar.Next.Title == "First", "Agenda sorts actual instants across offsets and trims titles");
    var loaded = new LocalCalendar(calendarPath, () => calendarNow);
    Check(loaded.Entries.SequenceEqual(calendar.Entries), "Agenda and reminder preferences survive a restart");
    foreach (var title in new[] { "", new string('x', 121), "Two\nlines" })
    {
        var rejected = false;
        try { calendar.Save(null, title, calendarNow.AddHours(1), true); } catch (ArgumentException) { rejected = true; }
        Check(rejected && calendar.Entries.Count == 2, "Invalid title cannot change the agenda");
    }
    var pastRejected = false;
    try { calendar.Save(null, "Past", calendarNow, true); } catch (ArgumentException) { pastRejected = true; }
    Check(pastRejected, "Past or current event time is rejected");
    Check(calendar.TakeDueReminders().Length == 0, "Reminders do not arrive early");
    calendarNow = calendarNow.AddMinutes(55);
    Check(calendar.TakeDueReminders().Single().Id == early.Id, "Reminder arrives exactly at the five-minute boundary");
    Check(calendar.TakeDueReminders().Length == 0 && new LocalCalendar(calendarPath, () => calendarNow).TakeDueReminders().Length == 0, "Polling and restart do not repeat delivered reminders");
    calendar.Save(early.Id, "Renamed", early.StartsAt, true);
    Check(calendar.TakeDueReminders().Length == 0, "Title edits do not replay a reminder");
    calendar.Save(early.Id, "Rescheduled", calendarNow.AddMinutes(4), true);
    Check(calendar.TakeDueReminders().Single().Id == early.Id && calendar.Entries.Count == 2, "Rescheduling rearms one reminder without duplicating the event");
    calendarNow = calendarNow.AddHours(3);
    Check(calendar.TakeDueReminders().Length == 0 && calendar.Next == null && calendar.Entries.Count == 2, "Resume after the event skips stale reminders and preserves past entries");
    var quiet = calendar.Save(null, "No reminder", calendarNow.AddMinutes(1), false);
    Check(calendar.TakeDueReminders().Length == 0, "Per-event reminder opt-out is respected");
    var together1 = calendar.Save(null, "Together one", calendarNow.AddMinutes(2), true);
    var together2 = calendar.Save(null, "Together two", calendarNow.AddMinutes(2), true);
    Check(calendar.TakeDueReminders().Length == 2, "Simultaneous reminders are all consumed in one durable update");
    calendarNow = calendarNow.AddMinutes(-10);
    Check(calendar.TakeDueReminders().Length == 0, "Moving the clock backwards does not send early reminders");
    calendarNow = calendarNow.AddMinutes(10);
    Check(calendar.TakeDueReminders().Length == 0, "Moving the system clock backwards does not replay consumed reminders");
    calendar.Delete(quiet.Id);
    Check(new LocalCalendar(calendarPath, () => calendarNow).Entries.All(entry => entry.Id != quiet.Id), "Delete persists across restart");
    Directory.CreateDirectory(calendarPath + ".tmp");
    var beforeFailure = File.ReadAllText(calendarPath);
    var failed = false;
    try { calendar.Save(later.Id, "Must not overwrite", calendarNow.AddHours(2), true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
    Check(failed && File.ReadAllText(calendarPath) == beforeFailure && calendar.Entries.Single(entry => entry.Id == later.Id).Title == "Later", "Failed save preserves disk and in-memory agenda");
    Directory.Delete(calendarPath + ".tmp");
    File.WriteAllText(calendarPath, "{broken");
    var broken = new LocalCalendar(calendarPath, () => calendarNow);
    failed = false;
    try { broken.Delete(later.Id); } catch (IOException) { failed = true; }
    Check(broken.LoadFailed && failed && File.ReadAllText(calendarPath) == "{broken", "Unreadable agenda is preserved instead of silently overwritten");
    File.WriteAllText(calendarPath, "[null]");
    Check(new LocalCalendar(calendarPath).LoadFailed, "Malformed entry is rejected safely");
}
finally { if (Directory.Exists(calendarFolder)) Directory.Delete(calendarFolder, true); }
Console.WriteLine($"{count} total checks passed.");

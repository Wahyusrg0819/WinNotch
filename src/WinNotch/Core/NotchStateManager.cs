using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core;

public enum NotchState { Hidden, Idle, Peek, Compact, Expanded }
public enum VisibilityMode { Always, ActiveOnly, SmartHide }
public sealed record NotchEvent(string Key, string Glyph, string Title, string Detail, int Priority, DateTimeOffset Expires, double? Level = null);
public sealed record NotchPresentation(NotchState State, NotchEvent? Event);

// One arbiter owns presentation; modules never resize or open the window.
public sealed class NotchStateManager
{
    private readonly Dictionary<string, NotchEvent> events = new();
    private readonly Func<DateTimeOffset> now;
    private bool expanded;
    public bool HasMedia { get; set; }
    public bool Suppressed { get; set; }
    public bool Paused { get; set; }
    public bool MaximizedApp { get; set; }
    public VisibilityMode Visibility { get; set; } = VisibilityMode.SmartHide;
    public NotchPresentation Current { get; private set; } = new(NotchState.Idle, null);
    public event Action<NotchPresentation>? Changed;
    public bool HasEvents => events.Count > 0;

    public NotchStateManager(Func<DateTimeOffset>? clock = null) => now = clock ?? (() => DateTimeOffset.UtcNow);

    public void Publish(string key, string glyph, string title, string detail, int priority, TimeSpan duration, double? level = null)
    {
        if (Suppressed || Paused) return;
        events[key] = new(key, glyph, title, detail, priority, now() + duration, level);
        Refresh();
    }

    public void Remove(string key) { events.Remove(key); Refresh(); }
    public void Expand() { if (!Suppressed && !Paused) expanded = true; Refresh(); }
    public void Collapse() { expanded = false; Refresh(); }

    public void Refresh()
    {
        foreach (var key in events.Where(pair => pair.Value.Expires <= now()).Select(pair => pair.Key).ToArray()) events.Remove(key);
        if (Suppressed || Paused) { expanded = false; events.Clear(); }
        var active = events.Values.OrderByDescending(e => e.Priority).ThenByDescending(e => e.Expires).FirstOrDefault();
        var state = Suppressed || Paused ? NotchState.Hidden
            : expanded ? NotchState.Expanded
            : active != null ? NotchState.Peek
            : HasMedia ? NotchState.Compact
            : Visibility == VisibilityMode.ActiveOnly || (Visibility == VisibilityMode.SmartHide && MaximizedApp) ? NotchState.Hidden
            : NotchState.Idle;
        var next = new NotchPresentation(state, active);
        if (next == Current) return;
        Current = next;
        Changed?.Invoke(next);
    }
}

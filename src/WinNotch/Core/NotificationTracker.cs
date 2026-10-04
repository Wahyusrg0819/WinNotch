using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core;

public readonly record struct NotificationStamp(uint Id, DateTimeOffset Created);

// Keep only identifiers in the baseline; never persist notification content.
public sealed class NotificationTracker
{
    private HashSet<NotificationStamp>? seen;
    public NotificationStamp[] Update(IEnumerable<NotificationStamp> current, DateTimeOffset now)
    {
        var next = current.ToHashSet();
        var added = seen == null ? Array.Empty<NotificationStamp>()
            : next.Where(item => !seen.Contains(item) && now - item.Created >= TimeSpan.Zero && now - item.Created <= TimeSpan.FromSeconds(15)).ToArray();
        seen = next;
        return added;
    }
    public void Reset() => seen = null;
}

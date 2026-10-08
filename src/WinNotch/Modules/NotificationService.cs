using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WinNotch.Core;

namespace WinNotch.Modules;

public sealed record NotificationPreview(string App, string Title, string Body);

public sealed class NotificationService : IDisposable
{
    private readonly DispatcherTimer poll;
    private readonly NotificationTracker tracker = new();
    private readonly Func<UserNotificationListenerAccessStatus> getAccess;
    private readonly Func<Task<IReadOnlyList<UserNotification>>> read;
    private readonly Func<Task<UserNotificationListenerAccessStatus>> requestAccess;
    private bool enabled, busy, disposed;
    private int generation;
    public string Status { get; private set; } = "Notification previews are off.";
    public event Action<NotificationPreview?>? Changed;

    public NotificationService(Dispatcher dispatcher) : this(dispatcher,
        () => UserNotificationListener.Current.GetAccessStatus(),
        async () => await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast),
        async () => await UserNotificationListener.Current.RequestAccessAsync()) { }

    internal NotificationService(Dispatcher dispatcher, Func<UserNotificationListenerAccessStatus> getAccess,
        Func<Task<IReadOnlyList<UserNotification>>> read, Func<Task<UserNotificationListenerAccessStatus>> requestAccess)
    {
        this.getAccess = getAccess; this.read = read; this.requestAccess = requestAccess;
        // Desktop NotificationChanged subscription is not reliable on all Windows builds.
        // A bounded, non-overlapping poll runs only after access was granted.
        poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await SyncAsync(), dispatcher);
        poll.Stop();
    }

    public async Task RequestAccessAsync()
    {
        if (disposed) return;
        try
        {
            var status = await requestAccess();
            if (disposed) return;
            Status = status == UserNotificationListenerAccessStatus.Allowed ? "Access granted. Enable previews and Save changes."
                : status == UserNotificationListenerAccessStatus.Denied ? "Access denied. You can allow it in Windows notification privacy settings."
                : "Access was not granted. You can try again.";
            if (enabled) { poll.Start(); await SyncAsync(); }
        }
        catch (Exception ex)
        {
            if (disposed) return;
            SettingsStore.Log("notifications.permission", ex);
            Status = "Windows could not grant access to this build. Use the MSIX build and try again.";
        }
    }

    public void SetEnabled(bool value)
    {
        if (disposed || enabled == value) return;
        enabled = value; generation++; tracker.Reset(); poll.Stop(); Changed?.Invoke(null);
        if (value) _ = SyncAsync();
        else Status = "Notification previews are off.";
    }

    internal async Task SyncAsync()
    {
        if (!enabled || disposed || busy) return;
        busy = true;
        var version = generation;
        try
        {
            if (!CheckAccess()) return;
            var notifications = await read();
            if (disposed || version != generation) return;
            if (!CheckAccess()) return;
            var added = tracker.Update(notifications.Select(n => new NotificationStamp(n.Id, n.CreationTime)), DateTimeOffset.UtcNow);
            foreach (var stamp in added.OrderBy(n => n.Created))
            {
                try
                {
                    var notification = notifications.First(n => n.Id == stamp.Id && n.CreationTime == stamp.Created);
                    var text = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                    if (text == null || text.Count == 0) continue;
                    Changed?.Invoke(new(Clip(notification.AppInfo.DisplayInfo.DisplayName, 80), Clip(text[0].Text, 160),
                        Clip(string.Join("\n", text.Skip(1).Select(t => t.Text)), 1200)));
                }
                catch (Exception ex) { SettingsStore.Log("notifications.item", ex); }
            }
            Status = "Listening locally. Existing notifications are skipped; Windows originals are kept.";
            poll.Start();
        }
        catch (Exception ex)
        {
            if (disposed || version != generation) return;
            poll.Stop(); tracker.Reset(); Changed?.Invoke(null);
            SettingsStore.Log("notifications.read", ex);
            Status = "Notification access is unavailable. Try Allow access or the MSIX build.";
        }
        finally
        {
            busy = false;
            if (!disposed && enabled && version != generation) _ = SyncAsync();
        }
    }

    private bool CheckAccess()
    {
        var status = getAccess();
        if (status == UserNotificationListenerAccessStatus.Allowed) return true;
        poll.Stop(); tracker.Reset(); Changed?.Invoke(null);
        Status = status == UserNotificationListenerAccessStatus.Denied
            ? "Access denied. You can allow it in Windows notification privacy settings."
            : "Permission needed. Select Allow access, then enable previews and Save changes.";
        return false;
    }

    private static string Clip(string? value, int length) => string.IsNullOrWhiteSpace(value) ? "" : value.Length > length ? value[..length] : value;
    public void Dispose() { disposed = true; generation++; poll.Stop(); tracker.Reset(); }
}

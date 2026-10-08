using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WinNotch.Modules;

namespace WinNotch;

// Explicit test entry points only. Permission checks use fakes; live checks send their own silent toasts.
internal static class NotificationValidation
{
    internal static async Task CheckPermissionsAsync(Dispatcher dispatcher, Dictionary<string, object> checks)
    {
        var access = UserNotificationListenerAccessStatus.Denied;
        var reads = 0; var requests = 0; var cleared = 0;
        Task<IReadOnlyList<UserNotification>> next = Task.FromResult<IReadOnlyList<UserNotification>>(Array.Empty<UserNotification>());
        using var service = new NotificationService(dispatcher, () => access, () => { reads++; return next; },
            () => { requests++; return Task.FromResult(access); });
        service.Changed += preview => { if (preview == null) cleared++; };
        service.SetEnabled(true);
        checks["notifications.deniedDoesNotReadOrPrompt"] = reads == 0 && requests == 0 && service.Status.StartsWith("Access denied.");
        await service.RequestAccessAsync();
        checks["notifications.deniedExplainsWindowsSettings"] = requests == 1 && reads == 0 && service.Status.Contains("Windows notification privacy settings");
        access = UserNotificationListenerAccessStatus.Unspecified;
        await service.SyncAsync();
        checks["notifications.unspecifiedNeedsPermission"] = reads == 0 && service.Status.StartsWith("Permission needed.");
        access = UserNotificationListenerAccessStatus.Allowed;
        await service.RequestAccessAsync();
        checks["notifications.allowedResumesReading"] = reads == 1 && service.Status.StartsWith("Listening locally.");
        var previousClears = cleared;
        access = UserNotificationListenerAccessStatus.Denied;
        await service.SyncAsync();
        checks["notifications.revocationClearsPreview"] = cleared == previousClears + 1 && reads == 1 && service.Status.StartsWith("Access denied.");
        service.SetEnabled(false);
        access = UserNotificationListenerAccessStatus.Allowed;
        var pending = new TaskCompletionSource<IReadOnlyList<UserNotification>>();
        next = pending.Task;
        service.SetEnabled(true);
        access = UserNotificationListenerAccessStatus.Denied;
        previousClears = cleared;
        pending.SetResult(Array.Empty<UserNotification>());
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        checks["notifications.revokedDuringReadIsRejected"] = service.Status.StartsWith("Access denied.") && cleared == previousClears + 1;
        service.SetEnabled(false);
        access = UserNotificationListenerAccessStatus.Allowed;
        pending = new(); next = pending.Task;
        service.SetEnabled(true);
        service.SetEnabled(false);
        pending.SetException(new InvalidOperationException("Synthetic stale read"));
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        checks["notifications.staleFailureKeepsOffStatus"] = service.Status == "Notification previews are off.";
        pending = new(); next = pending.Task;
        service.SetEnabled(true);
        service.SetEnabled(false);
        service.SetEnabled(true);
        next = Task.FromResult<IReadOnlyList<UserNotification>>(Array.Empty<UserNotification>());
        var previousReads = reads;
        pending.SetResult(Array.Empty<UserNotification>());
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        checks["notifications.reenableWaitsForOldRead"] = reads == previousReads + 1 && service.Status.StartsWith("Listening locally.");
        service.SetEnabled(false);
        pending = new(); next = pending.Task;
        service.SetEnabled(true);
        service.Dispose();
        var status = service.Status;
        previousClears = cleared;
        pending.SetException(new InvalidOperationException("Synthetic disposed read"));
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        await service.RequestAccessAsync();
        checks["notifications.disposedIgnoresPendingWork"] = service.Status == status && cleared == previousClears && requests == 2;
    }

    internal static async Task RunLiveAsync(App app, string output, string senderScript)
    {
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, object>();
        try
        {
            if (!app.Settings.Notifications) throw new InvalidOperationException("Enable notification previews before running this explicit live test.");
            await Task.Delay(2500); // Allow the real listener to establish its history baseline.
            if (!app.Notifications.Status.StartsWith("Listening locally.")) throw new InvalidOperationException("Notification access must already be allowed; this test never requests permission.");
            foreach (var sender in new[] { "Windows PowerShell", "Windows PowerShell ISE" })
            {
                var title = "WinNotch validation " + Guid.NewGuid().ToString("N")[..8];
                await SendAsync(sender, title, false);
                for (var i = 0; i < 40 && app.LatestNotification?.Title != title; i++) await Task.Delay(250);
                Expect(app.LatestNotification?.Title == title && app.LatestNotification.App == sender, sender + ".delivery");
                app.Overlay.Expand();
                var panel = app.Overlay.ExpandedContent!;
                panel.ShowNotification();
                await Task.Delay(350);
                Expect(((TextBlock)panel.FindName("NotificationTitle")).Text == title, sender + ".detail");
                SmokeTest.Capture(app.Overlay, Path.Combine(output, sender + "-preview.png"));
                ((Button)panel.FindName("DismissNotificationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Expect(app.LatestNotification == null && ((TextBlock)panel.FindName("NotificationTitle")).Text == "Nothing new here.", sender + ".dismiss");
                SmokeTest.Capture(app.Overlay, Path.Combine(output, sender + "-dismissed.png"));
                Expect(await SendAsync(sender, title, true), sender + ".windowsOriginalRetained");
                await Task.Delay(2300);
                Expect(app.LatestNotification?.Title != title, sender + ".dismissNotReplayed");
                app.State.Collapse();
            }
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(output, "notification-live-results.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
        void Expect(bool passed, string name)
        {
            checks[name] = passed;
            if (!passed) throw new InvalidOperationException("Notification check failed: " + name);
        }
        async Task<bool> SendAsync(string sender, string title, bool checkOnly)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-File", Path.GetFullPath(senderScript), "-Sender", sender, "-Title", title }) start.ArgumentList.Add(arg);
            if (checkOnly) start.ArgumentList.Add("-CheckOnly");
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { process.Kill(); throw; }
            if (process.ExitCode != 0) throw new InvalidOperationException("Test sender failed: " + await stderr);
            using var result = JsonDocument.Parse(await stdout);
            return result.RootElement.GetProperty("TestToastPresentInWindows").GetBoolean();
        }
    }
}

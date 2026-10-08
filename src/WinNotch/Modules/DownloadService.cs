using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinNotch.Core;
using WinNotch.Native;

namespace WinNotch.Modules;

public sealed record DownloadItem(string Path, long Bytes, DateTime Written, bool Partial)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Detail => (Partial ? "Temporary file · " : "Final name observed · ") +
        (Bytes >= 1073741824 ? $"{Bytes / 1073741824d:0.##} GiB" : Bytes >= 1048576 ? $"{Bytes / 1048576d:0.##} MiB" : Bytes >= 1024 ? $"{Bytes / 1024d:0.##} KiB" : $"{Bytes} B");
}

public sealed class DownloadService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer poll;
    private readonly object gate = new();
    private readonly HashSet<string> pendingFinals = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? watcher;
    private DownloadItem[] recent = Array.Empty<DownloadItem>();
    private bool enabled, disposed, busy, scheduled, recover, missed;
    private int generation;
    public string Folder { get; private set; } = "";
    public string Status { get; private set; } = "Downloads are off.";
    public DownloadItem[] Items { get; private set; } = Array.Empty<DownloadItem>();
    public event Action? Changed;
    public event Action<DownloadItem[]>? Completed;
    internal bool IsWatching => watcher != null;
    internal bool IsPolling => poll.IsEnabled;

    public DownloadService(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        poll = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        poll.Tick += async (_, _) => { poll.Stop(); await RefreshAsync(); };
    }

    internal static bool IsPartial(string? path) => path?.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) == true || path?.EndsWith(".part", StringComparison.OrdinalIgnoreCase) == true;

    public void Configure(bool value, string folder, bool retry = false)
    {
        if (disposed) return;
        var resolved = string.IsNullOrWhiteSpace(folder) ? Win32.DownloadsFolder() : folder;
        if (!retry && enabled == value && string.Equals(Folder, resolved, StringComparison.OrdinalIgnoreCase)) return;
        lock (gate) { generation++; enabled = value; pendingFinals.Clear(); scheduled = recover = missed = false; }
        poll.Stop(); watcher?.Dispose(); watcher = null;
        Folder = resolved; recent = Items = Array.Empty<DownloadItem>();
        Status = value ? "Checking the downloads folder…" : "Downloads are off.";
        Changed?.Invoke();
        if (value) _ = RefreshAsync();
    }

    private FileSystemWatcher Watch(string folder, int version)
    {
        var source = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite };
        source.Created += (_, e) => { if (IsPartial(e.Name)) QueueChange(version); };
        source.Changed += (_, e) => { if (IsPartial(e.Name)) QueueChange(version); };
        source.Deleted += (_, _) => QueueChange(version);
        source.Renamed += (_, e) => QueueChange(version, IsPartial(e.OldName) && !IsPartial(e.Name) ? e.FullPath : null);
        source.Error += (_, _) => QueueChange(version, error: true);
        try { source.EnableRaisingEvents = true; return source; }
        catch { source.Dispose(); throw; }
    }

    private void QueueChange(int version, string? final = null, bool error = false)
    {
        lock (gate)
        {
            if (disposed || !enabled || generation != version) return;
            if (error || pendingFinals.Count >= 64) { recover = missed = true; pendingFinals.Clear(); }
            else if (final != null && !recover) pendingFinals.Add(final);
            if (scheduled) return;
            scheduled = true;
        }
        // Coalesce bursts into one dispatcher request and one background read.
        if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(() =>
        {
            if (!disposed && enabled && generation == version && !poll.IsEnabled) Schedule(0.5);
        });
    }

    private void Schedule(double seconds) { poll.Interval = TimeSpan.FromSeconds(seconds); poll.Start(); }

    internal async Task RefreshAsync()
    {
        if (!enabled || disposed || busy) return;
        busy = true;
        var version = generation;
        var folder = Folder;
        string[] finals;
        bool restart;
        lock (gate) { finals = pendingFinals.ToArray(); pendingFinals.Clear(); scheduled = false; restart = recover; recover = false; }
        if (restart) { watcher?.Dispose(); watcher = null; finals = Array.Empty<string>(); }
        var previous = recent;
        var needsWatcher = watcher == null;
        FileSystemWatcher? created = null;
        try
        {
            var result = await Task.Run(() =>
            {
                if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("An absolute downloads folder is required.");
                if (needsWatcher) created = Watch(folder, version);
                // ponytail: cap at 32 temporary files and five recent completions; paginate only if real usage needs more.
                var paths = Directory.EnumerateFiles(folder, "*.crdownload").Concat(Directory.EnumerateFiles(folder, "*.part")).Take(33).ToArray();
                var partials = paths.Take(32).Select(path => Read(path, true)).OfType<DownloadItem>().ToArray();
                var ready = finals.Concat(previous.Select(item => item.Path)).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(path => Read(path, false)).OfType<DownloadItem>().ToArray();
                return (partials, ready, limited: paths.Length > 32);
            });
            if (disposed || generation != version) { created?.Dispose(); return; }
            watcher ??= created;
            var completed = result.ready.Where(item => finals.Contains(item.Path, StringComparer.OrdinalIgnoreCase) && !previous.Contains(item)).ToArray();
            recent = completed.Reverse().Concat(previous.Where(item => result.ready.Any(file => file.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase))))
                .DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
            var next = result.partials.Concat(recent).ToArray();
            var status = (result.limited ? "Showing the first 32 temporary files." : result.partials.Length > 0 ? "Temporary files may be active, paused, or interrupted." : "Watching for .crdownload and .part files.") +
                (missed ? " Some changes were missed; the current folder was refreshed." : "");
            if (!Items.SequenceEqual(next) || Status != status) { Items = next; Status = status; Changed?.Invoke(); }
            if (completed.Length > 0) Completed?.Invoke(completed);
            bool pending;
            lock (gate) pending = scheduled;
            if (pending) Schedule(0.5); else if (result.partials.Length > 0) Schedule(2); else poll.Stop();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            created?.Dispose();
            if (disposed || generation != version) return;
            watcher?.Dispose(); watcher = null; Items = recent = Array.Empty<DownloadItem>();
            if (!Status.StartsWith("Folder unavailable", StringComparison.Ordinal)) SettingsStore.Log("downloads.watch", ex);
            Status = "Folder unavailable. Check the folder in Settings. Retrying in 30 seconds."; Changed?.Invoke(); Schedule(30);
        }
        finally
        {
            busy = false;
            if (!disposed && enabled && generation != version) _ = RefreshAsync();
        }
    }

    private static DownloadItem? Read(string path, bool partial)
    {
        try
        {
            var file = new FileInfo(path);
            // Never open file contents or follow file links when collecting download metadata.
            return file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
                ? new(file.FullName, file.Length, file.LastWriteTimeUtc, partial) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; generation++; pendingFinals.Clear(); scheduled = false; }
        poll.Stop(); watcher?.Dispose(); watcher = null; Items = recent = Array.Empty<DownloadItem>();
    }
}

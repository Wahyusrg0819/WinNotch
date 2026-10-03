using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinNotch.Core;

namespace WinNotch.Modules;

public sealed record MediaSnapshot(string Title, string Artist, string Source, bool Playing, bool CanPlayPause,
    bool CanPrevious, bool CanNext, TimeSpan Position, TimeSpan Duration, BitmapSource? Artwork);

public sealed class MediaService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private bool disposed, refreshing, pending, metadataDirty;
    private bool enabled = true;
    private int generation;
    public MediaSnapshot? Current { get; private set; }
    public string Status { get; private set; } = "Connecting to Windows media…";
    public string? DiagnosticError { get; private set; }
    public event Action<MediaSnapshot?, bool>? Changed;

    public MediaService(Dispatcher dispatcher) => this.dispatcher = dispatcher;

    public void SetEnabled(bool value)
    {
        if (enabled == value) return;
        enabled = value;
        if (enabled) SelectSession();
        else
        {
            Unsubscribe(); session = null; generation++; Current = null;
            Status = "Media module is turned off."; Changed?.Invoke(null, false);
        }
    }

    public async Task StartAsync()
    {
        try
        {
            var result = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (disposed) return;
            manager = result;
            manager.CurrentSessionChanged += OnSession;
            manager.SessionsChanged += OnSessions;
            SelectSession();
        }
        catch (Exception ex) { DiagnosticError = $"{ex.GetType().Name}: 0x{ex.HResult:X8}"; Status = "Windows media is unavailable. Try restarting WinNotch."; SettingsStore.Log("media.initialize", ex); Changed?.Invoke(null, false); }
    }

    private void OnSession(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => dispatcher.BeginInvoke(SelectSession);
    private void OnSessions(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => dispatcher.BeginInvoke(SelectSession);
    private void OnProperties(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => QueueRefresh(true);
    private void OnPlayback(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => QueueRefresh(false);
    private void OnTimeline(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => QueueRefresh(false);

    private void SelectSession()
    {
        if (disposed || !enabled) return;
        GlobalSystemMediaTransportControlsSession? next;
        try { next = manager?.GetCurrentSession(); }
        catch (Exception ex)
        {
            SettingsStore.Log("media.session", ex); Unsubscribe(); session = null; generation++;
            Current = null; Status = "Media session unavailable. Open your player again."; Changed?.Invoke(null, false); return;
        }
        if (next == session) { QueueRefresh(true); return; }
        Unsubscribe();
        generation++;
        session = next;
        Current = null;
        if (session != null)
        {
            session.MediaPropertiesChanged += OnProperties;
            session.PlaybackInfoChanged += OnPlayback;
            session.TimelinePropertiesChanged += OnTimeline;
        }
        QueueRefresh(true);
    }

    private void QueueRefresh(bool metadata) => dispatcher.BeginInvoke(async () =>
    {
        if (disposed || !enabled) return;
        metadataDirty |= metadata;
        pending = true;
        if (refreshing) return;
        refreshing = true;
        try
        {
            while (pending && !disposed)
            {
                pending = false;
                var readMetadata = metadataDirty;
                metadataDirty = false;
                await RefreshAsync(readMetadata);
            }
        }
        finally { refreshing = false; }
    });

    private async Task RefreshAsync(bool metadata)
    {
        var target = session;
        var version = generation;
        if (target == null)
        {
            Current = null; Status = "Play something. We'll meet you here."; Changed?.Invoke(null, false); return;
        }
        try
        {
            var title = Current?.Title ?? "";
            var artist = Current?.Artist ?? "";
            var artwork = Current?.Artwork;
            if (metadata || Current == null)
            {
                var properties = await target.TryGetMediaPropertiesAsync();
                title = properties.Title;
                artist = properties.Artist;
                artwork = null;
                if (properties.Thumbnail != null)
                {
                    try
                    {
                        using var stream = await properties.Thumbnail.OpenReadAsync();
                        if (stream.Size is > 0 and < 8_000_000)
                        {
                            using var reader = new DataReader(stream.GetInputStreamAt(0));
                            await reader.LoadAsync((uint)stream.Size);
                            var bytes = new byte[(int)stream.Size];
                            reader.ReadBytes(bytes);
                            using var memory = new MemoryStream(bytes);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = 192; bitmap.StreamSource = memory; bitmap.EndInit(); bitmap.Freeze();
                            artwork = bitmap;
                        }
                    }
                    catch (Exception ex) { SettingsStore.Log("media.artwork", ex); }
                }
            }
            if (disposed || version != generation) return;
            var playback = target.GetPlaybackInfo();
            var timeline = target.GetTimelineProperties();
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var position = timeline.Position - timeline.StartTime;
            if (playing) position += (DateTimeOffset.UtcNow - timeline.LastUpdatedTime) * (playback.PlaybackRate ?? 1);
            var duration = timeline.EndTime - timeline.StartTime;
            position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Math.Max(0, duration.TotalSeconds)));
            var trackChanged = title != Current?.Title || artist != Current?.Artist;
            Current = new(string.IsNullOrWhiteSpace(title) ? "Untitled media" : title, artist, target.SourceAppUserModelId,
                playing, playback.Controls.IsPlayPauseToggleEnabled, playback.Controls.IsPreviousEnabled, playback.Controls.IsNextEnabled,
                position, duration, artwork);
            Status = "Connected to Windows media";
            Changed?.Invoke(Current, trackChanged);
        }
        catch (Exception ex)
        {
            if (disposed || version != generation) return;
            Current = null; Status = "Media session unavailable. Open your player again.";
            SettingsStore.Log("media.read", ex); Changed?.Invoke(null, false);
        }
    }

    public void RefreshTimeline() => QueueRefresh(false);

    public async Task ControlAsync(string command)
    {
        var target = session;
        if (target == null) return;
        try
        {
            bool success = command switch
            {
                "previous" => await target.TrySkipPreviousAsync(),
                "next" => await target.TrySkipNextAsync(),
                _ => await target.TryTogglePlayPauseAsync()
            };
            if (!success) { Status = "This player did not accept the control."; Changed?.Invoke(Current, false); }
            QueueRefresh(false);
        }
        catch (Exception ex) { SettingsStore.Log("media.control", ex); Status = "Couldn't control this player."; Changed?.Invoke(Current, false); }
    }

    private void Unsubscribe()
    {
        if (session == null) return;
        session.MediaPropertiesChanged -= OnProperties;
        session.PlaybackInfoChanged -= OnPlayback;
        session.TimelinePropertiesChanged -= OnTimeline;
    }

    public void Dispose()
    {
        disposed = true; generation++;
        Unsubscribe();
        if (manager != null) { manager.CurrentSessionChanged -= OnSession; manager.SessionsChanged -= OnSessions; }
    }
}

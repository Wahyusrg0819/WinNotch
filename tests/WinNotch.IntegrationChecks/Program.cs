using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using WinNotch.Modules;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await RunAsync(app); app.Shutdown(); }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); }
        };
        Environment.ExitCode = app.Run();
    }

    private static async Task RunAsync(Application app)
    {
        var output = Path.GetFullPath("artifacts/integration");
        Directory.CreateDirectory(output);
        var wav = Path.Combine(output, "silent.wav");
        using (var writer = new BinaryWriter(File.Create(wav)))
        {
            const int bytes = 8000 * 2 * 60;
            writer.Write("RIFF"u8); writer.Write(bytes + 36); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000);
            writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
        }
        var art = Path.Combine(output, "cover.png");
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(200, 247, 173)), null, new Rect(0, 0, 192, 192));
            drawing.DrawRoundedRectangle(Brushes.Black, null, new Rect(46, 30, 100, 115), 28, 28);
        }
        var bitmap = new RenderTargetBitmap(192, 192, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(art)) png.Save(file);

        using var player = new Windows.Media.Playback.MediaPlayer { IsLoopingEnabled = true, Volume = 0 };
        player.CommandManager.IsEnabled = false;
        var transport = player.SystemMediaTransportControls;
        transport.IsEnabled = true; transport.IsPlayEnabled = true; transport.IsPauseEnabled = true;
        transport.IsNextEnabled = true; transport.IsPreviousEnabled = true;
        transport.DisplayUpdater.Type = MediaPlaybackType.Music;
        transport.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(art));
        var track = 1;
        void UpdateTrack()
        {
            transport.DisplayUpdater.MusicProperties.Title = $"WinNotch integration fixture {track}";
            transport.DisplayUpdater.MusicProperties.Artist = "Silent local test";
            transport.DisplayUpdater.Update();
        }
        player.PlaybackSession.PlaybackStateChanged += (_, _) => app.Dispatcher.InvokeAsync(() =>
            transport.PlaybackStatus = player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused);
        transport.ButtonPressed += (_, args) => app.Dispatcher.InvokeAsync(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play: player.Play(); break;
                case SystemMediaTransportControlsButton.Pause: player.Pause(); break;
                case SystemMediaTransportControlsButton.Next: track++; UpdateTrack(); break;
                case SystemMediaTransportControlsButton.Previous: track--; UpdateTrack(); break;
            }
        });
        player.Source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(wav));
        UpdateTrack(); player.Play();
        using var media = new MediaService(app.Dispatcher);
        await media.StartAsync();
        async Task Expect(Func<bool> condition, string name)
        {
            for (var attempt = 0; attempt < 40 && !condition(); attempt++) await Task.Delay(200);
            if (!condition()) throw new Exception($"FAIL {name}; media status: {media.Status}; diagnostic: {media.DiagnosticError}");
            Console.WriteLine($"PASS {name}");
        }
        await Expect(() => media.Current?.Title == "WinNotch integration fixture 1", "Media metadata change reaches module");
        await Expect(() => media.Current?.Artwork != null, "Album art decodes");
        await Expect(() => media.Current?.Playing == true, "Playback status reaches module");
        // Only issue controls while our explicitly named silent fixture is current.
        async Task Control(string command)
        {
            if (media.Current?.Title.StartsWith("WinNotch integration fixture ") != true) throw new Exception("Fixture lost focus; refusing to control another media session.");
            await media.ControlAsync(command);
        }
        await Control("play"); await Expect(() => media.Current?.Playing == false, "Play/pause pauses actual system session");
        await Control("play"); await Expect(() => media.Current?.Playing == true, "Play/pause resumes actual system session");
        await Control("next"); await Expect(() => media.Current?.Title == "WinNotch integration fixture 2", "Next reaches system session");
        await Control("previous"); await Expect(() => media.Current?.Title == "WinNotch integration fixture 1", "Previous reaches system session");
        media.SetEnabled(false);
        await Expect(() => media.Current == null, "Disabling media releases its active snapshot");
        media.SetEnabled(true);
        await Expect(() => media.Current?.Title == "WinNotch integration fixture 1", "Re-enabling media reconnects to the system session");
        using var audio = new AudioService(app.Dispatcher);
        using var battery = new BatteryService(app.Dispatcher);
        Console.WriteLine($"READ audio endpoint available={audio.Current.Available}; battery present={battery.Current.Present}");
        player.Pause(); transport.IsEnabled = false;
        Console.WriteLine("9 native media integration checks passed.");
    }
}

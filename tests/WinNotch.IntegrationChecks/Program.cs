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
using WinNotch.Native;
using System.Windows.Interop;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                if (Environment.GetCommandLineArgs().Contains("--controls-only")) await CheckAudioControlsAsync(app);
                else await RunAsync(app);
                app.Shutdown();
            }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); }
        };
        Environment.ExitCode = app.Run();
    }

    private static async Task CheckAudioControlsAsync(Application app)
    {
        using var audio = new AudioService(app.Dispatcher);
        if (!audio.Current.Available) throw new Exception("An audio output is needed for the control integration check.");
        var enumerator = (IMMDeviceEnumerator)new DeviceEnumeratorCom();
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;
        var context = Guid.NewGuid();
        float originalLevel = 0;
        bool originalMute = false, captured = false;
        var passed = 0;
        void Expect(bool result, string name)
        { if (!result) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); passed++; }
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
            var id = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, IntPtr.Zero, out var instance));
            endpoint = (IAudioEndpointVolume)instance;
            Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out originalLevel));
            Marshal.ThrowExceptionForHR(endpoint.GetMute(out originalMute));
            captured = true;
            var ownPeeks = 0;
            audio.Changed += (_, changed) => { if (changed) ownPeeks++; };
            // Silence first, so testing mute/unmute cannot increase audible output.
            Expect(audio.SetVolume(0), "Volume write succeeds");
            Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var actual));
            Expect(actual == 0 && audio.Current.Percent == 0, "Independent endpoint read confirms the volume write");
            Expect(audio.SetMuted(true), "Mute write succeeds");
            Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
            Expect(muted && audio.Current.Muted, "Independent endpoint read confirms mute");
            Expect(audio.SetMuted(false), "Unmute works while volume is zero");
            Marshal.ThrowExceptionForHR(endpoint.GetMute(out muted));
            Expect(!muted && !audio.Current.Muted, "Independent endpoint read confirms unmute");
            await Task.Delay(200);
            Expect(ownPeeks == 0, "Our controls do not generate duplicate volume peeks");
            Expect(!audio.SetVolume(double.NaN) && !audio.SetVolume(double.PositiveInfinity) && !audio.SetVolume(-1) && !audio.SetVolume(101), "Nonfinite and out-of-range volume inputs are rejected");
            Marshal.ThrowExceptionForHR(endpoint.SetMute(true, ref context));
            for (var i = 0; i < 20 && !audio.Current.Muted; i++) await Task.Delay(50);
            Expect(audio.Current.Muted && ownPeeks > 0, "External endpoint changes flow back to the control state");
        }
        finally
        {
            try
            {
                if (captured && endpoint != null)
                {
                    Marshal.ThrowExceptionForHR(endpoint.SetMute(originalMute, ref context));
                    Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(originalLevel, ref context));
                    Marshal.ThrowExceptionForHR(endpoint.GetMute(out var restoredMute));
                    Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var restoredLevel));
                    Expect(restoredMute == originalMute && Math.Abs(restoredLevel - originalLevel) < 0.0001, "Original volume and mute state restored exactly");
                }
            }
            finally
            {
                if (endpoint != null) Marshal.ReleaseComObject(endpoint);
                if (device != null) Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
        }
        audio.Dispose();
        Expect(!audio.SetVolume(10) && !audio.SetMuted(true), "Disposed audio service safely rejects controls");
        Console.WriteLine($"{passed} native audio control checks passed.");
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
        await CheckFullscreenAsync(app);
    }

    private static async Task CheckFullscreenAsync(Application app)
    {
        using var foreground = new ForegroundService(app.Dispatcher, IntPtr.Zero);
        var hidden = false;
        foreground.Changed += (fullscreen, _) => hidden = fullscreen;
        var window = new Window { Title = "WinNotch fullscreen test", Width = 480, Height = 280, Topmost = true, ResizeMode = ResizeMode.NoResize,
            Background = Brushes.Black, Foreground = Brushes.White,
            Content = new System.Windows.Controls.TextBlock { Text = "WinNotch window test\nThis test closes automatically.", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        try
        {
            window.Show(); window.Activate();
            var hwnd = new WindowInteropHelper(window).Handle;
            var screen = Win32.PrimaryMonitor().Info.Monitor;
            Win32.SetWindowPos(hwnd, new IntPtr(-1), screen.Left + 100, screen.Top + 100, 480, 280, 0);
            Console.WriteLine("WAIT Bring 'WinNotch fullscreen test' to the foreground if Windows prevents activation.");
            for (var i = 0; i < 300 && Win32.GetForegroundWindow() != hwnd; i++) await Task.Delay(100);
            if (Win32.GetForegroundWindow() != hwnd) throw new Exception("Test fixture could not obtain foreground focus.");
            await Task.Delay(700);
            foreground.Check();
            if (hidden)
            {
                Win32.SHQueryUserNotificationState(out var shellState);
                throw new Exception($"Normal window incorrectly detected as fullscreen. ShellState={shellState}; covers={Win32.CoversMonitor(hwnd, screen)}; foreground={Win32.GetForegroundWindow() == hwnd}.");
            }
            Console.WriteLine("PASS Normal window stays visible");
            window.WindowStyle = WindowStyle.None;
            var clock = Stopwatch.StartNew();
            Win32.SetWindowPos(hwnd, new IntPtr(-1), screen.Left, screen.Top, screen.Width, screen.Height, 0);
            for (var i = 0; i < 30 && !hidden; i++) await Task.Delay(100);
            if (!hidden)
            {
                Win32.GetClientRect(hwnd, out var client);
                var origin = new Win32.Point(0, 0); Win32.ClientToScreen(hwnd, ref origin);
                throw new Exception($"Real borderless fullscreen was not detected. Foreground={Win32.GetForegroundWindow() == hwnd}; client={origin.X},{origin.Y} {client.Width}x{client.Height}; monitor={screen.Left},{screen.Top} {screen.Width}x{screen.Height}.");
            }
            Console.WriteLine($"PASS Native fullscreen detected via window events ({clock.ElapsedMilliseconds} ms)");
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            Win32.SetWindowPos(hwnd, new IntPtr(-1), screen.Left + 100, screen.Top + 100, 480, 280, 0);
            await Task.Delay(180);
            if (!hidden) throw new Exception("Fullscreen restoration did not wait for its debounce.");
            Console.WriteLine("PASS Fullscreen exit respects restore delay");
            for (var i = 0; i < 25 && hidden; i++) await Task.Delay(100);
            if (hidden) throw new Exception("Foreground service did not restore after fullscreen exit.");
            Console.WriteLine("PASS Fullscreen exit restores visibility, including own-process windows");
        }
        finally { window.Close(); }
    }
}

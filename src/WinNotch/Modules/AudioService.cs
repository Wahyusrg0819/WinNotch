using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinNotch.Core;
using WinNotch.Native;

namespace WinNotch.Modules;

public sealed record AudioSnapshot(bool Available, int Percent, bool Muted);

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class AudioService : IAudioEndpointVolumeCallback, IMMNotificationClient, IDisposable
{
    private readonly Dispatcher dispatcher;
    private IMMDeviceEnumerator? enumerator;
    private IMMDevice? device;
    private IAudioEndpointVolume? volume;
    private bool disposed;
    private Guid changeContext = Guid.NewGuid();
    public AudioSnapshot Current { get; private set; } = new(false, 0, false);
    public event Action<AudioSnapshot, bool>? Changed;

    public AudioService(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        try
        {
            enumerator = (IMMDeviceEnumerator)new DeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(this));
            Bind();
        }
        catch (Exception ex) { SettingsStore.Log("audio.initialize", ex); }
    }

    private void Bind()
    {
        if (disposed) return;
        ReleaseEndpoint();
        try
        {
            if (enumerator == null) return;
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
            var id = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, IntPtr.Zero, out var instance));
            volume = (IAudioEndpointVolume)instance;
            Marshal.ThrowExceptionForHR(volume.RegisterControlChangeNotify(this));
            ReadCurrent();
        }
        catch (Exception ex) { ReleaseEndpoint(); Current = new(false, 0, false); SettingsStore.Log("audio.endpoint", ex); }
        Changed?.Invoke(Current, false);
    }

    public int OnNotify(IntPtr data)
    {
        if (disposed) return 0;
        var notification = Marshal.PtrToStructure<AudioNotification>(data);
        dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            // Read the currently bound device: a queued callback may belong to a removed endpoint.
            try
            {
                var previous = Current;
                ReadCurrent();
                if (previous != Current) Changed?.Invoke(Current, notification.Context != changeContext);
            }
            catch (Exception ex) { SettingsStore.Log("audio.read", ex); Bind(); }
        });
        return 0;
    }

    private void ReadCurrent()
    {
        if (volume == null) { Current = new(false, 0, false); return; }
        Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
        Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
        Current = new(true, (int)Math.Round(level * 100), muted);
    }

    public bool SetVolume(double percent)
    {
        if (!double.IsFinite(percent) || percent < 0 || percent > 100) return false;
        return Write(() => volume!.SetMasterVolumeLevelScalar((float)(percent / 100), ref changeContext));
    }

    public bool SetMuted(bool muted) => Write(() => volume!.SetMute(muted, ref changeContext));

    private bool Write(Func<int> operation)
    {
        if (disposed || volume == null || !Current.Available) return false;
        try
        {
            Marshal.ThrowExceptionForHR(operation());
            ReadCurrent();
            Changed?.Invoke(Current, false);
            return true;
        }
        catch (Exception ex) { SettingsStore.Log("audio.control", ex); Bind(); return false; }
    }

    private void Rebind() { if (!disposed) dispatcher.BeginInvoke(Bind); }
    public int OnDefaultDeviceChanged(int flow, int role, string? id) { if (flow == 0 && role == 1) Rebind(); return 0; }
    public int OnDeviceStateChanged(string id, uint state) { Rebind(); return 0; }
    public int OnDeviceAdded(string id) => 0;
    public int OnDeviceRemoved(string id) { Rebind(); return 0; }
    public int OnPropertyValueChanged(string id, PropertyKey key) => 0;

    private void ReleaseEndpoint()
    {
        if (volume != null) { volume.UnregisterControlChangeNotify(this); Marshal.ReleaseComObject(volume); volume = null; }
        if (device != null) { Marshal.ReleaseComObject(device); device = null; }
    }
    public void Dispose()
    {
        disposed = true;
        ReleaseEndpoint();
        if (enumerator != null) { enumerator.UnregisterEndpointNotificationCallback(this); Marshal.ReleaseComObject(enumerator); enumerator = null; }
    }
}

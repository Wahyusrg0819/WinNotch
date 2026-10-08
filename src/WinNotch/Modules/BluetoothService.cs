using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using WinNotch.Core;

namespace WinNotch.Modules;

public sealed record BluetoothRadioItem(string Id, RadioState State);
public sealed record BluetoothDeviceItem(string Id, string Name, bool? Connected, int? Battery, string Container = "")
{
    public string Detail => Connected == true ? "Connected · " + (Battery is >= 0 and <= 100 ? $"Battery {Battery}%" : "Battery unavailable")
        : Connected == false ? "Paired · Not connected" : "Paired · Connection status unavailable";
}
public sealed record BluetoothSnapshot(BluetoothRadioItem[] Radios, BluetoothDeviceItem[] Devices)
{
    public static BluetoothSnapshot Empty { get; } = new(Array.Empty<BluetoothRadioItem>(), Array.Empty<BluetoothDeviceItem>());
}

public sealed class BluetoothService : IDisposable
{
    private readonly Func<CancellationToken, Task<BluetoothSnapshot>> read;
    private readonly Func<Task<RadioAccessStatus>> requestAccess;
    private readonly Func<string, RadioState, CancellationToken, Task<RadioAccessStatus>> setState;
    private readonly DispatcherTimer poll;
    private CancellationTokenSource? reading, changing;
    private bool enabled, visible, disposed;
    private int generation;
    private RadioAccessStatus? access;
    public BluetoothSnapshot Current { get; private set; } = BluetoothSnapshot.Empty;
    public string Status { get; private set; } = "Bluetooth module is off.";
    public string ActionStatus { get; private set; } = "";
    public bool IsReading { get; private set; }
    public bool IsChanging { get; private set; }
    public bool IsOn => Current.Radios.Any(radio => radio.State == RadioState.On);
    public bool CanChange => enabled && !disposed && !IsReading && !IsChanging && Current.Radios.Any(radio => radio.State is RadioState.On or RadioState.Off);
    internal bool IsPolling => poll.IsEnabled;
    public event Action? Changed;

    public BluetoothService(Dispatcher dispatcher) : this(dispatcher, ReadWindowsAsync,
        async () => await Radio.RequestAccessAsync(), SetWindowsStateAsync) { }

    internal BluetoothService(Dispatcher dispatcher, Func<CancellationToken, Task<BluetoothSnapshot>> read,
        Func<Task<RadioAccessStatus>> requestAccess, Func<string, RadioState, CancellationToken, Task<RadioAccessStatus>> setState)
    {
        this.read = read; this.requestAccess = requestAccess; this.setState = setState;
        poll = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(15) };
        poll.Tick += async (_, _) => await RefreshAsync();
    }

    public void SetEnabled(bool value)
    {
        if (disposed || enabled == value) return;
        enabled = value; generation++; reading?.Cancel(); changing?.Cancel(); poll.Stop();
        Current = BluetoothSnapshot.Empty; ActionStatus = "";
        Status = value ? "Open Bluetooth to view paired devices." : "Bluetooth module is off.";
        Changed?.Invoke();
        if (value && visible) { poll.Start(); _ = RefreshAsync(); }
    }

    internal void SetPanelVisible(bool value)
    {
        if (disposed || visible == value) return;
        visible = value;
        if (value && enabled) { poll.Start(); _ = RefreshAsync(); } else poll.Stop();
    }

    public async Task RefreshAsync(bool preserveActionStatus = false)
    {
        if (!enabled || disposed || IsReading || IsChanging) return;
        if (!preserveActionStatus) ActionStatus = "";
        IsReading = true; var version = generation;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        reading = cancellation; Changed?.Invoke();
        try
        {
            var snapshot = await read(cancellation.Token);
            if (disposed || version != generation) return;
            var devices = MergeDevices(snapshot.Devices);
            if (snapshot.Radios.Length > 0 && snapshot.Radios.All(radio => radio.State is RadioState.Off or RadioState.Disabled))
                devices = devices.Select(device => device with { Connected = false, Battery = null }).ToArray();
            Current = snapshot with { Devices = devices.Take(32).ToArray() };
            var radioStatus = snapshot.Radios.Length == 0 ? "No Bluetooth adapter available"
                : snapshot.Radios.All(radio => radio.State == RadioState.Disabled) ? "Bluetooth is blocked by Windows"
                : snapshot.Radios.All(radio => radio.State == RadioState.On) ? "Bluetooth on"
                : snapshot.Radios.All(radio => radio.State == RadioState.Off) ? "Bluetooth off"
                : IsOn ? "Bluetooth partly on" : "Bluetooth state unavailable";
            Status = radioStatus + (devices.Length > 32 ? " · Showing 32 paired devices" : $" · {devices.Length} paired");
        }
        catch (Exception ex)
        {
            if (disposed || version != generation) return;
            Current = BluetoothSnapshot.Empty;
            Status = ex is OperationCanceledException ? "Bluetooth did not respond. Refresh to retry." : "Bluetooth unavailable. Refresh or open Windows settings.";
            if (ex is not OperationCanceledException) SettingsStore.Log("bluetooth.read", ex);
        }
        finally
        {
            reading = null; IsReading = false;
            if (!disposed) Changed?.Invoke();
            if (!disposed && enabled && visible && version != generation) _ = RefreshAsync();
        }
    }

    public async Task ToggleAsync()
    {
        if (!CanChange) return;
        var target = IsOn ? RadioState.Off : RadioState.On;
        var radios = Current.Radios.Where(radio => radio.State is RadioState.On or RadioState.Off).Where(radio => radio.State != target).ToArray();
        var version = generation;
        IsChanging = true; ActionStatus = "Waiting for Windows…"; Changed?.Invoke();
        using var cancellation = new CancellationTokenSource();
        changing = cancellation;
        var accepted = false;
        try
        {
            // Access is requested only from this explicit user action, never by enumeration or startup.
            if (access != RadioAccessStatus.Allowed) access = await requestAccess();
            if (disposed || version != generation) return;
            if (access != RadioAccessStatus.Allowed) { ActionStatus = Denied(access.Value); return; }
            cancellation.CancelAfter(TimeSpan.FromSeconds(12));
            accepted = true;
            foreach (var radio in radios)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (disposed || version != generation) return;
                var result = await setState(radio.Id, target, cancellation.Token);
                if (disposed || version != generation) return;
                if (result != RadioAccessStatus.Allowed) { access = null; accepted = false; ActionStatus = Denied(result); break; }
            }
            if (accepted) ActionStatus = "Request sent; checking the actual state…";
        }
        catch (Exception ex)
        {
            accepted = false;
            if (!disposed && version == generation)
            {
                ActionStatus = "Could not confirm the change. Refresh or use Windows settings.";
                if (ex is not OperationCanceledException) SettingsStore.Log("bluetooth.toggle", ex);
            }
        }
        finally
        {
            changing = null; IsChanging = false;
            if (!disposed && version == generation)
            {
                await RefreshAsync(true);
                if (!disposed && version == generation && accepted)
                    ActionStatus = radios.All(radio => Current.Radios.Any(actual => actual.Id == radio.Id && actual.State == target))
                        ? $"Bluetooth turned {(target == RadioState.On ? "on" : "off")}."
                        : "Windows accepted the request but has not confirmed the change. Refresh to check.";
                if (!disposed && version == generation) Changed?.Invoke();
            }
            else if (!disposed && enabled && visible) _ = RefreshAsync();
        }
    }

    private static string Denied(RadioAccessStatus status) => status == RadioAccessStatus.DeniedByUser
        ? "Access was denied. Allow radio control in Windows, then try again."
        : "Windows or this hardware blocked the change. Use Windows Bluetooth settings.";

    internal static BluetoothDeviceItem[] MergeDevices(IEnumerable<BluetoothDeviceItem> devices) => devices
        .GroupBy(device => string.IsNullOrEmpty(device.Container) ? device.Id : device.Container, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var first = group.OrderByDescending(device => device.Connected == true).First();
            var connected = group.Any(device => device.Connected == true) ? true : group.All(device => device.Connected == false) ? false : (bool?)null;
            var battery = connected == true ? group.Where(device => device.Connected == true && device.Battery is >= 0 and <= 100).Select(device => device.Battery).FirstOrDefault() : null;
            var name = new string(first.Name.Where(character => !char.IsControl(character)).Take(120).ToArray()).Trim();
            return first with { Name = name.Length == 0 ? "Bluetooth device" : name, Connected = connected, Battery = battery };
        }).OrderByDescending(device => device.Connected == true).ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    internal static async Task<BluetoothSnapshot> ReadWindowsAsync(CancellationToken token)
    {
        var radios = new List<BluetoothRadioItem>();
        var interfaces = await DeviceInformation.FindAllAsync(Radio.GetDeviceSelector()).AsTask(token);
        foreach (var device in interfaces)
        {
            var radio = await Radio.FromIdAsync(device.Id).AsTask(token);
            if (radio?.Kind == RadioKind.Bluetooth) radios.Add(new(device.Id, radio.State));
        }
        var query = $"({BluetoothDevice.GetDeviceSelectorFromPairingState(true)}) OR ({BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)})";
        const string connectedKey = "System.Devices.Aep.IsConnected", containerKey = "System.Devices.Aep.ContainerId", batteryKey = "System.Devices.BatteryLife";
        var paired = await DeviceInformation.FindAllAsync(query, new[] { connectedKey, containerKey, batteryKey }, DeviceInformationKind.AssociationEndpoint).AsTask(token);
        var devices = new List<BluetoothDeviceItem>();
        var batteries = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        // Only metadata is read: no active scanning, GATT connection, pairing, or device wake request.
        foreach (var device in paired)
        {
            token.ThrowIfCancellationRequested();
            var connected = device.Properties.TryGetValue(connectedKey, out var value) && value is bool boolean ? boolean : (bool?)null;
            var container = device.Properties.TryGetValue(containerKey, out value) && Guid.TryParse(value?.ToString(), out var id) && id != Guid.Empty ? id.ToString("B") : "";
            int? battery = device.Properties.TryGetValue(batteryKey, out value) && value is byte level && level <= 100 ? level : null;
            if (connected == true && battery == null && container.Length > 0)
            {
                if (!batteries.TryGetValue(container, out battery))
                {
                    try
                    {
                        var info = await DeviceInformation.CreateFromIdAsync(container, new[] { batteryKey }, DeviceInformationKind.DeviceContainer).AsTask(token);
                        if (info?.Properties.TryGetValue(batteryKey, out value) == true && value is byte percent && percent <= 100) battery = percent;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { /* Optional driver property: missing means unavailable, never zero. */ }
                    batteries[container] = battery;
                }
            }
            devices.Add(new(device.Id, device.Name, connected, battery, container));
        }
        return new(radios.ToArray(), devices.ToArray());
    }

    private static async Task<RadioAccessStatus> SetWindowsStateAsync(string id, RadioState target, CancellationToken token)
    {
        var radio = await Radio.FromIdAsync(id).AsTask(token);
        if (radio?.Kind != RadioKind.Bluetooth) return RadioAccessStatus.DeniedBySystem;
        return await radio.SetStateAsync(target).AsTask(token);
    }

    public void Dispose() { disposed = true; generation++; poll.Stop(); reading?.Cancel(); changing?.Cancel(); Current = BluetoothSnapshot.Empty; }
}

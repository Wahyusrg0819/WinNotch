using System;
using System.Windows.Threading;
using Windows.System.Power;
using WinNotch.Core;

namespace WinNotch.Modules;

public sealed record BatterySnapshot(bool Present, int Percent, bool PluggedIn, bool Charging, bool Saver);

public sealed class BatteryService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private bool disposed;
    public BatterySnapshot Current { get; private set; } = new(false, 0, false, false, false);
    public event Action<BatterySnapshot, BatterySnapshot>? Changed;

    public BatteryService(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        try
        {
            Current = Read();
            PowerManager.BatteryStatusChanged += OnPower;
            PowerManager.PowerSupplyStatusChanged += OnPower;
            PowerManager.RemainingChargePercentChanged += OnPower;
            PowerManager.EnergySaverStatusChanged += OnPower;
        }
        catch (Exception ex) { SettingsStore.Log("battery.initialize", ex); }
    }

    private static BatterySnapshot Read() => new(PowerManager.BatteryStatus != BatteryStatus.NotPresent,
        PowerManager.RemainingChargePercent, PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent,
        PowerManager.BatteryStatus == BatteryStatus.Charging, PowerManager.EnergySaverStatus == EnergySaverStatus.On);

    private void OnPower(object? sender, object args) => dispatcher.BeginInvoke(() =>
    {
        if (disposed) return;
        try { var previous = Current; Current = Read(); if (Current != previous) Changed?.Invoke(previous, Current); }
        catch (Exception ex) { SettingsStore.Log("battery.read", ex); }
    });

    public void Dispose()
    {
        disposed = true;
        PowerManager.BatteryStatusChanged -= OnPower;
        PowerManager.PowerSupplyStatusChanged -= OnPower;
        PowerManager.RemainingChargePercentChanged -= OnPower;
        PowerManager.EnergySaverStatusChanged -= OnPower;
    }
}

using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinNotch.Core;

namespace WinNotch.Modules;

public sealed record BrightnessSnapshot(bool Available, int Percent);

public sealed class BrightnessService : IDisposable
{
    private readonly DispatcherTimer poll;
    private bool enabled, busy, disposed;
    private int generation;
    private int? pending;
    public BrightnessSnapshot Current { get; private set; } = new(false, 0);
    public event Action<BrightnessSnapshot, bool>? Changed;

    public BrightnessService(Dispatcher dispatcher)
    {
        poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await RefreshAsync(), dispatcher);
        poll.Stop();
    }

    public void SetEnabled(bool value)
    {
        if (disposed || enabled == value) return;
        enabled = value; generation++; pending = null; poll.Stop();
        if (value) { poll.Start(); _ = RefreshAsync(); }
        else { Current = new(false, 0); Changed?.Invoke(Current, false); }
    }

    public void SetBrightness(double value)
    {
        if (!enabled || !Current.Available || !double.IsFinite(value)) return;
        pending = (int)Math.Round(Math.Clamp(value, 0, 100));
        _ = RefreshAsync();
    }

    internal async Task RefreshAsync()
    {
        if (!enabled || busy || disposed) return;
        busy = true;
        var version = generation;
        var requested = pending; pending = null;
        try
        {
            var result = await Task.Run(() => ReadOrSet(requested));
            if (disposed || version != generation) return;
            var external = !requested.HasValue && Current.Available && result.Available && Current.Percent != result.Percent;
            Current = result;
            poll.Interval = TimeSpan.FromSeconds(result.Available ? 2 : 30);
            Changed?.Invoke(Current, external);
        }
        catch (Exception ex)
        {
            SettingsStore.Log("brightness.wmi", ex);
            if (!disposed && version == generation) { Current = new(false, 0); poll.Interval = TimeSpan.FromSeconds(30); Changed?.Invoke(Current, false); }
        }
        finally
        {
            busy = false;
            if (!disposed && enabled && (pending.HasValue || version != generation)) _ = RefreshAsync();
        }
    }

    private static BrightnessSnapshot ReadOrSet(int? value)
    {
        using var search = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE");
        using var screens = search.Get();
        foreach (ManagementObject screen in screens)
        {
            using (screen)
            {
                var instance = (string)screen["InstanceName"];
                if (value.HasValue)
                {
                    // Select a supported brightness step, then address the same internal display.
                    var levels = screen["Level"] as byte[];
                    var level = levels?.Length > 0 ? levels.OrderBy(n => Math.Abs(n - value.Value)).First() : (byte)value.Value;
                    using var methodsSearch = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE");
                    using var methods = methodsSearch.Get();
                    foreach (ManagementObject method in methods)
                    {
                        using (method)
                        {
                            if (!string.Equals(instance, method["InstanceName"] as string, StringComparison.OrdinalIgnoreCase)) continue;
                            var result = Convert.ToUInt32(method.InvokeMethod("WmiSetBrightness", new object[] { 0u, level }));
                            if (result != 0) throw new InvalidOperationException("Brightness write failed.");
                            screen.Get();
                            return new(true, Convert.ToInt32(screen["CurrentBrightness"]));
                        }
                    }
                    return new(false, 0);
                }
                return new(true, Convert.ToInt32(screen["CurrentBrightness"]));
            }
        }
        return new(false, 0);
    }

    public void Dispose() { disposed = true; generation++; pending = null; poll.Stop(); }
}

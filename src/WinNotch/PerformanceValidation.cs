using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using WinNotch.Core;

namespace WinNotch;

internal static class PerformanceValidation
{
    // Opt-in local benchmark: no screenshots, forced GC, working-set trimming, saved settings, or hardware writes.
    internal static async Task RunAsync(App app, string output)
    {
        Directory.CreateDirectory(output);
        app.ApplySettings(app.Settings with { Visibility = VisibilityMode.Always, Fullscreen = FullscreenMode.Show, ExcludedApps = Array.Empty<string>() });
        var report = new Dictionary<string, object>();
        var transitions = new List<object>();
        var layouts = 0; var stateChanges = 0;
        app.Overlay.LayoutUpdated += (_, _) => layouts++;
        app.State.Changed += _ => stateChanges++;
        using var process = Process.GetCurrentProcess();
        async Task Sample(string phase)
        {
            var memory = new List<double>(); var committed = new List<double>();
            process.Refresh(); var cpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
            var allocated = GC.GetTotalAllocatedBytes();
            var previousLayouts = layouts; var previousStates = stateChanges;
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(500); process.Refresh();
                memory.Add(process.WorkingSet64 / 1048576d); committed.Add(process.PrivateMemorySize64 / 1048576d);
            }
            report[phase] = new { MeanWorkingSetMiB = memory.Average(), PeakWorkingSetMiB = memory.Max(), PrivateBytesMiB = committed.Average(),
                ManagedBytesMiB = GC.GetTotalMemory(false) / 1048576d, AllocatedBytesPerSecond = (GC.GetTotalAllocatedBytes() - allocated) / clock.Elapsed.TotalSeconds,
                CpuPercent = (process.TotalProcessorTime - cpu).TotalSeconds / clock.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
                Handles = process.HandleCount, Gen2Collections = GC.CollectionCount(2), Layouts = layouts - previousLayouts, StateChanges = stateChanges - previousStates };
        }
        await Task.Delay(8000);
        await Sample("initialIdle");
        var watch = Stopwatch.StartNew();
        app.Overlay.Expand(false); app.Overlay.UpdateLayout();
        report["firstExpandMilliseconds"] = watch.Elapsed.TotalMilliseconds;
        var panel = app.Overlay.ExpandedContent!;
        await Task.Delay(400);
        await Sample("firstPanel");
        var panels = new List<string> { "media" };
        if (app.Settings.Timer) panels.Add("timer");
        if (app.Settings.Brightness) panels.Add("brightness");
        if (app.Settings.Notifications) panels.Add("notification");
        if (app.Settings.Calendar) panels.Add("calendar");
        if (app.Settings.Downloads) panels.Add("downloads");
        if (app.Settings.Bluetooth) panels.Add("bluetooth");
        for (var round = 0; round < 6; round++)
        {
            app.Overlay.Expand(false);
            foreach (var name in panels)
            {
                watch.Restart(); panel.SelectPanel(name); panel.UpdateLayout();
                transitions.Add(new { Round = round, Panel = name, Milliseconds = watch.Elapsed.TotalMilliseconds });
                await Task.Delay(80);
            }
            app.State.Collapse(); await Task.Delay(250);
        }
        var settings = new SettingsWindow(app); settings.Show();
        await Task.Delay(200); settings.Close();
        settings = null!; // Do not retain a closed window across the remaining benchmark awaits.
        app.State.Collapse(); await Task.Delay(1000);
        await Sample("afterUse");
        // Revisit the same panels to distinguish initial loading from repeated interaction growth.
        for (var round = 0; round < 12; round++)
        {
            app.Overlay.Expand(false);
            foreach (var name in panels) { panel.SelectPanel(name); panel.UpdateLayout(); await Task.Delay(40); }
            app.State.Collapse(); await Task.Delay(100);
        }
        await Task.Delay(1000); await Sample("afterRepeatedUse");
        report["panelTransitions"] = transitions;
        report["mediaPresent"] = app.Media.Current != null;
        report["bluetoothPollingStopped"] = !app.Bluetooth.IsPolling;
        report["version"] = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
        report["processId"] = Environment.ProcessId;
        await File.WriteAllTextAsync(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (Environment.GetCommandLineArgs().Contains("--stability-test")) await RunStabilityAsync(app, output, panels);
        // Keep this diagnostic instance alive for an external memory map/dump after measurements finish.
        if (Environment.GetCommandLineArgs().Contains("--memory-profile")) await Task.Delay(45000);
    }

    private static async Task RunStabilityAsync(App app, string output, List<string> panels)
    {
        var path = Path.Combine(output, "stability.csv");
        await File.WriteAllTextAsync(path, "Seconds,Phase,WorkingSetMiB,PrivateMiB,ManagedMiB,GcCommittedMiB,Handles,Threads,Gen0,Gen1,Gen2,CpuPercent,ClosedSettingsAlive\n");
        using var process = Process.GetCurrentProcess();
        var closedWindows = new List<WeakReference>();
        var clock = Stopwatch.StartNew();
        var cycles = 0; var samples = 0;
        var previousTime = 0d; var maxGap = 0d;
        var previousCpu = process.TotalProcessorTime.TotalSeconds;
        var initialGen2 = GC.CollectionCount(2);
        while (clock.Elapsed.TotalMinutes < 15)
        {
            if (cycles < 10 && clock.Elapsed.TotalSeconds >= cycles * 30)
            {
                app.State.Expand(); // Exercise the panel tree without repeatedly activating the test window.
                foreach (var name in panels) { app.Overlay.ExpandedContent!.SelectPanel(name); await Task.Delay(80); }
                app.State.Collapse();
                var settings = new SettingsWindow(app) { ShowActivated = false };
                settings.Show(); await Task.Delay(200); settings.Close();
                closedWindows.Add(new WeakReference(settings));
                settings = null!;
                cycles++;
            }
            await Task.Delay(5000);
            process.Refresh();
            var elapsed = clock.Elapsed.TotalSeconds;
            var gap = elapsed - previousTime;
            maxGap = Math.Max(maxGap, gap);
            var cpu = process.TotalProcessorTime.TotalSeconds;
            var phase = elapsed < 300 ? "interaction" : "idle";
            await File.AppendAllTextAsync(path, FormattableString.Invariant($"{elapsed:F2},{phase},{process.WorkingSet64 / 1048576d:F3},{process.PrivateMemorySize64 / 1048576d:F3},{GC.GetTotalMemory(false) / 1048576d:F3},{GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576d:F3},{process.HandleCount},{process.Threads.Count},{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)},{(cpu - previousCpu) / gap / Environment.ProcessorCount * 100:F3},{closedWindows.Count(window => window.IsAlive)}\n"));
            samples++; previousTime = elapsed; previousCpu = cpu;
        }
        var result = new { ElapsedSeconds = clock.Elapsed.TotalSeconds, Samples = samples, InteractionCycles = cycles,
            MaxSampleGapSeconds = maxGap, ClosedSettingsAlive = closedWindows.Count(window => window.IsAlive),
            Gen2Collections = GC.CollectionCount(2) - initialGen2, BluetoothPollingStopped = !app.Bluetooth.IsPolling,
            LoadedPanels = app.Overlay.ExpandedContent!.LoadedPanelCount };
        await File.WriteAllTextAsync(Path.Combine(output, "stability-result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        if (cycles != 10 || samples < 170 || maxGap > 15 || app.Bluetooth.IsPolling)
            throw new InvalidOperationException("Stability run was incomplete or interrupted; inspect its report.");
    }
}

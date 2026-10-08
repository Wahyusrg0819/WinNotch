using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Windows.Devices.Radios;
using WinNotch.Core;
using WinNotch.Modules;

namespace WinNotch;

internal static class BluetoothValidation
{
    // Uses synthetic devices and radio delegates only: no access prompt, pairing, or hardware writes.
    internal static async Task CheckAsync(App app, Dictionary<string, object> checks, string output)
    {
        var saved = app.Settings;
        var original = app.Bluetooth;
        var reads = 0; var requests = 0; var writes = 0;
        var radio = RadioState.On;
        var permission = RadioAccessStatus.Allowed;
        var result = RadioAccessStatus.Allowed;
        var applyWrite = true;
        var devices = new[] {
            new BluetoothDeviceItem("headphones-classic", "Studio headphones", true, null, "headphones"),
            new BluetoothDeviceItem("headphones-le", "Studio headphones", true, 76, "headphones"),
            new BluetoothDeviceItem("keyboard", "Desktop keyboard", true, null),
            new BluetoothDeviceItem("mouse", "Travel mouse", false, 83) };
        using var service = new BluetoothService(app.Dispatcher,
            _ => { reads++; return Task.FromResult(new BluetoothSnapshot(new[] { new BluetoothRadioItem("bluetooth-test-only", radio) }, devices)); },
            () => { requests++; return Task.FromResult(permission); },
            (id, state, _) => { writes++; if (id != "bluetooth-test-only") throw new InvalidOperationException(); if (applyWrite && result == RadioAccessStatus.Allowed) radio = state; return Task.FromResult(result); });
        app.Bluetooth = service;
        service.Changed += app.Overlay.UpdateBluetooth;
        try
        {
            app.ApplySettings(saved with { Bluetooth = true, Timer = true, Brightness = true, Notifications = true, Calendar = true, Volume = true });
            checks["controls.bluetoothIdleDoesNotReadOrPrompt"] = reads == 0 && requests == 0 && !service.IsPolling;
            app.Overlay.Expand();
            var panel = app.Overlay.ExpandedContent!;
            T Find<T>(string name) where T : FrameworkElement => (T)panel.FindName(name);
            void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            panel.SelectPanel("bluetooth"); panel.FocusPanel();
            await Task.Delay(300);
            checks["controls.bluetoothPanelReadsWithoutPrompt"] = reads == 1 && requests == 0 && service.IsPolling && Find<Grid>("BluetoothPanel").IsVisible;
            checks["controls.bluetoothDevicesDeduplicated"] = service.Current.Devices.Length == 3 && service.Current.Devices.Single(device => device.Container == "headphones").Battery == 76;
            checks["controls.bluetoothMissingBatteryHonest"] = service.Current.Devices.Single(device => device.Id == "keyboard").Detail.Contains("unavailable") && service.Current.Devices.Single(device => device.Id == "mouse").Battery == null;
            checks["controls.bluetoothKeyboardFocus"] = Find<Button>("ToggleBluetoothButton").IsKeyboardFocused;
            SmokeTest.Capture(app.Overlay, Path.Combine(output, "bluetooth-panel.png"));
            Click("ToggleBluetoothButton");
            checks["controls.bluetoothTurnOffViaButton"] = requests == 1 && writes == 1 && radio == RadioState.Off && service.ActionStatus == "Bluetooth turned off." && service.Current.Devices.All(device => device.Connected == false && device.Battery == null);
            Click("ToggleBluetoothButton");
            checks["controls.bluetoothTurnOnCachesAllowedAccess"] = requests == 1 && writes == 2 && radio == RadioState.On && service.ActionStatus == "Bluetooth turned on.";
            applyWrite = false; await service.ToggleAsync();
            checks["controls.bluetoothAcceptedIsNotConfirmed"] = radio == RadioState.On && service.ActionStatus.Contains("has not confirmed");
            result = RadioAccessStatus.DeniedBySystem; await service.ToggleAsync();
            checks["controls.bluetoothSystemDenialShown"] = radio == RadioState.On && service.ActionStatus.Contains("blocked");
            permission = RadioAccessStatus.DeniedByUser;
            var beforeWrites = writes; await service.ToggleAsync();
            checks["controls.bluetoothUserDenialDoesNotWrite"] = requests == 2 && writes == beforeWrites && service.ActionStatus.Contains("denied");
            await Task.Delay(100); SmokeTest.Capture(app.Overlay, Path.Combine(output, "bluetooth-denied.png"));
            permission = result = RadioAccessStatus.Allowed; applyWrite = true; await service.ToggleAsync();
            checks["controls.bluetoothRetryAfterPermissionDenial"] = radio == RadioState.Off && requests == 3 && service.ActionStatus == "Bluetooth turned off.";
            var beforeReads = reads; Click("RefreshBluetoothButton");
            checks["controls.bluetoothRefreshDoesNotPrompt"] = reads == beforeReads + 1 && requests == 3 && service.ActionStatus.Length == 0;
            panel.SelectPanel("media");
            checks["controls.bluetoothOtherPanelStopsPolling"] = !service.IsPolling;
            panel.SelectPanel("bluetooth"); app.State.Collapse();
            checks["controls.bluetoothCollapseStopsPolling"] = !service.IsPolling;
            app.Overlay.Expand(); panel.SelectPanel("bluetooth"); app.TogglePause();
            checks["controls.bluetoothHiddenStopsPolling"] = !service.IsPolling && app.State.Current.State == NotchState.Hidden;
            app.TogglePause(); app.Overlay.Expand(); panel.SelectPanel("bluetooth");
            radio = RadioState.Disabled; devices = Array.Empty<BluetoothDeviceItem>(); await service.RefreshAsync();
            checks["controls.bluetoothBlockedRadioDisabled"] = !Find<Button>("ToggleBluetoothButton").IsEnabled && service.Status.Contains("blocked") && Find<TextBlock>("BluetoothEmpty").IsVisible;
            await Task.Delay(100); SmokeTest.Capture(app.Overlay, Path.Combine(output, "bluetooth-blocked.png"));
            app.ApplySettings(app.Settings with { Bluetooth = false });
            checks["controls.bluetoothModuleDisableLeavesRadioAlone"] = !service.IsPolling && service.Current.Devices.Length == 0 && !Find<Button>("BluetoothTab").IsVisible && radio == RadioState.Disabled && writes == beforeWrites + 1;
            var preferences = JsonSerializer.Deserialize(JsonSerializer.Serialize(app.Settings, SettingsJsonContext.Default.NotchSettings), SettingsJsonContext.Default.NotchSettings);
            checks["controls.bluetoothPreferenceRoundTrip"] = preferences?.Bluetooth == false;
        }
        finally
        {
            app.State.Collapse(); if (app.State.Paused) app.TogglePause();
            service.Changed -= app.Overlay.UpdateBluetooth; service.Dispose();
            app.Bluetooth = original; app.ApplySettings(saved);
        }

        var merged = BluetoothService.MergeDevices(new[] { new BluetoothDeviceItem("one", "Same name", true, 0), new BluetoothDeviceItem("two", "Same name", true, 255), new BluetoothDeviceItem("three", "\n\t", null, 80) });
        checks["controls.bluetoothBatteryZeroAndUnknownNotConfused"] = merged.Length == 3 && merged.Single(device => device.Id == "one").Detail.Contains("0%") && merged.Single(device => device.Id == "two").Battery == null && merged.Single(device => device.Id == "three").Name == "Bluetooth device";
        using var noAdapter = new BluetoothService(app.Dispatcher, _ => Task.FromResult(BluetoothSnapshot.Empty), () => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException());
        noAdapter.SetEnabled(true); await noAdapter.RefreshAsync(); await noAdapter.ToggleAsync();
        checks["controls.bluetoothNoAdapterCannotToggle"] = !noAdapter.CanChange && noAdapter.Status.Contains("No Bluetooth adapter");
        var pendingRead = new TaskCompletionSource<BluetoothSnapshot>();
        using var pending = new BluetoothService(app.Dispatcher, _ => pendingRead.Task, () => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException());
        pending.SetEnabled(true); var reading = pending.RefreshAsync(); pending.SetEnabled(false);
        pendingRead.SetResult(new(new[] { new BluetoothRadioItem("test", RadioState.On) }, merged)); await reading;
        checks["controls.bluetoothDisableIgnoresPendingRead"] = pending.Current.Radios.Length == 0 && !pending.IsReading;
        var pendingAccess = new TaskCompletionSource<RadioAccessStatus>();
        var pendingWrites = 0; var accessRequests = 0;
        using var delayed = new BluetoothService(app.Dispatcher, _ => Task.FromResult(new BluetoothSnapshot(new[] { new BluetoothRadioItem("test", RadioState.On) }, merged)),
            () => { accessRequests++; return pendingAccess.Task; }, (_, _, _) => { pendingWrites++; return Task.FromResult(RadioAccessStatus.Allowed); });
        delayed.SetEnabled(true); await delayed.RefreshAsync();
        var changing = delayed.ToggleAsync(); await delayed.ToggleAsync();
        checks["controls.bluetoothDuplicateToggleBlocked"] = !delayed.CanChange && accessRequests == 1;
        delayed.Dispose(); pendingAccess.SetResult(RadioAccessStatus.Allowed); await changing;
        checks["controls.bluetoothDisposeDuringPermissionPreventsWrite"] = pendingWrites == 0 && delayed.Current.Radios.Length == 0;
    }

    // Explicit read-only diagnostic; no names or device identifiers are written to disk.
    internal static async Task<bool> ReadLiveAsync(string output)
    {
        Directory.CreateDirectory(output);
        var report = new Dictionary<string, object> { ["readOnly"] = true };
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var snapshot = await BluetoothService.ReadWindowsAsync(cancellation.Token);
            var devices = BluetoothService.MergeDevices(snapshot.Devices);
            report["radioStates"] = snapshot.Radios.Select(radio => radio.State.ToString()).ToArray();
            report["pairedDevices"] = devices.Length;
            report["connectedDevices"] = devices.Count(device => device.Connected == true);
            report["batteryAvailable"] = devices.Count(device => device.Battery != null);
            report["passed"] = true;
        }
        catch (Exception ex) { report["passed"] = false; report["error"] = ex.GetType().Name; report["hresult"] = $"0x{ex.HResult:X8}"; }
        await File.WriteAllTextAsync(Path.Combine(output, "bluetooth-read.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return report["passed"] is true;
    }
}

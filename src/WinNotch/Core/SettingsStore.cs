using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WinNotch.Core;

public sealed record NotchSettings
{
    public bool Media { get; init; } = true;
    public bool Battery { get; init; } = true;
    public bool Volume { get; init; } = true;
    public bool Animations { get; init; } = true;
    public bool HideInFullscreen { get; init; } = true;
    public bool AlwaysOnTop { get; init; } = true;
    public bool Floating { get; init; }
    public double Scale { get; init; } = 1;
    public VisibilityMode Visibility { get; init; } = VisibilityMode.SmartHide;
}

public static class SettingsStore
{
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinNotch");
    private static string FilePath => Path.Combine(DataDirectory, "settings.json");
    public static NotchSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<NotchSettings>(File.ReadAllText(FilePath)) ?? new();
            return settings with { Scale = settings.Scale is >= 0.85 and <= 1.2 ? settings.Scale : 1,
                Visibility = Enum.IsDefined(settings.Visibility) ? settings.Visibility : VisibilityMode.SmartHide };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public static void Save(NotchSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("WinNotch") is string;
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("WinNotch", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("WinNotch", false);
    }

    public static void Log(string module, Exception error)
    {
        // Do not persist exception messages, media metadata, or notification content.
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var path = Path.Combine(DataDirectory, "diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 128_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {module} {error.GetType().Name} 0x{error.HResult:X8}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

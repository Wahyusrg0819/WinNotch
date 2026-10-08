using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WinNotch.Core;

public sealed record NotchSettings
{
    public bool Media { get; init; } = true;
    public bool Battery { get; init; } = true;
    public bool Volume { get; init; } = true;
    public bool Timer { get; init; } = true;
    public bool Notifications { get; init; }
    public bool Brightness { get; init; } = true;
    public bool Clock { get; init; }
    public bool Calendar { get; init; } = true;
    public bool Downloads { get; init; } = true;
    public bool Bluetooth { get; init; } = true;
    public string DownloadsFolder { get; init; } = "";
    public AccentTheme Theme { get; init; } = AccentTheme.Leaf;
    public bool TrueBlack { get; init; }
    public double AnimationSpeed { get; init; } = 1;
    public FullscreenMode? Fullscreen { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public FullscreenMode EffectiveFullscreen => Fullscreen ?? (HideInFullscreen ? FullscreenMode.Hide : FullscreenMode.Show);
    public bool Animations { get; init; } = true;
    public bool HideInFullscreen { get; init; } = true;
    public bool AlwaysOnTop { get; init; } = true;
    public bool Floating { get; init; }
    public double Scale { get; init; } = 1;
    public VisibilityMode Visibility { get; init; } = VisibilityMode.SmartHide;
    public DisplayMode Display { get; init; } = DisplayMode.Primary;
    public string SelectedDisplay { get; init; } = "";
    public string[] ExcludedApps { get; init; } = Array.Empty<string>();
}

public static class SettingsStore
{
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinNotch");
    private static string FilePath => Path.Combine(DataDirectory, "settings.json");
    public static NotchSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.NotchSettings) ?? new();
            return settings with { Scale = settings.Scale is >= 0.85 and <= 1.2 ? settings.Scale : 1,
                Visibility = Enum.IsDefined(settings.Visibility) ? settings.Visibility : VisibilityMode.SmartHide,
                Display = Enum.IsDefined(settings.Display) ? settings.Display : DisplayMode.Primary,
                Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : AccentTheme.Leaf,
                AnimationSpeed = settings.AnimationSpeed is >= 0.5 and <= 2 ? settings.AnimationSpeed : 1,
                Fullscreen = settings.Fullscreen.HasValue && Enum.IsDefined(settings.Fullscreen.Value) ? settings.Fullscreen : null,
                DownloadsFolder = settings.DownloadsFolder ?? "",
                SelectedDisplay = settings.SelectedDisplay ?? "", ExcludedApps = DesktopPolicy.NormalizeApps(settings.ExcludedApps) };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public static void Save(NotchSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(settings, SettingsJsonContext.Default.NotchSettings));
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

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(NotchSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext { }

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinNotch.Core;

public enum DisplayMode { Primary, FollowActive, Selected }

public static class DesktopPolicy
{
    public static string SelectDisplay(DisplayMode mode, string? selected, string? active, IReadOnlyList<string> available, string primary)
    {
        var requested = mode == DisplayMode.FollowActive ? active : mode == DisplayMode.Selected ? selected : primary;
        return available.FirstOrDefault(name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase)) ?? primary;
    }

    public static string[] NormalizeApps(IEnumerable<string>? apps) => (apps ?? Array.Empty<string>())
        .Select(value => Path.GetFileName((value ?? "").Trim().Trim('"')).ToLowerInvariant())
        .Where(value => value.Length > 4 && value.EndsWith(".exe", StringComparison.Ordinal) &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && value is not "winnotch.exe" and not "applicationframehost.exe")
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsExcluded(string? app, IEnumerable<string>? exclusions) => !string.IsNullOrEmpty(app) &&
        (exclusions?.Contains(app, StringComparer.OrdinalIgnoreCase) == true);
}

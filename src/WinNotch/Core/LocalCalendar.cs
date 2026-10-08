using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinNotch.Core;

public sealed record CalendarEntry(Guid Id, string Title, DateTimeOffset StartsAt, bool Reminder, bool Reminded = false)
{
    [JsonIgnore] public string When => StartsAt.ToLocalTime().ToString("ddd, dd MMM yyyy · HH:mm");
}

// One local file, owned by the app's single UI thread. No account or background worker.
public sealed class LocalCalendar
{
    private readonly string path;
    private readonly Func<DateTimeOffset> now;
    private CalendarEntry[] entries = Array.Empty<CalendarEntry>();
    public IReadOnlyList<CalendarEntry> Entries => Array.AsReadOnly(entries);
    public bool LoadFailed { get; }
    public CalendarEntry? Next => entries.FirstOrDefault(entry => entry.StartsAt > now());

    public LocalCalendar(string path, Func<DateTimeOffset>? clock = null)
    {
        this.path = path;
        now = clock ?? (() => DateTimeOffset.Now);
        try
        {
            if (new FileInfo(path).Length > 1_000_000) throw new JsonException();
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), CalendarJsonContext.Default.CalendarEntryArray);
            if (loaded == null || loaded.Length > 1000 || loaded.Any(entry => entry == null || entry.Id == Guid.Empty || !ValidTitle(entry.Title) || entry.StartsAt == default) ||
                loaded.Select(entry => entry.Id).Distinct().Count() != loaded.Length) throw new JsonException();
            entries = loaded.OrderBy(entry => entry.StartsAt).ToArray();
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { LoadFailed = true; }
    }

    private static bool ValidTitle(string? title) => !string.IsNullOrWhiteSpace(title) && title.Length <= 120 && !title.Any(char.IsControl);

    public CalendarEntry Save(Guid? id, string title, DateTimeOffset start, bool reminder)
    {
        title = title.Trim();
        if (!ValidTitle(title)) throw new ArgumentException("Enter a title of 1–120 characters on one line.");
        if (start <= now()) throw new ArgumentException("Choose a future date and time.");
        var previous = id.HasValue ? entries.SingleOrDefault(entry => entry.Id == id) ?? throw new ArgumentException("This event no longer exists.") : null;
        if (previous == null && entries.Length >= 1000) throw new ArgumentException("The agenda is full. Delete old events before adding another.");
        var entry = new CalendarEntry(previous?.Id ?? Guid.NewGuid(), title, start, reminder,
            previous?.Reminded == true && previous.StartsAt == start && previous.Reminder == reminder);
        Write(entries.Where(item => item.Id != entry.Id).Append(entry).OrderBy(item => item.StartsAt).ToArray());
        return entry;
    }

    public void Delete(Guid id) => Write(entries.Where(entry => entry.Id != id).ToArray());

    public CalendarEntry[] TakeDueReminders()
    {
        if (LoadFailed) return Array.Empty<CalendarEntry>();
        var time = now();
        // Missed events stay in the agenda but never replay after sleep or a late launch.
        var due = entries.Where(entry => entry.Reminder && !entry.Reminded && entry.StartsAt > time && entry.StartsAt - time <= TimeSpan.FromMinutes(5)).ToArray();
        if (due.Length == 0) return due;
        var ids = due.Select(entry => entry.Id).ToHashSet();
        // Persist before emitting so a restart does not repeat an already delivered reminder.
        Write(entries.Select(entry => ids.Contains(entry.Id) ? entry with { Reminded = true } : entry).ToArray());
        return due;
    }

    private void Write(CalendarEntry[] updated)
    {
        if (LoadFailed) throw new IOException("The existing agenda could not be read and has been preserved.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(updated, CalendarJsonContext.Default.CalendarEntryArray));
        File.Move(path + ".tmp", path, true);
        entries = updated;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CalendarEntry[]))]
internal partial class CalendarJsonContext : JsonSerializerContext { }

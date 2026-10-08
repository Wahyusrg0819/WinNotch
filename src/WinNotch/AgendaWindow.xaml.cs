using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WinNotch.Core;
using WinNotch.Native;

namespace WinNotch;

public partial class AgendaWindow : Window
{
    private readonly App host;
    private Guid? editing;
    internal AgendaWindow(App host)
    {
        this.host = host;
        InitializeComponent();
        SourceInitialized += (_, _) => { var dark = 1; Win32.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        RefreshEntries(); ResetEditor();
        SaveEventButton.IsEnabled = !host.Calendar.LoadFailed;
        if (host.Calendar.LoadFailed) AgendaStatus.Text = "Could not read agenda.json. The file is preserved and editing is disabled. Restore the file, then restart WinNotch.";
        else if (!host.Settings.Calendar) AgendaStatus.Text = "Calendar is off in Settings. Events can be saved, but reminders are off.";
    }
    private void RefreshEntries()
    {
        // Keep past events available for editing/deletion, after upcoming events.
        var now = DateTimeOffset.Now;
        EventsList.ItemsSource = host.Calendar.Entries.OrderBy(entry => entry.StartsAt <= now).ThenBy(entry => entry.StartsAt).ToArray();
        EmptyMessage.Visibility = host.Calendar.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ResetEditor()
    {
        editing = null; EventsList.SelectedItem = null;
        var next = DateTime.Now.AddHours(1);
        EventTitle.Text = ""; EventDate.SelectedDate = next.Date; EventTime.Text = next.ToString("HH:mm", CultureInfo.InvariantCulture);
        ReminderCheck.IsChecked = true; EditorHeading.Text = "New event"; SaveEventButton.Content = "Add event"; DeleteEventButton.IsEnabled = false;
    }
    private void OnNew(object sender, RoutedEventArgs e) { ResetEditor(); EventTitle.Focus(); }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventsList.SelectedItem is not CalendarEntry entry) return;
        editing = entry.Id; EventTitle.Text = entry.Title; EventDate.SelectedDate = entry.StartsAt.LocalDateTime.Date;
        EventTime.Text = entry.StartsAt.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture); ReminderCheck.IsChecked = entry.Reminder;
        EditorHeading.Text = "Edit event"; SaveEventButton.Content = "Save event"; DeleteEventButton.IsEnabled = !host.Calendar.LoadFailed;
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (EventDate.SelectedDate is not DateTime date || !DateTime.TryParse(EventDate.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var typedDate) || typedDate.Date != date.Date ||
            !TimeOnly.TryParseExact(EventTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { AgendaStatus.Text = "Choose a date and enter a valid time, such as 09:30."; return; }
        var local = DateTime.SpecifyKind(date.Date + time.ToTimeSpan(), DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
        { AgendaStatus.Text = "This time is skipped or repeated by daylight saving. Choose a different time."; return; }
        try
        {
            host.Calendar.Save(editing, EventTitle.Text, new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)), ReminderCheck.IsChecked == true);
            host.RefreshCalendar(); RefreshEntries(); ResetEditor(); AgendaStatus.Text = "Event saved on this device.";
        }
        catch (ArgumentException ex) { AgendaStatus.Text = ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SettingsStore.Log("calendar.save", ex); AgendaStatus.Text = "Couldn't save the event. Your previous agenda is unchanged. Check folder permissions and retry."; }
    }
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (editing is not Guid id) return;
        if (MessageBox.Show(this, "Delete the selected event?", "WinNotch", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            host.Calendar.Delete(id); host.RefreshCalendar(); RefreshEntries(); ResetEditor(); AgendaStatus.Text = "Event deleted.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SettingsStore.Log("calendar.delete", ex); AgendaStatus.Text = "Couldn't delete the event. It is still saved; please retry."; }
    }
}

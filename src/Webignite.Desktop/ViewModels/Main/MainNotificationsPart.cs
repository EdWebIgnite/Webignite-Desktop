using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Overview;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Notifications: every note the app shows in its corner is also kept here (rules in <see cref="NoteHistory"/>),
/// under the bell in the sidebar, until it is dismissed. Not a page: a list that drops down from the bell.
/// What a note opens when clicked is remembered only while the app is running.
/// </summary>
public partial class MainViewModel
{
    private readonly Dictionary<string, Action> noteActions = [];

    public ObservableCollection<KeptNoteRow> Notifications { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnseenNotifications), nameof(NotificationsTip))]
    public partial int UnseenNotifications { get; private set; }

    public bool HasUnseenNotifications => UnseenNotifications > 0;
    public bool HasNotifications => Notifications.Count > 0;
    public bool NoNotifications => Notifications.Count == 0;
    public string NotificationsTip => UnseenNotifications switch
    {
        0 => "Notifications",
        1 => "Notifications: 1 new",
        var n => $"Notifications: {n} new",
    };

    /// <summary>Keeps a note that was just shown. Called by the window for each one.</summary>
    public void KeepNote(string title, string text, Action? onClick)
    {
        var before = services.Settings.NoteHistory;
        var after = NoteHistory.Add(before, title, text, DateTimeOffset.Now);
        if (ReferenceEquals(before, after))
            return;
        services.Settings.NoteHistory = after;
        if (onClick is not null)
            noteActions[after[0].Id] = onClick;
        ShowNotifications();
    }

    /// <summary>Reads the kept notes into the list; called when the bell's list opens and after each change.</summary>
    public void ShowNotifications()
    {
        var kept = services.Settings.NoteHistory;
        var today = DateOnly.FromDateTime(DateTime.Today);
        Notifications.Clear();
        foreach (var note in kept)
            Notifications.Add(new KeptNoteRow(note, today, noteActions.GetValueOrDefault(note.Id), this));
        UnseenNotifications = NoteHistory.Unseen(kept);
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(NoNotifications));
    }

    /// <summary>The bell's list was opened: nothing in it is new any more.</summary>
    public void NotificationsSeen()
    {
        ShowNotifications();
        services.Settings.NoteHistory = NoteHistory.Seen(services.Settings.NoteHistory);
        UnseenNotifications = 0;
    }

    internal void DismissNotification(string id)
    {
        services.Settings.NoteHistory = NoteHistory.Dismiss(services.Settings.NoteHistory, id);
        noteActions.Remove(id);
        ShowNotifications();
    }

    [RelayCommand]
    private void ClearNotifications()
    {
        services.Settings.NoteHistory = [];
        noteActions.Clear();
        ShowNotifications();
    }
}

/// <summary>A kept note in the bell's list.</summary>
public sealed partial class KeptNoteRow(KeptNote note, DateOnly today, Action? open, MainViewModel owner)
{
    public string Title => note.Title;
    public string Text => note.Text;
    public bool IsNew => !note.Seen;
    public bool CanOpen => open is not null;

    /// <summary>The time for one from today, otherwise the day and the time. Follows the PC's region.</summary>
    public string When
    {
        get
        {
            var local = note.At.ToLocalTime();
            var day = DateOnly.FromDateTime(local.DateTime);
            return day == today ? local.ToString("t", CultureInfo.CurrentCulture) : $"{Webignite.Core.Wording.Day(day, today)}, {local.ToString("t", CultureInfo.CurrentCulture)}";
        }
    }

    [RelayCommand]
    private void Open() => open?.Invoke();

    [RelayCommand]
    private void Dismiss() => owner.DismissNotification(note.Id);
}

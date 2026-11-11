using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Inbox, as Asana's: what your partner did on tasks you follow or are given, comments that mention you, and likes
/// on what you made, newest first in Today, Yesterday, This week and Earlier. Clicking one opens the task and marks
/// it read; archiving takes it out of the list (Archive shows those).
/// </summary>
public sealed partial class InboxViewModel : ViewModelBase, IRefreshable, IDisposable
{
    private readonly AppServices services;

    public InboxViewModel(AppServices services)
    {
        this.services = services;
        services.Inbox.Changed += ElsewhereChanged;
    }

    public void Dispose() => services.Inbox.Changed -= ElsewhereChanged;

    // Another copy of the page, or a sync, changed what's read or new.
    private void ElsewhereChanged()
    {
        if (!refreshing)
            Refresh();
    }

    private bool refreshing;

    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>Showing what's been archived rather than what's waiting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsActivity))]
    public partial bool ShowsArchive { get; set; }

    public bool ShowsActivity => !ShowsArchive;

    [ObservableProperty] public partial string Summary { get; private set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; private set; }
    [ObservableProperty] public partial bool HasUnread { get; private set; }

    partial void OnShowsArchiveChanged(bool value) => Refresh();

    [RelayCommand]
    private void ShowActivity() => ShowsArchive = false;

    [RelayCommand]
    private void ShowArchive() => ShowsArchive = true;

    public void Refresh()
    {
        refreshing = true;
        try
        {
            var state = services.Inbox.State;
            var archived = state.Archived.ToHashSet(StringComparer.Ordinal);
            var read = state.Read.ToHashSet(StringComparer.Ordinal);
            var items = services.Inbox.Items().Where(i => archived.Contains(i.Key) == ShowsArchive).ToList();
            var now = DateTimeOffset.Now;
            var projects = services.Projects.Projects().ToDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal);
            Rows.Clear();
            foreach (var group in items.GroupBy(i => InboxRules.Group(i.At, now, TimeZoneInfo.Local)))
            {
                Rows.Add(new InboxHeading(group.Key));
                foreach (var item in group)
                    Rows.Add(new InboxRow(item, projects.GetValueOrDefault(item.ProjectId, ""), !read.Contains(item.Key),
                        TaskTalk.When(item.At, now, TimeZoneInfo.Local, CultureInfo.CurrentCulture), ShowsArchive, this));
            }
            var unread = items.Count(i => !read.Contains(i.Key));
            IsEmpty = items.Count == 0;
            HasUnread = !ShowsArchive && unread > 0;
            Summary = ShowsArchive
                ? (items.Count == 1 ? "1 archived" : $"{items.Count} archived")
                : unread switch { 0 => "All read.", 1 => "1 unread.", var n => $"{n} unread." };
        }
        finally
        {
            refreshing = false;
        }
    }

    [RelayCommand]
    private void MarkAllRead()
    {
        services.Inbox.MarkAllRead();
        Refresh();
    }

    internal void Open(InboxRow row)
    {
        services.Inbox.MarkRead([row.Item.Key]);
        Refresh();
        if (row.Item.MessageId is not null)
            services.Go.ProjectMessages(row.Item.ProjectId);
        else if (row.Item.Kind == InboxKind.Added)
            services.Go.Project(row.Item.ProjectId);
        else
            services.Go.ProjectTask(row.Item.TaskId);
    }

    internal void Archive(InboxRow row)
    {
        services.Inbox.Archive(row.Item.Key);
        Refresh();
        services.OfferUndo("Archived", () =>
        {
            services.Inbox.Unarchive(row.Item.Key);
            Refresh();
        });
    }

    internal void MoveBack(InboxRow row)
    {
        services.Inbox.Unarchive(row.Item.Key);
        Refresh();
    }
}

/// <summary>Today, Yesterday, This week or Earlier, above the items in it.</summary>
public sealed class InboxHeading(string title)
{
    public string Title { get; } = title;
}

/// <summary>One thing in Inbox: who did what to which task, and the comment when it's about one.</summary>
public sealed partial class InboxRow(InboxItem item, string projectName, bool unread, string when, bool archived, InboxViewModel page)
{
    public InboxItem Item { get; } = item;
    public string Who => Item.Who;
    public string Line => $"{Item.Who} {Item.Line}";
    public string TaskTitle => Item.TaskTitle;
    public string ProjectName { get; } = projectName;
    public bool HasProject => ProjectName.Length > 0 && Item.Kind != InboxKind.Added; // its title is the project already
    public string Snippet => Item.Snippet;
    public bool HasSnippet => Item.Snippet.Length > 0;
    public bool IsUnread { get; } = unread;
    public string When { get; } = when;
    public bool IsArchived { get; } = archived;
    public bool CanArchive => !IsArchived;

    [RelayCommand]
    private void Open() => page.Open(this);

    [RelayCommand]
    private void Archive() => page.Archive(this);

    [RelayCommand]
    private void MoveBack() => page.MoveBack(this);
}

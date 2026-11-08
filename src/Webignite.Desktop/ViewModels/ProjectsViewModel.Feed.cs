using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// A task's feed, as Asana's: Comments or All activity, and a box to comment. Synced in shared projects.
public sealed partial class TaskDetail
{
    public ObservableCollection<FeedRow> Feed { get; } = [];

    /// <summary>All activity (what happened, and the comments) rather than only the comments.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsComments))]
    public partial bool ShowsAll { get; set; }

    public bool ShowsComments => !ShowsAll;

    public bool NoFeed => Feed.Count == 0;

    [ObservableProperty] public partial string NewComment { get; set; } = "";

    /// <summary>The comment being changed in the box, or null for a new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommentButton))]
    public partial string? EditingCommentId { get; private set; }

    public string CommentButton => EditingCommentId is null ? "Comment" : "Save";

    public string MyName => services.Me().Name;

    partial void OnShowsAllChanged(bool value) => LoadFeed();

    [RelayCommand]
    private void ShowComments() => ShowsAll = false;

    [RelayCommand]
    private void ShowAllActivity() => ShowsAll = true;

    internal void LoadFeed()
    {
        var team = services.Team();
        string NameOf(string? id) => team.NameOf(id);
        var now = DateTimeOffset.Now;
        Feed.Clear();
        var comments = services.Projects.Comments(Task.Id);
        foreach (var line in TaskTalk.Feed(comments, services.Projects.Activity(Task.Id), ShowsAll, NameOf, CultureInfo.CurrentCulture))
        {
            // My own comments, and those Claude or ChatGPT wrote for me, can be changed and deleted.
            var mine = line.CommentId is { } id && comments.First(c => c.Id == id).Created.By is { } by && (by == team.Me!.Id || Actors.IsAssistant(by));
            Feed.Add(new FeedRow(line, TaskTalk.When(line.At, now, TimeZoneInfo.Local, CultureInfo.CurrentCulture), mine, this));
        }
        OnPropertyChanged(nameof(NoFeed));
    }

    /// <summary>Posts the comment typed, or saves the one being changed. Ctrl+Enter does the same.</summary>
    [RelayCommand]
    private void PostComment()
    {
        if (NewComment.Trim().Length == 0)
            return;
        if (EditingCommentId is { } id)
            page.Ours(() => services.Projects.EditComment(id, NewComment));
        else if (page.Ours(() => services.Projects.AddComment(Task.Id, NewComment)) is { Problem: { } problem })
        {
            Problem = problem;
            return;
        }
        NewComment = "";
        EditingCommentId = null;
        page.CommentsChanged();
        LoadFeed();
    }

    internal void EditComment(FeedRow row)
    {
        EditingCommentId = row.Line.CommentId;
        NewComment = row.Line.Text;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingCommentId = null;
        NewComment = "";
    }

    internal void DeleteComment(FeedRow row)
    {
        if (row.Line.CommentId is not { } id || page.Ours(() => services.Projects.DeleteComment(id)) is not { } gone)
            return;
        page.CommentsChanged();
        LoadFeed();
        services.OfferUndo("Comment deleted", () =>
        {
            services.Projects.RestoreComment(gone);
            LoadFeed();
        });
    }
}

/// <summary>A line in a task's feed: who, when, and what they wrote or did.</summary>
public sealed partial class FeedRow(FeedLine line, string when, bool mine, TaskDetail task)
{
    public FeedLine Line { get; } = line;
    public string Who => Line.Who;
    public string When { get; } = when;
    public string Text => Line.Text;
    public bool IsComment => Line.IsComment;
    public bool IsActivity => !Line.IsComment;

    /// <summary>My own comments can be changed and deleted.</summary>
    public bool IsMine { get; } = mine;

    [RelayCommand]
    private void Edit() => task.EditComment(this);

    [RelayCommand]
    private void Delete() => task.DeleteComment(this);
}

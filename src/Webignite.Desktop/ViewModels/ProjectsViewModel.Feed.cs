using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// A task's feed, as Asana's: Comments or All activity, and a box to comment with @mentions; likes and followers.
// Synced in shared projects.
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

    // Likes and followers, as Asana's: a thumbs-up by Mark complete, and who follows it above the comment box.

    [ObservableProperty] public partial bool ILikeIt { get; private set; }
    [ObservableProperty] public partial string LikeCount { get; private set; } = "";
    [ObservableProperty] public partial string LikeTip { get; private set; } = "Like this task";
    [ObservableProperty] public partial bool IFollow { get; private set; }
    [ObservableProperty] public partial string FollowersText { get; private set; } = "";

    public string FollowButton => IFollow ? "Stop following" : "Follow";

    partial void OnIFollowChanged(bool value) => OnPropertyChanged(nameof(FollowButton));

    private void ShowLikesAndFollowers()
    {
        var team = services.Team();
        ILikeIt = Task.LikedBy.Contains(team.Me!.Id);
        LikeCount = Task.LikedBy.Count > 0 ? Task.LikedBy.Count.ToString(CultureInfo.CurrentCulture) : "";
        LikeTip = Task.LikedBy.Count == 0 ? "Like this task" : "Liked by " + Names(Task.LikedBy, team);
        IFollow = Task.Followers.Contains(team.Me.Id);
        FollowersText = Task.Followers.Count == 0 ? "Nobody follows this task yet." : "Followers: " + Names(Task.Followers, team);
    }

    private static string Names(IEnumerable<string> ids, TeamSettings team)
    {
        var names = ids.Select(team.NameOf).ToList();
        return names.Count <= 1 ? string.Concat(names) : string.Join(", ", names[..^1]) + " and " + names[^1];
    }

    [RelayCommand]
    private void ToggleLike()
    {
        if (page.Ours(() => services.Projects.LikeTask(Task.Id, !ILikeIt)) is { } saved)
            Saved(saved, typed: true);
    }

    [RelayCommand]
    private void ToggleFollow()
    {
        if (page.Ours(() => services.Projects.FollowTask(Task.Id, !IFollow)) is { } saved)
            Saved(saved, typed: true);
    }

    internal void LikeComment(FeedRow row)
    {
        if (row.Line.CommentId is { } id && page.Ours(() => services.Projects.LikeComment(id, !row.ILikeIt)) is not null)
            LoadFeed();
    }

    /// <summary>People whose name matches what's typed after an @ at the end of the comment, to mention with a click.</summary>
    public ObservableCollection<string> MentionSuggestions { get; } = [];

    public bool HasMentionSuggestions => MentionSuggestions.Count > 0;

    partial void OnNewCommentChanged(string value)
    {
        MentionSuggestions.Clear();
        if (TaskTalk.MentionTyped(value) is { } typed)
        {
            var team = services.Team();
            foreach (var name in new[] { team.Partner!.Name, team.Me!.Name }.Where(n => n.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase) && n != typed))
                MentionSuggestions.Add(name);
        }
        OnPropertyChanged(nameof(HasMentionSuggestions));
    }

    [RelayCommand]
    private void Mention(string name) => NewComment = TaskTalk.CompleteMention(NewComment, name);

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
            var comment = line.CommentId is { } id ? comments.First(c => c.Id == id) : null;
            var mine = comment?.Created.By is { } by && (by == team.Me!.Id || Actors.IsAssistant(by));
            Feed.Add(new FeedRow(line, TaskTalk.When(line.At, now, TimeZoneInfo.Local, CultureInfo.CurrentCulture), mine, this)
            {
                ILikeIt = comment?.LikedBy.Contains(team.Me!.Id) == true,
                Likes = comment?.LikedBy.Count ?? 0,
                LikeTip = comment is { LikedBy.Count: > 0 } ? "Liked by " + Names(comment.LikedBy, team) : "Like this comment",
            });
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

    public bool ILikeIt { get; init; }
    public int Likes { get; init; }
    public string LikeTip { get; init; } = "";
    public string LikeCount => Likes > 0 ? Likes.ToString(CultureInfo.CurrentCulture) : "";

    [RelayCommand]
    private void ToggleLike() => task.LikeComment(this);

    [RelayCommand]
    private void Edit() => task.EditComment(this);

    [RelayCommand]
    private void Delete() => task.DeleteComment(this);
}

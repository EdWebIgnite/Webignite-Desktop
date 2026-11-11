using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// The Messages tab, as Asana's: conversations about the whole project rather than one task, each with replies,
// @mentions and likes. In a shared project they sync and show in your partner's Inbox.
public sealed partial class ProjectsViewModel
{
    /// <summary>The Messages tab is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList), nameof(ShowsTaskTools))]
    public partial bool IsMessages { get; set; }

    public ObservableCollection<MessageThreadRow> MessageThreads { get; } = [];

    public bool NoMessages => MessageThreads.Count == 0;

    [ObservableProperty] public partial string NewMessageSubject { get; set; } = "";
    [ObservableProperty] public partial string NewMessageText { get; set; } = "";
    [ObservableProperty] public partial string MessageProblem { get; private set; } = "";

    public bool HasMessageProblem => MessageProblem.Length > 0;

    partial void OnMessageProblemChanged(string value) => OnPropertyChanged(nameof(HasMessageProblem));

    partial void OnIsMessagesChanged(bool value)
    {
        if (value)
        {
            ShowOnly(ProjectTab.Messages);
            ListMessages();
        }
        if (open is not null && Layout.Messages != value)
            SaveLayout(Layout with { Messages = value });
    }

    [RelayCommand]
    private void MessagesView() => IsMessages = true;

    public string MyName => services.Me().Name;

    /// <summary>Under Share: how sharing reaches your partner, or what's needed first.</summary>
    public string ShareNote => services.MaySync
        ? $"Shared projects sync with {services.Team().Partner!.Name} every 15 minutes (or click Sync now in Settings), locked on this PC so only the two of you can read them. Untick to make it yours alone again."
        : "Sign in on both PCs under Settings, Account and sync, and shared projects sync between you. Until then they stay on this PC.";

    internal void ListMessages()
    {
        MessageThreads.Clear();
        if (open is null)
            return;
        var team = services.Team();
        var now = DateTimeOffset.Now;
        MessageRow Row(ProjectMessage m) => new(m, team.NameOf(m.Created.By), TaskTalk.When(m.Created.At, now, TimeZoneInfo.Local, CultureInfo.CurrentCulture),
            m.Created.By == team.Me!.Id || Actors.IsAssistant(m.Created.By), m.LikedBy.Contains(team.Me.Id), this);
        foreach (var thread in services.Projects.Messages(open.Id))
            MessageThreads.Add(new MessageThreadRow(Row(thread.Start), [.. thread.Replies.Select(Row)], this));
        OnPropertyChanged(nameof(NoMessages));
    }

    /// <summary>Starts a conversation with the subject and words typed.</summary>
    [RelayCommand]
    private void PostMessage()
    {
        if (open is null)
            return;
        var (saved, problem) = Ours(() => services.Projects.PostMessage(open.Id, NewMessageSubject, NewMessageText));
        MessageProblem = problem ?? "";
        if (saved is null)
            return;
        NewMessageSubject = NewMessageText = "";
        ListMessages();
    }

    internal void Reply(MessageThreadRow thread)
    {
        if (open is null)
            return;
        var (saved, problem) = Ours(() => services.Projects.PostMessage(open.Id, "", thread.ReplyText, thread.Start.Message.Id));
        MessageProblem = problem ?? "";
        if (saved is not null)
            ListMessages();
    }

    internal void LikeMessage(MessageRow row)
    {
        if (Ours(() => services.Projects.LikeMessage(row.Message.Id, !row.ILikeIt)) is not null)
            ListMessages();
    }

    internal void DeleteMessage(MessageRow row)
    {
        if (Ours(() => services.Projects.DeleteMessage(row.Message.Id)) is not { } gone)
            return;
        ListMessages();
        services.OfferUndo(gone.ReplyTo is null ? "Message deleted" : "Reply deleted", () =>
        {
            services.Projects.RestoreMessage(gone);
            ListMessages();
        });
    }
}

/// <summary>A conversation on the Messages tab: the message that started it, its replies, and a box to reply.</summary>
public sealed partial class MessageThreadRow(MessageRow start, IReadOnlyList<MessageRow> replies, ProjectsViewModel page) : ObservableObject
{
    public MessageRow Start { get; } = start;
    public IReadOnlyList<MessageRow> Replies { get; } = replies;
    public string Subject => Start.Message.Subject;
    public string ReplyCount => Replies.Count switch { 0 => "No replies yet", 1 => "1 reply", var n => $"{n} replies" };

    [ObservableProperty] public partial string ReplyText { get; set; } = "";

    [RelayCommand]
    private void Reply() => page.Reply(this);
}

/// <summary>One message or reply: who, when, what, and its likes. My own can be deleted.</summary>
public sealed partial class MessageRow(ProjectMessage message, string who, string when, bool mine, bool iLikeIt, ProjectsViewModel page)
{
    public ProjectMessage Message { get; } = message;
    public string Who { get; } = who;
    public string When { get; } = when;
    public string Text => Message.Text;
    public bool IsMine { get; } = mine;
    public bool ILikeIt { get; } = iLikeIt;
    public string LikeCount => Message.LikedBy.Count > 0 ? Message.LikedBy.Count.ToString(CultureInfo.CurrentCulture) : "";

    [RelayCommand]
    private void ToggleLike() => page.LikeMessage(this);

    [RelayCommand]
    private void Delete() => page.DeleteMessage(this);
}

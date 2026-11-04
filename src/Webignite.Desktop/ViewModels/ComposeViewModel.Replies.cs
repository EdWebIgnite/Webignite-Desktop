using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>Saved replies in the editor: put one in where the cursor is, save selected text as one, or remove one.</summary>
public partial class ComposeViewModel
{
    public IReadOnlyList<SavedReply> SavedReplyList => services.Settings.SavedReplies;

    [ObservableProperty] public partial bool AskingReplyName { get; set; }
    [ObservableProperty] public partial string ReplyName { get; set; } = "";
    [ObservableProperty] public partial string ReplyProblem { get; set; } = "";

    public bool HasReplyProblem => ReplyProblem.Length > 0;
    partial void OnReplyProblemChanged(string value) => OnPropertyChanged(nameof(HasReplyProblem));

    private string replyText = "";

    /// <summary>The reply with {name} and {client} filled in for the first person in To.</summary>
    public string Fill(SavedReply reply)
    {
        var to = Recipients.Parse(To).People.FirstOrDefault();
        var client = SavedReplies.ClientFor(services.Clients.List(), to?.Address);
        return SavedReplies.Fill(reply.Text, SavedReplies.FirstName(to, client), client?.Name);
    }

    /// <summary>A reply as writing, ready to put in at the caret.</summary>
    public Webignite.Core.Writing.RichDocument Writing(SavedReply reply) => Webignite.Core.Writing.RichText.FromText(Fill(reply));

    /// <summary>Asks for a name for the selected text, or the whole mail when nothing is selected.</summary>
    public void StartSaveReply(string text)
    {
        replyText = text.Trim();
        ReplyName = "";
        ReplyProblem = "";
        AskingReplyName = true;
    }

    [RelayCommand]
    private void SaveReply()
    {
        var problems = SavedReplies.Check(ReplyName, replyText, SavedReplyList);
        if (problems.Count > 0)
        {
            ReplyProblem = string.Join(" ", problems);
            return;
        }
        services.Settings.SavedReplies = [.. SavedReplyList, new SavedReply(SavedReply.NewId(), ReplyName.Trim(), replyText)];
        AskingReplyName = false;
        OnPropertyChanged(nameof(SavedReplyList));
        Say($"Saved \"{ReplyName.Trim()}\". Find it under Saved replies. Type {{name}} or {{client}} in a reply to have them filled in.", false);
    }

    [RelayCommand]
    private void CancelSaveReply() => AskingReplyName = false;

    public void RemoveReply(SavedReply reply)
    {
        var before = SavedReplyList;
        var at = before.ToList().FindIndex(r => r.Id == reply.Id);
        services.Settings.SavedReplies = before.Where(r => r.Id != reply.Id).ToList();
        OnPropertyChanged(nameof(SavedReplyList));
        services.OfferUndo($"Removed the saved reply \"{reply.Name}\"", () =>
        {
            services.Settings.SavedReplies = Webignite.Core.Layout.ListUndo.PutBack(services.Settings.SavedReplies, reply, at, r => r.Id);
            OnPropertyChanged(nameof(SavedReplyList));
        });
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>One open message: who, when, the body, attachments, and what you can do with it.</summary>
public partial class MailReaderViewModel(MailSummary summary, MailAccount account, AppServices services, MailViewModel owner) : ViewModelBase
{
    private byte[]? mime;

    public MailSummary Summary { get; private set; } = summary;
    public MailAccount Account => account;

    public string Subject => string.IsNullOrWhiteSpace(Summary.Subject) ? "(no subject)" : Summary.Subject;
    public string From => Summary.From?.ToString() ?? "Unknown sender";
    public string To => string.Join(", ", Summary.To.Select(p => p.ToString()));
    public string Cc => string.Join(", ", Summary.Cc.Select(p => p.ToString()));
    public bool HasCc => Summary.Cc.Count > 0;
    public string Date => Summary.Date.ToLocalTime().ToString("dddd, d MMMM yyyy, HH:mm");
    public string FlagLabel => Summary.Flagged ? "Unflag" : "Flag";
    public bool IsDraft => services.Store.ListMailFolders(account.Id).Any(f => f.Path == Summary.Folder && f.Role == FolderRole.Drafts);
    public bool IsNotDraft => !IsDraft;
    public bool CanWrite => mime is not null;

    public ObservableCollection<AttachmentViewModel> Attachments { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;

    public IReadOnlyList<MoveTarget> MoveTargets { get; } =
        services.Store.ListMailFolders(account.Id).Where(f => f.Path != summary.Folder).Select(f => new MoveTarget(f.Name, f.Path)).ToList();

    [ObservableProperty] public partial string Html { get; set; } = "";
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial string Problem { get; set; } = "";
    [ObservableProperty] public partial bool CanShowImages { get; set; }
    [ObservableProperty] public partial string DraftedBy { get; set; } = "";

    public bool HasDraftedBy => DraftedBy.Length > 0;
    partial void OnDraftedByChanged(string value) => OnPropertyChanged(nameof(HasDraftedBy));
    [ObservableProperty] public partial string AttachmentMessage { get; set; } = "";
    [ObservableProperty] public partial MoveTarget? MoveTo { get; set; }

    public bool HasProblem => Problem.Length > 0;
    public bool HasAttachmentMessage => AttachmentMessage.Length > 0;

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));
    partial void OnAttachmentMessageChanged(string value) => OnPropertyChanged(nameof(HasAttachmentMessage));

    public async Task Load()
    {
        IsLoading = true;
        try
        {
            mime = await services.Mail.GetMessageAsync(account, Summary.Folder, Summary.Uid);
            Show(allowImages: false);
            OnPropertyChanged(nameof(CanWrite));
        }
        catch (MailException e)
        {
            Problem = e.Message + " Mail opened before can still be read without internet.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Show(bool allowImages)
    {
        if (mime is null)
            return;
        var body = MailReader.Read(mime, allowImages);
        Html = MailStyle.Wrap(body.Html ?? "");
        CanShowImages = body.HasRemoteImages && !allowImages;
        DraftedBy = body.DraftedBy is { Length: > 0 } who ? $"{who} drafted this. Read it through before you send it." : "";
        Attachments.Clear();
        foreach (var attachment in body.Attachments)
            Attachments.Add(new AttachmentViewModel(attachment, this));
        OnPropertyChanged(nameof(HasAttachments));
    }

    /// <summary>Pictures from the internet stay off until asked for, so senders can't tell when you read their mail.</summary>
    [RelayCommand]
    private void ShowImages() => Show(allowImages: true);

    [RelayCommand]
    private async Task ToggleFlag()
    {
        var flagged = !Summary.Flagged;
        if (await owner.Run(() => services.Mail.SetFlaggedAsync(account, Summary, flagged), quiet: true))
        {
            Summary = Summary with { Flagged = flagged };
            OnPropertyChanged(nameof(FlagLabel));
            owner.Changed(Summary);
        }
    }

    [RelayCommand]
    private async Task MarkUnread()
    {
        if (await owner.Run(() => services.Mail.SetSeenAsync(account, Summary, false), quiet: true))
        {
            Summary = Summary with { Seen = false };
            owner.Changed(Summary);
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (await owner.Run(() => services.Mail.DeleteAsync(account, Summary), quiet: true))
            owner.Removed(Summary);
    }

    partial void OnMoveToChanged(MoveTarget? value)
    {
        if (value is not null)
            _ = Move(value);
    }

    private async Task Move(MoveTarget target)
    {
        if (await owner.Run(() => services.Mail.MoveAsync(account, Summary, target.Path), quiet: true))
        {
            owner.Removed(Summary);
            owner.SetStatus($"Moved to {target.Name}.", false);
        }
    }

    [RelayCommand]
    private void Reply() => Write(all: false);

    [RelayCommand]
    private void ReplyAll() => Write(all: true);

    private void Write(bool all)
    {
        if (mime is null)
            return;
        var mine = owner.Accounts.Select(a => a.Address).ToList();
        owner.Compose(MailComposer.Reply(mime, account, mine, all), source: null, answering: Summary);
    }

    [RelayCommand]
    private void Forward()
    {
        if (mime is not null)
            owner.Compose(MailComposer.Forward(mime, account), source: mime);
    }

    [RelayCommand]
    private void EditDraft()
    {
        if (mime is not null)
            owner.Compose(MailComposer.FromDraft(mime, account, Summary.Uid), source: mime);
    }

    [RelayCommand]
    private void SaveAll()
    {
        var saved = Attachments.Select(a => a.SaveToDownloads()).OfType<string>().ToList();
        AttachmentMessage = saved.Count == 0 ? "" : $"Saved {saved.Count} file{(saved.Count == 1 ? "" : "s")} to Downloads.";
    }

    internal string? Save(MailAttachment attachment, string folder)
    {
        if (mime is null)
            return null;
        var path = UniquePath(folder, attachment.FileName);
        using (var file = File.Create(path))
            MailReader.SaveAttachment(mime, attachment.Index, file);
        return path;
    }

    internal void Said(string message) => AttachmentMessage = message;

    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{stem} ({i}){extension}");
        return path;
    }
}

public sealed record MoveTarget(string Name, string Path)
{
    public override string ToString() => Name;
}

public partial class AttachmentViewModel(MailAttachment attachment, MailReaderViewModel reader) : ViewModelBase
{
    /// <summary>Files that run as programs. They can be saved, but the app won't start them.</summary>
    private static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".msi", ".msp", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".wsh",
        ".ps1", ".psm1", ".hta", ".jar", ".lnk", ".reg", ".cpl", ".dll", ".iso", ".img", ".vhd", ".vhdx", ".appx", ".msix",
    };

    public string Name => attachment.FileName;
    public string Size => FormatSize(attachment.Size);

    public static string FormatSize(long size) => size switch
    {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024.0:0} KB",
        _ => $"{size / 1024.0 / 1024.0:0.0} MB",
    };

    internal string? SaveToDownloads()
    {
        try
        {
            return reader.Save(attachment, AppServices.DownloadsFolder());
        }
        catch (IOException e)
        {
            reader.Said($"Couldn't save {Name}: {e.Message}");
            return null;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (SaveToDownloads() is { } path)
            reader.Said($"Saved to Downloads as {Path.GetFileName(path)}.");
    }

    [RelayCommand]
    private void Open()
    {
        if (Programs.Contains(Path.GetExtension(Name)))
        {
            if (SaveToDownloads() is { } saved)
                reader.Said($"{Path.GetFileName(saved)} is a program, so it was saved to Downloads but not opened. Only run it if you trust the sender.");
            return;
        }
        try
        {
            var folder = Directory.CreateTempSubdirectory("webignite-attachment-").FullName;
            if (reader.Save(attachment, folder) is { } path)
                AppServices.OpenFile(path);
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            reader.Said($"Couldn't open {Name}: {e.Message}");
        }
    }
}

/// <summary>The base look for mail bodies, so plain mail reads in the app's own font and size.</summary>
public static class MailStyle
{
    public static string Wrap(string html) =>
        "<div style=\"font-family: 'Segoe UI', Inter, Arial, sans-serif; font-size: 10.5pt; color: #1F2A26;\">" + html + "</div>";
}

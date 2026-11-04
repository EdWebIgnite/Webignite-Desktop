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
    public bool IsImportant => Summary.Flagged;
    public IReadOnlyList<MailCategory> Categories => MailCategories.Of(owner.CategoryList, Summary);
    public bool HasCategories => Categories.Count > 0;
    public bool IsDraft => services.Store.ListMailFolders(account.Id).Any(f => f.Path == Summary.Folder && f.Role == FolderRole.Drafts);
    public bool IsNotDraft => !IsDraft;
    public bool CanWrite => mime is not null;

    /// <summary>The whole conversation this mail is in, oldest first. Just this mail when there is no conversation.</summary>
    public IReadOnlyList<MailSummary> Thread { get; init; } = [summary];

    /// <summary>The conversation stacked above the open mail, one line per mail, the open one marked.</summary>
    public IReadOnlyList<ThreadLineViewModel> ThreadLines => Thread.Count < 2 ? [] :
        Thread.Select(m => new ThreadLineViewModel(m, MailViewModel.Key(m) == MailViewModel.Key(Summary),
            () => owner.ShowInThread(m, account, Thread))).ToList();
    public bool HasThread => Thread.Count > 1;
    public string ThreadTitle => $"{Thread.Count} mails in this conversation";

    public ObservableCollection<AttachmentViewModel> Attachments { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;

    [ObservableProperty] public partial string Html { get; set; } = "";
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial string Problem { get; set; } = "";
    [ObservableProperty] public partial bool CanShowImages { get; set; }
    [ObservableProperty] public partial string DraftedBy { get; set; } = "";

    public bool HasDraftedBy => DraftedBy.Length > 0;
    partial void OnDraftedByChanged(string value) => OnPropertyChanged(nameof(HasDraftedBy));
    [ObservableProperty] public partial string AttachmentMessage { get; set; } = "";

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

    /// <summary>Called when the open mail's marks or categories change.</summary>
    internal void Update(MailSummary updated)
    {
        Summary = updated;
        OnPropertyChanged(nameof(IsImportant));
        RefreshCategories();
    }

    internal void RefreshCategories()
    {
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(HasCategories));
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
        if (mime is null)
            return;
        // Saving the edited draft makes a new one, so a send set for this one would find nothing: it is taken off.
        if (Scheduled is not null)
            owner.Unschedule(account.Id, Summary.Uid);
        owner.Compose(MailComposer.FromDraft(mime, account, Summary.Uid), source: mime);
    }

    /// <summary>The send set for this draft, if it is one waiting to go later.</summary>
    public ScheduledSend? Scheduled => IsDraft ? SendLater.For(services.Settings.SendLater.Scheduled, account.Id, Summary.Uid) : null;
    public bool IsScheduled => Scheduled is not null;
    public string ScheduledText => Scheduled is { } s ? $"Set to send {SendLater.When(s.SendAt, DateTimeOffset.Now)}. Editing it takes it off, so set the time again when you send." : "";

    [RelayCommand]
    private void DontSend()
    {
        owner.Unschedule(account.Id, Summary.Uid);
        OnPropertyChanged(nameof(Scheduled));
        OnPropertyChanged(nameof(IsScheduled));
        OnPropertyChanged(nameof(ScheduledText));
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

    /// <summary>The attachment shown in the reading pane in place of the mail, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewing), nameof(IsNotPreviewing))]
    public partial AttachmentPreviewViewModel? Preview { get; set; }

    partial void OnPreviewChanged(AttachmentPreviewViewModel? oldValue, AttachmentPreviewViewModel? newValue) => oldValue?.Free();

    /// <summary>Closes an open preview, e.g. when another mail is opened in its place.</summary>
    internal void ClosePreview() => Preview = null;

    public bool IsPreviewing => Preview is not null;
    public bool IsNotPreviewing => Preview is null;

    internal async Task ShowPreview(AttachmentViewModel row, MailAttachment attachment)
    {
        if (mime is null)
            return;
        using var buffer = new MemoryStream();
        MailReader.SaveAttachment(mime, attachment.Index, buffer);
        var preview = new AttachmentPreviewViewModel(row, () => Preview = null);
        Preview = preview;
        await preview.Load(buffer.ToArray(), row.PreviewKind);
    }

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

    /// <summary>PDFs and pictures open inside the app; everything else in another program.</summary>
    public PreviewKind PreviewKind => AttachmentPreview.Kind(attachment.FileName, attachment.ContentType, attachment.Size);
    public bool CanPreview => PreviewKind != PreviewKind.None;

    [RelayCommand]
    private Task Preview() => reader.ShowPreview(this, attachment);

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

/// <summary>
/// The base look for mail bodies, so plain mail reads in the app's own font and size. Mail always sits on the
/// Paper colour (white in every look), so the dark text here is the light palette's Text on purpose.
/// </summary>
public static class MailStyle
{
    public static string Wrap(string html) =>
        "<div style=\"font-family: 'Segoe UI', Inter, Arial, sans-serif; font-size: 10.5pt; color: #1F2A26;\">" + html + "</div>";
}

/// <summary>One mail in the conversation stack above the reader: who, when and the first words. Click to open it.</summary>
public sealed partial class ThreadLineViewModel(MailSummary mail, bool isOpen, Func<Task> open)
{
    public string Who => mail.From?.Display ?? "Unknown sender";
    public string When => mail.Date.ToLocalTime().ToString("d MMM, HH:mm");
    public string Preview => mail.Preview;
    public bool IsOpen => isOpen;
    public bool IsUnread => !mail.Seen;

    [RelayCommand]
    private Task Open() => isOpen ? Task.CompletedTask : open();
}

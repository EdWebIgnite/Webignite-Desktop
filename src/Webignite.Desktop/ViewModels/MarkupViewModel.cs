using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Mail;
using Webignite.Core.Tools;
using Webignite.Data;
using Webignite.Desktop.Controls;

namespace Webignite.Desktop.ViewModels;

/// <summary>Mark up a screenshot of a client's site with numbered notes, arrows and boxes, then mail it to them.</summary>
public sealed partial class MarkupViewModel : ViewModelBase, IHoldsWork, IDisposable
{
    public bool HoldsWork => Image is not null || Recording is not null;

    /// <summary>A screen recording just made, being trimmed and saved; the picture tools step aside meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecording), nameof(ShowsPicture))]
    public partial RecordingViewModel? Recording { get; private set; }

    public bool HasRecording => Recording is not null;
    public bool ShowsPicture => Recording is null;

    public void ShowRecording(string path, TimeSpan length, IVideoTools video, string? soundProblem)
    {
        Recording?.Dispose();
        Recording = new RecordingViewModel(path, length, video, this, services, soundProblem);
        Say(soundProblem ?? "Drag the handles to cut the start and end, then save it.", problem: soundProblem is not null);
    }

    public void CloseRecording()
    {
        Recording?.Dispose();
        Recording = null;
        Say("");
    }

    public void Dispose()
    {
        Recording?.Dispose();
        Recording = null;
    }

    private readonly AppServices services;

    public MarkupViewModel(AppServices services)
    {
        this.services = services;
        ChosenTool = Tools[0];
        ChosenPen = Pens[0];
    }

    public IReadOnlyList<ToolChoice> Tools { get; } =
    [
        new(MarkKind.Pin, "Numbered note"), new(MarkKind.Arrow, "Arrow"), new(MarkKind.Box, "Box"),
        new(MarkKind.Highlight, "Highlight"), new(MarkKind.Pen, "Pen"), new(MarkKind.Text, "Text"),
        new(MarkKind.Blur, "Blur, for passwords and details"),
    ];

    public IReadOnlyList<PenChoice> Pens { get; } = [new(MarkColour.Red, "Red"), new(MarkColour.Yellow, "Yellow"), new(MarkColour.Blue, "Blue")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(ImageSize))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand), nameof(EmailCommand), nameof(CopyImageCommand), nameof(ClearMarksCommand), nameof(SaveToFolderCommand), nameof(AddToTodoCommand))]
    public partial Bitmap? Image { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand), nameof(ClearMarksCommand))]
    public partial IReadOnlyList<Mark> Marks { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tool))]
    public partial ToolChoice? ChosenTool { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Pen))]
    public partial PenChoice? ChosenPen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveToText))]
    public partial Client? Client { get; set; }

    /// <summary>The last picture saved, so the same one isn't saved twice for a to-do.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    public partial string? SavedPath { get; private set; }

    public bool HasSaved => SavedPath is not null;
    [ObservableProperty] public partial string Page { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool MessageProblem { get; set; }

    public MarkKind Tool => ChosenTool?.Kind ?? MarkKind.Pin;
    public MarkColour Pen => ChosenPen?.Colour ?? MarkColour.Red;
    public bool HasImage => Image is not null;
    public string ImageSize => Image is { } i ? $"{i.PixelSize.Width} × {i.PixelSize.Height}" : "";
    public bool HasPins => Pins.Count > 0;

    public ObservableCollection<PinRow> Pins { get; } = [];
    public ObservableCollection<Client> Clients { get; } = [];

    /// <summary>Called when the page is shown, so new clients appear in the list.</summary>
    public void LoadClients()
    {
        // Updated in place: emptying the list would make the client boxes drop the client picked.
        var chosen = Client?.Id;
        var fresh = services.Clients.List();
        foreach (var gone in Clients.Where(c => fresh.All(f => f.Id != c.Id)).ToList())
            Clients.Remove(gone);
        for (var i = 0; i < fresh.Count; i++)
        {
            var at = Clients.ToList().FindIndex(c => c.Id == fresh[i].Id);
            if (at < 0)
                Clients.Insert(Math.Min(i, Clients.Count), fresh[i]);
            else if (Clients[at] != fresh[i] && fresh[i].Id != chosen)
                Clients[at] = fresh[i];
        }
        Client = Clients.FirstOrDefault(c => c.Id == chosen);
    }

    public void Show(Bitmap image)
    {
        var old = Image;
        Image = image;
        Marks = [];
        SavedPath = null;
        Pins.Clear();
        OnPropertyChanged(nameof(HasPins));
        Say("Click to drop a numbered note, or drag to draw. Type what each number means on the right.");
        if (!ReferenceEquals(old, image))
            old?.Dispose();
    }

    /// <summary>Words typed for a Text mark where the picture was clicked.</summary>
    public void AddText(MarkPoint at, string words) => Add(new Mark(MarkKind.Text, Pen, [at]) { Comment = words });

    public void Add(Mark mark)
    {
        SavedPath = null;
        var before = Marks.Count;
        Marks = Markup.Add(Marks, mark);
        if (Marks.Count > before && Marks[^1].Kind == MarkKind.Pin)
            SyncPins(focusNumber: Marks[^1].Number);
    }

    private void SyncPins(int? focusNumber = null)
    {
        Pins.Clear();
        foreach (var pin in Marks.Where(m => m.Kind == MarkKind.Pin).OrderBy(m => m.Number))
            Pins.Add(new PinRow(pin.Number, pin.Comment, this) { Focus = pin.Number == focusNumber });
        OnPropertyChanged(nameof(HasPins));
    }

    internal void SetComment(int number, string comment) => Marks = Markup.SetComment(Marks, number, comment);

    internal void RemovePin(int number)
    {
        Marks = Markup.RemovePin(Marks, number);
        SyncPins();
    }

    private bool CanUndo() => Marks.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Marks = Markup.Undo(Marks);
        SyncPins();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void ClearMarks()
    {
        Marks = [];
        SyncPins();
    }

    public byte[]? Png() => Image is { } image ? MarkupCanvas.RenderPng(image, Marks) : null;

    public string FileName => Markup.FileName(Client?.Name, DateTime.Now);

    [RelayCommand(CanExecute = nameof(HasImage))]
    private async Task CopyImage(Avalonia.Controls.TopLevel? top)
    {
        if (Png() is not { } png || top?.Clipboard is not { } clipboard)
            return;
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        await Avalonia.Input.Platform.ClipboardExtensions.SetBitmapAsync(clipboard, bitmap);
        Say("Copied. Paste it into WhatsApp, Word or anywhere else.");
    }

    /// <summary>Opens a mail with the marked-up picture attached and the numbered notes written out.</summary>
    [RelayCommand(CanExecute = nameof(HasImage))]
    private async Task Email()
    {
        if (services.Drafts.EverydayMailbox is not { } mailbox)
        {
            Say(MailDrafts.NoMailbox, problem: true);
            return;
        }
        if (Png() is not { } png)
            return;
        var picture = await services.Drafts.AttachAsync(FileName, png);
        var (subject, body) = Markup.Mail(Client?.Name, Client?.ContactPerson, Page, Marks, services.Settings.Xero.TenantName ?? "Webignite");
        var unwritten = Marks.Count(m => m.Kind == MarkKind.Pin && string.IsNullOrWhiteSpace(m.Comment));
        var opened = services.Drafts.Open(new MailDraft(mailbox, Client?.Email, subject, body)
        {
            Attachments = [picture],
            Note = unwritten > 0 ? $"{unwritten} numbered note(s) have nothing written, so they're not in the list." : null,
        });
        if (!opened)
            Say(MailDrafts.AnotherOpen, problem: true);
    }

    /// <summary>"Save to Bosveld Builders' folder" when the client has one, else "Save to Screenshots".</summary>
    public string SaveToText => ClientFolder() is not null && Client is { } c
        ? $"Save to {c.Name}{(c.Name.EndsWith('s') ? "'" : "'s")} folder"
        : "Save to Screenshots";

    private string? ClientFolder()
    {
        if (Client is not { } client)
            return null;
        var root = Files.FileOps.ClientRoot(services.Settings.Files);
        var match = Webignite.Core.Files.ClientFolders.For(client, root is null ? [] : Files.FileOps.Folders(root));
        return match.Fit == Webignite.Core.Files.ClientFolders.Fit.Sure && match.Folder is { } folder && Directory.Exists(folder) ? folder : null;
    }

    /// <summary>Where screenshots without a client go: the folder in Settings, else Pictures\Screenshots.</summary>
    public string ScreenshotsFolder()
    {
        if (services.Settings.Screenshots.Folder is { Length: > 0 } chosen)
            return chosen;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Path.Combine(pictures.Length > 0 ? pictures : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Screenshots");
    }

    /// <summary>Saves the marked-up picture in the client's folder, or in Screenshots.</summary>
    [RelayCommand(CanExecute = nameof(HasImage))]
    private void SaveToFolder() => SaveNow();

    /// <summary>The client's folder when they have one, else Screenshots.</summary>
    public string SaveFolder() => ClientFolder() ?? ScreenshotsFolder();

    private string? SaveNow()
    {
        if (Png() is not { } png)
            return null;
        var folder = SaveFolder();
        try
        {
            Directory.CreateDirectory(folder);
            var name = Webignite.Core.Files.FileNames.Unique(FileName, false, Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>());
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, png);
            SavedPath = path;
            Say($"Saved {name} in {folder}.");
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say("Couldn't save it: " + e.Message, problem: true);
            return null;
        }
    }

    [RelayCommand]
    private void ShowSaved()
    {
        if (SavedPath is { } path && Path.GetDirectoryName(path) is { } folder)
            services.Go.Folder(folder);
    }

    /// <summary>A to-do to come back to it, with the saved picture's place in its notes and the first note as its title.</summary>
    [RelayCommand(CanExecute = nameof(HasImage))]
    private void AddToTodo()
    {
        var path = SavedPath ?? SaveNow();
        if (path is null)
            return;
        var first = Marks.Where(m => m.Kind == MarkKind.Pin && m.Comment.Trim().Length > 0).OrderBy(m => m.Number).FirstOrDefault()?.Comment.Trim();
        services.Store.SaveTask(new Webignite.Core.Tasks.TaskItem
        {
            Id = Webignite.Core.Tasks.TaskItem.NewId(),
            Title = first ?? (Client is { } c ? $"Look at the screenshot for {c.Name}" : "Look at the screenshot"),
            Notes = "Screenshot: " + path + (Markup.Feedback(Marks) is { Length: > 0 } notes ? "\n\n" + notes : ""),
            Customer = Client?.Name,
            Created = DateTimeOffset.UtcNow,
        });
        Say("Added to My tasks, with where the screenshot is saved.");
    }

    public void Say(string text, bool problem = false)
    {
        Message = text;
        MessageProblem = problem;
    }
}

public sealed record ToolChoice(MarkKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record PenChoice(MarkColour Colour, string Label)
{
    public IBrush Brush => new SolidColorBrush(MarkupCanvas.ColourOf(Colour));
}

/// <summary>One numbered note and what it says.</summary>
public sealed partial class PinRow : ViewModelBase
{
    private readonly MarkupViewModel owner;

    public PinRow(int number, string comment, MarkupViewModel owner)
    {
        this.owner = owner;
        Number = number;
        text = comment;
    }

    public int Number { get; }
    public bool Focus { get; init; }

    private string text;

    public string Comment
    {
        get => text;
        set
        {
            if (SetProperty(ref text, value))
                owner.SetComment(Number, value);
        }
    }

    [RelayCommand]
    private void Remove() => owner.RemovePin(Number);
}

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
public sealed partial class MarkupViewModel : ViewModelBase
{
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
        new(MarkKind.Highlight, "Highlight"), new(MarkKind.Pen, "Pen"),
    ];

    public IReadOnlyList<PenChoice> Pens { get; } = [new(MarkColour.Red, "Red"), new(MarkColour.Yellow, "Yellow"), new(MarkColour.Blue, "Blue")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(ImageSize))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand), nameof(EmailCommand), nameof(CopyImageCommand), nameof(ClearMarksCommand))]
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

    [ObservableProperty] public partial Client? Client { get; set; }
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
        var chosen = Client?.Id;
        Clients.Clear();
        foreach (var c in services.Store.ListClients().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            Clients.Add(c);
        Client = Clients.FirstOrDefault(c => c.Id == chosen);
    }

    public void Show(Bitmap image)
    {
        var old = Image;
        Image = image;
        Marks = [];
        Pins.Clear();
        OnPropertyChanged(nameof(HasPins));
        Say("Click to drop a numbered note, or drag to draw. Type what each number means on the right.");
        if (!ReferenceEquals(old, image))
            old?.Dispose();
    }

    public void Add(Mark mark)
    {
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
        var accounts = services.Store.ListMailAccounts();
        if ((accounts.FirstOrDefault(a => !a.SendsInvoices) ?? accounts.FirstOrDefault()) is not { } mailbox)
        {
            Say("Add a mailbox under Mail first.", problem: true);
            return;
        }
        if (Png() is not { } png)
            return;
        var folder = Path.Combine(DataPaths.Root(), "outgoing");
        Directory.CreateDirectory(folder);
        var name = FileName;
        var path = Path.Combine(folder, name);
        await File.WriteAllBytesAsync(path, png);

        var (subject, body) = Markup.Mail(Client?.Name, Client?.ContactPerson, Page, Marks, services.XeroSettings.TenantName ?? "Webignite");
        var to = Recipients.Parse(Client?.Email);
        var mail = new OutgoingMail
        {
            AccountId = mailbox.Id,
            To = to.People,
            Subject = subject,
            Body = body + Replies.StartBody(mailbox.Signature),
            Attachments = [new OutgoingAttachment(name, png.Length, FilePath: path)],
        };
        var unwritten = Marks.Count(m => m.Kind == MarkKind.Pin && string.IsNullOrWhiteSpace(m.Comment));
        var note = to.People.Count == 0 ? "Type who it goes to in To." : unwritten > 0 ? $"{unwritten} numbered note(s) have nothing written, so they're not in the list." : null;
        var opened = services.ComposeMail?.Invoke(mail, note, null) ?? false;
        if (!opened)
            Say("Another mail is open in Mail with changes. Send or close it first.", problem: true);
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

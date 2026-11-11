using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Chat;
using Webignite.Data;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The chat window: messages between the two of you, newest at the bottom, with files and screenshots. What's typed
/// goes with Send (or Enter); files are added with Attach, by dropping them on the window, or by pasting (files copied in
/// Explorer, a screenshot or a picture copied from a web page). A file in a message opens with a click, and can be
/// copied, saved or dragged out to Explorer or another program.
/// Messages reach the other PC with the next sync, about once a minute.
/// </summary>
public sealed partial class ChatViewModel(AppServices services) : ViewModelBase, IDisposable
{
    public ObservableCollection<ChatBubble> Messages { get; } = [];
    public ObservableCollection<PendingFile> Pending { get; } = [];

    [ObservableProperty] public partial string Draft { get; set; } = "";
    [ObservableProperty] public partial string Note { get; set; } = "";
    [ObservableProperty] public partial bool NoteIsProblem { get; set; }

    /// <summary>Whether this PC is signed in to the sync server; without it there's nobody to chat with.</summary>
    public bool CanChat => services.Account.IsSignedIn;

    public string Title => $"Chat with {services.Chat.PartnerName}";

    /// <summary>The statuses I can pick, and the one picked: Busy keeps pop-ups and sounds away.</summary>
    public IReadOnlyList<PresenceState> StatusChoices => PresenceRules.Choices;

    public PresenceState MyStatus
    {
        get => services.Chat.Chosen;
        set
        {
            if (value == services.Chat.Chosen)
                return;
            services.Chat.Chosen = value;
            OnPropertyChanged();
            ShowPresence();
            services.SyncNowAsync().Forget("Telling your partner your status");
        }
    }

    /// <summary>What I show as now (Away after 10 minutes without using the PC, unless picked otherwise).</summary>
    public PresenceState MyShown => services.Chat.MyShown;

    /// <summary>What my partner shows as now, and in words: "Jaco is away".</summary>
    public PresenceState PartnerShown => services.Chat.PartnerShown;

    public string PartnerStatusText => $"{services.Chat.PartnerName} is {PresenceRules.Word(PartnerShown).ToLowerInvariant()}";

    /// <summary>Statuses read again, after a sync or a pick.</summary>
    public void ShowPresence()
    {
        OnPropertyChanged(nameof(MyShown));
        OnPropertyChanged(nameof(PartnerShown));
        OnPropertyChanged(nameof(PartnerStatusText));
    }

    public bool IsEmpty => Messages.Count == 0;

    public bool HasPending => Pending.Count > 0;

    /// <summary>Reads the messages again, keeping the pictures already shown.</summary>
    public void Refresh()
    {
        var shown = Messages.ToDictionary(m => m.Id);
        var messages = services.Chat.Messages();
        Messages.Clear();
        foreach (var message in messages)
            Messages.Add(shown.TryGetValue(message.Id, out var kept) && kept.Complete ? kept : new ChatBubble(message, services, this));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanChat));
        if (!CanChat)
            Say("Sign in under Settings, Account and sync to chat with your partner.", problem: true);
    }

    /// <summary>The window is in front: everything shown has been read.</summary>
    public void Seen()
    {
        services.Chat.MarkRead();
        services.ChatRead?.Invoke();
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                if (ChatRules.Problem(info.Name, info.Length) is { } problem)
                {
                    Say(problem, problem: true);
                    continue;
                }
                AddPending(info.Name, File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Say($"Couldn't read {Path.GetFileName(path)}: {e.Message}", problem: true);
            }
        }
    }

    /// <summary>A pasted picture, as a PNG named by the time it was pasted.</summary>
    public void AddPicture(byte[] png) => AddPending($"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png", png);

    private void AddPending(string name, byte[] data)
    {
        Pending.Add(new PendingFile(name, data, this));
        OnPropertyChanged(nameof(HasPending));
    }

    internal void Remove(PendingFile file)
    {
        Pending.Remove(file);
        OnPropertyChanged(nameof(HasPending));
    }

    [RelayCommand]
    private void Send()
    {
        if (!CanChat)
        {
            Refresh();
            return;
        }
        try
        {
            if (services.Chat.Send(Draft, [.. Pending.Select(p => (p.Name, p.Data))]) is null)
                return;
        }
        catch (InvalidOperationException e)
        {
            Say(e.Message, problem: true);
            return;
        }
        Draft = "";
        Pending.Clear();
        OnPropertyChanged(nameof(HasPending));
        Say("", problem: false);
        Refresh();
        // Sent straight away rather than waiting for the minute's sync.
        services.SyncNowAsync().Forget("Sending a chat message");
    }

    private void Say(string text, bool problem)
    {
        Note = text;
        NoteIsProblem = problem;
    }

    /// <summary>Opens a file from a message: a picture in the picture viewer, anything else in its own program.</summary>
    internal void Open(ChatFile file)
    {
        if (LocalCopy(file) is not { } path)
            return;
        try
        {
            if (ChatRules.IsPicture(file.Name))
                services.Go.Picture(path);
            else
                AppServices.OpenFile(path);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Say($"Couldn't open {file.Name}: {e.Message}", problem: true);
        }
    }

    /// <summary>
    /// The file from a message as a file on this PC (under chat-files in the data folder), to open, copy, save or drag
    /// somewhere; null, with a note, when it hasn't all arrived or can't be written.
    /// </summary>
    internal string? LocalCopy(ChatFile file)
    {
        if (services.Chat.FileBytes(file) is not { } data)
        {
            Say($"{file.Name} is still on its way. Try again in a minute.", problem: false);
            return null;
        }
        try
        {
            var folder = Path.Combine(DataPaths.Root(), "chat-files", file.Id);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, string.Join("_", file.Name.Split(Path.GetInvalidFileNameChars())));
            if (!File.Exists(path) || new FileInfo(path).Length != data.Length)
                File.WriteAllBytes(path, data);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Couldn't get {file.Name} ready: {e.Message}", problem: true);
            return null;
        }
    }

    /// <summary>A file from a message saved where the person picked.</summary>
    internal void SaveAs(ChatFile file, string target)
    {
        if (LocalCopy(file) is not { } path)
            return;
        try
        {
            File.Copy(path, target, overwrite: true);
            Say($"Saved {Path.GetFileName(target)}.", problem: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Couldn't save {file.Name}: {e.Message}", problem: true);
        }
    }

    internal void Told(string text, bool problem) => Say(text, problem);

    public void Dispose()
    {
        foreach (var bubble in Messages)
            bubble.Dispose();
    }
}

/// <summary>One message as shown: who, when, the words, and its files.</summary>
public sealed class ChatBubble : IDisposable
{
    public ChatBubble(ChatMessage message, AppServices services, ChatViewModel owner)
    {
        Id = message.Id;
        IsMine = message.From == services.Chat.MyId;
        Who = services.Chat.NameOf(message.From);
        When = ChatRules.When(message.Sent, DateTimeOffset.Now, TimeZoneInfo.Local, System.Globalization.CultureInfo.CurrentCulture);
        Text = message.Text;
        Files = [.. message.Files.Select(f => new ChatFileRow(f, services, owner))];
        Complete = Files.All(f => f.Arrived);
    }

    public string Id { get; }
    public bool IsMine { get; }
    public bool IsTheirs => !IsMine;
    public string Who { get; }
    public string When { get; }
    public string Text { get; }
    public bool HasText => Text.Length > 0;
    public IReadOnlyList<ChatFileRow> Files { get; }

    /// <summary>Every file has arrived, so this bubble needn't be made again.</summary>
    public bool Complete { get; }

    public void Dispose()
    {
        foreach (var file in Files)
            file.Dispose();
    }
}

/// <summary>A file in a message: a small picture for screenshots, else its name and size; a click opens it.</summary>
public sealed partial class ChatFileRow : IDisposable
{
    private readonly ChatFile file;
    private readonly ChatViewModel owner;

    public ChatFileRow(ChatFile file, AppServices services, ChatViewModel owner)
    {
        this.file = file;
        this.owner = owner;
        Name = file.Name;
        Size = file.Bytes < 1024 * 1024 ? $"{Math.Max(1, file.Bytes / 1024)} KB" : $"{file.Bytes / (1024d * 1024):0.0} MB";
        var data = services.Chat.FileBytes(file);
        Arrived = data is not null;
        if (data is not null && ChatRules.IsPicture(file.Name))
        {
            try
            {
                using var stream = new MemoryStream(data);
                Picture = Bitmap.DecodeToWidth(stream, 260);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                Picture = null;
            }
        }
    }

    public string Name { get; }
    public string Size { get; }
    public bool Arrived { get; }
    public string SizeText => Arrived ? Size : "On its way…";
    public Bitmap? Picture { get; }
    public bool HasPicture => Picture is not null;
    public bool IsFile => Picture is null;

    public bool IsPictureFile => ChatRules.IsPicture(Name);

    [RelayCommand]
    private void Open() => owner.Open(file);

    /// <summary>The file as one on this PC, for Copy, Save a copy and dragging it out.</summary>
    public string? LocalCopy() => owner.LocalCopy(file);

    public void SaveAs(string target) => owner.SaveAs(file, target);

    public void Dispose() => Picture?.Dispose();
}

/// <summary>A file added to the message being written, not sent yet.</summary>
public sealed partial class PendingFile(string name, byte[] data, ChatViewModel owner)
{
    public string Name { get; } = name;
    public byte[] Data { get; } = data;

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}

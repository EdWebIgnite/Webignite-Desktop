using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Text snippets: saved blocks of text, found by name or a short code, and pasted where the person was typing
// (when opened by its shortcut from another program) or copied.
public sealed partial class ToolsViewModel
{
    public ObservableCollection<SnippetRow> SnippetRows { get; } = [];

    [ObservableProperty] public partial string SnippetSearch { get; set; } = "";
    [ObservableProperty] public partial string SnippetMessage { get; set; } = "";
    [ObservableProperty] public partial bool SnippetProblem { get; set; }

    public bool NoSnippets => services.Settings.Snippets.Items.Count == 0;
    public bool NoSnippetMatches => !NoSnippets && SnippetRows.Count == 0;

    /// <summary>True when the window was opened from another program, so a snippet is pasted there and not only copied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SnippetHint))]
    public partial bool SnippetsPaste { get; set; }

    public string SnippetHint => SnippetsPaste
        ? "Type a name or code and press Enter, or click one. It is pasted where you were typing."
        : "Click one to copy it. " + (services.Settings.Screenshots.SnippetShortcut is { Length: > 0 } keys
            ? $"{keys} opens this from any program and pastes where you were typing."
            : "A shortcut to open this from any program can be set in Settings, Screenshots.");

    /// <summary>Set by the view: puts the text on the clipboard and, from another program, pastes it there. Gives what to say.</summary>
    public Func<string, Task<string>>? SendSnippet { get; set; }

    /// <summary>Reads the clipboard's text, for {clipboard}. Set by the view.</summary>
    public Func<Task<string?>>? ReadClipboard { get; set; }

    public void LoadSnippets()
    {
        SnippetRows.Clear();
        foreach (var snippet in Snippets.Find(services.Settings.Snippets.Items, SnippetSearch))
            SnippetRows.Add(new SnippetRow(snippet, this));
        OnPropertyChanged(nameof(NoSnippets));
        OnPropertyChanged(nameof(NoSnippetMatches));
    }

    partial void OnSnippetSearchChanged(string value) => LoadSnippets();

    /// <summary>Enter in the search box: the best match.</summary>
    [RelayCommand]
    private Task UseFirstSnippet() => SnippetRows.Count > 0 && !IsEditingSnippet ? UseSnippetAsync(SnippetRows[0].Snippet) : Task.CompletedTask;

    internal async Task UseSnippetAsync(Snippet snippet)
    {
        if (SendSnippet is not { } send)
            return;
        var now = DateTime.Now;
        var text = snippet.Text;
        // The clipboard is only read when the snippet asks for it.
        var copied = text.Contains("{clipboard}", StringComparison.OrdinalIgnoreCase) && ReadClipboard is { } read ? await read() : null;
        SnippetMessage = await send(Snippets.Fill(text, now.ToString("d MMMM yyyy"), now.ToString("t"), copied));
        SnippetProblem = false;
    }

    // Adding and changing

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListingSnippets), nameof(SnippetEditTitle), nameof(CanRemoveSnippet))]
    public partial bool IsEditingSnippet { get; set; }

    public bool IsListingSnippets => !IsEditingSnippet;

    [ObservableProperty] public partial string SnippetName { get; set; } = "";
    [ObservableProperty] public partial string SnippetCode { get; set; } = "";
    [ObservableProperty] public partial string SnippetText { get; set; } = "";

    private string? editingSnippet;

    public string SnippetEditTitle => editingSnippet is null ? "New snippet" : "Change the snippet";
    public bool CanRemoveSnippet => editingSnippet is not null;

    [RelayCommand]
    private void NewSnippet() => EditSnippet(null);

    internal void EditSnippet(Snippet? snippet)
    {
        editingSnippet = snippet?.Id;
        SnippetName = snippet?.Name ?? "";
        SnippetCode = snippet?.Code ?? "";
        SnippetText = snippet?.Text ?? "";
        SnippetMessage = "";
        SnippetProblem = false;
        IsEditingSnippet = true;
        OnPropertyChanged(nameof(SnippetEditTitle));
        OnPropertyChanged(nameof(CanRemoveSnippet));
    }

    [RelayCommand]
    private void SaveSnippet()
    {
        var items = services.Settings.Snippets.Items;
        if (Snippets.Problem(items, editingSnippet, SnippetName, SnippetCode, SnippetText) is { } problem)
        {
            SnippetMessage = problem;
            SnippetProblem = true;
            return;
        }
        var snippet = new Snippet(editingSnippet ?? Guid.NewGuid().ToString("N"), SnippetName, SnippetCode ?? "", SnippetText);
        services.Settings.Snippets = new SnippetSettings { Items = Snippets.Save(items, snippet) };
        CloseSnippetEditor("Saved.");
    }

    [RelayCommand]
    private void RemoveSnippet()
    {
        if (editingSnippet is not { } id)
            return;
        services.Settings.Snippets = new SnippetSettings { Items = Snippets.Remove(services.Settings.Snippets.Items, id) };
        CloseSnippetEditor("Removed.");
    }

    [RelayCommand]
    private void CancelSnippet() => CloseSnippetEditor("");

    private void CloseSnippetEditor(string message)
    {
        IsEditingSnippet = false;
        editingSnippet = null;
        SnippetMessage = message;
        SnippetProblem = false;
        LoadSnippets();
    }
}

public sealed partial class SnippetRow(Snippet snippet, ToolsViewModel tools)
{
    public Snippet Snippet { get; } = snippet;
    public string Name => Snippet.Name;
    public string Code => Snippet.Code;
    public bool HasCode => Snippet.Code.Length > 0;
    public string Preview => Snippets.Preview(Snippet.Text);

    [RelayCommand]
    private Task Use() => tools.UseSnippetAsync(Snippet);

    [RelayCommand]
    private void Change() => tools.EditSnippet(Snippet);
}

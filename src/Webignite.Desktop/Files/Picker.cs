using Avalonia;
using Avalonia.Controls;
using Webignite.Desktop.ViewModels;
using Webignite.Desktop.Views;

namespace Webignite.Desktop.Files;

/// <summary>
/// Picking files, a folder, or where to save, in the app's own explorer instead of Windows' boxes. Each opens a
/// small window over the one it was asked from and waits for it.
/// </summary>
public static class Picker
{
    /// <summary>Files to open, e.g. patterns ["*.png", "*.jpg"]. Empty when nothing was picked.</summary>
    public static async Task<IReadOnlyList<string>> Files(Visual from, string title, IReadOnlyList<string>? patterns = null,
        bool many = false, string? kindName = null, string? start = null) =>
        await Show(from, services => new FilePickerViewModel(services, PickKind.Files, title, patterns, many, kindName, start: start)) ?? [];

    /// <summary>A folder, or null when none was picked.</summary>
    public static async Task<string?> Folder(Visual from, string title, string? start = null) =>
        (await Show(from, services => new FilePickerViewModel(services, PickKind.Folder, title, start: start)))?.FirstOrDefault();

    /// <summary>Where to save a file, or null when it was cancelled. A file there already was agreed to be replaced.</summary>
    public static async Task<string?> Save(Visual from, string title, string suggestedName, IReadOnlyList<string>? patterns = null,
        string? kindName = null, string? start = null) =>
        (await Show(from, services => new FilePickerViewModel(services, PickKind.Save, title, patterns, kindName: kindName, suggestedName: suggestedName, start: start)))?.FirstOrDefault();

    private static async Task<IReadOnlyList<string>?> Show(Visual from, Func<AppServices, FilePickerViewModel> make)
    {
        if (App.Services is not { } services)
            return null;
        var picker = make(services);
        var window = new FilePickerWindow { DataContext = picker };
        if (TopLevel.GetTopLevel(from) is Window owner)
            await window.ShowDialog(owner);
        else
            window.Show();
        return await picker.Done.Task;
    }
}

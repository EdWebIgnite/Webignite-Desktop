using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Duplicate files: the copies under a folder, in sets. A search still running stops when the window closes.</summary>
public partial class DuplicatesView : UserControl
{
    private DuplicatesToolViewModel? tools;

    public DuplicatesView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        tools = DataContext as DuplicatesToolViewModel;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        tools?.LeaveDuplicates();
        tools = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void BrowseFolder(object? sender, RoutedEventArgs e) => PickAsync().Forget("Picking a folder to look through", tell: true);

    private async Task PickAsync()
    {
        if (await Picker.Folder(this, "The folder to look through") is { } folder && DataContext is DuplicatesToolViewModel model)
            model.DuplicateFolder = folder;
    }
}

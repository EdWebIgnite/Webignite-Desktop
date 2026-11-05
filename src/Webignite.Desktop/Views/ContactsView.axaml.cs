using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ContactsView : FullBleedView
{
    public ContactsView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ContactsViewModel contacts)
            contacts.CopyText = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            };
    }

    private void GroupPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ContactsViewModel contacts && GroupBox.SelectedItem is string group && group != contacts.Group)
            contacts.PickGroup(group);
    }

    // One-way, so a new list of clients arriving never changes the person's client; only a real pick does.
    private void ClientPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ContactsViewModel contacts && ClientBox.SelectedItem is Choice<Webignite.Core.Clients.Client?> choice && choice != contacts.EditClient)
            contacts.PickClient(choice);
    }

    private void PaneResized(string pane, double width) => (DataContext as ContactsViewModel)?.Panels.Resized(pane, width);
}

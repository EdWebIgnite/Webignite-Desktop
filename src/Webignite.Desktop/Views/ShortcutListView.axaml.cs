using Avalonia.Controls;

namespace Webignite.Desktop.Views;

/// <summary>The keyboard shortcuts under their headings with a search box; F1 and Settings both show it.</summary>
public partial class ShortcutListView : UserControl
{
    public ShortcutListView() => InitializeComponent();

    /// <summary>Puts the typing in the search box, for when the list has just opened.</summary>
    public void FocusSearch() => SearchBox.Focus();
}

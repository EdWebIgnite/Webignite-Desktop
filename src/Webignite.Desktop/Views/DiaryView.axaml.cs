using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The diary: two pages open like a book, a day to a page.</summary>
public partial class DiaryView : UserControl
{
    public DiaryView() => InitializeComponent();

    // Notes are kept as soon as the caret leaves them, and again when the page is turned or the app closes.
    private void KeepNotes(object? sender, RoutedEventArgs e) => (DataContext as DiaryViewModel)?.Keep();
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Core.Writing;

namespace Webignite.Desktop.Views;

/// <summary>A note widget: the Notes page's editor with a short row of formatting buttons.</summary>
public partial class NoteWidgetView : UserControl
{
    public NoteWidgetView() => InitializeComponent();

    private void Bold(object? sender, RoutedEventArgs e) => Editor.ToggleBold();
    private void Italic(object? sender, RoutedEventArgs e) => Editor.ToggleItalic();
    private void Underline(object? sender, RoutedEventArgs e) => Editor.ToggleUnderline();
    private void Bullets(object? sender, RoutedEventArgs e) => Editor.ToggleList(ListKind.Bullet);
    private void Numbers(object? sender, RoutedEventArgs e) => Editor.ToggleList(ListKind.Number);
    private void Checklist(object? sender, RoutedEventArgs e) => Editor.ToggleList(ListKind.Check);
}

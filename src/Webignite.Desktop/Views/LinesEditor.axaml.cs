using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The lines table shared by the quote, invoice and retainer editors.</summary>
public partial class LinesEditor : UserControl
{
    public static readonly StyledProperty<IEnumerable?> LinesProperty = AvaloniaProperty.Register<LinesEditor, IEnumerable?>(nameof(Lines));
    public static readonly StyledProperty<ICommand?> RemoveCommandProperty = AvaloniaProperty.Register<LinesEditor, ICommand?>(nameof(RemoveCommand));
    public static readonly StyledProperty<string> PlaceholderProperty = AvaloniaProperty.Register<LinesEditor, string>(nameof(Placeholder), "Description");

    public LinesEditor() => InitializeComponent();

    /// <summary>The <see cref="LineRowViewModel"/> rows.</summary>
    public IEnumerable? Lines { get => GetValue(LinesProperty); set => SetValue(LinesProperty, value); }

    /// <summary>Takes the row to remove.</summary>
    public ICommand? RemoveCommand { get => GetValue(RemoveCommandProperty); set => SetValue(RemoveCommandProperty, value); }

    /// <summary>Hint shown in an empty description.</summary>
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    // "3.5 x 650" becomes 2275 when the price or quantity box is left.
    private void SettleSum(object? sender, RoutedEventArgs e) => ((sender as Control)?.DataContext as LineRowViewModel)?.Settle();
}

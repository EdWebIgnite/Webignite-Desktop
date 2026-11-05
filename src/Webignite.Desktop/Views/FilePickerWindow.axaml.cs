using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The app's own Open and Save box. Closing it, however, lets go of the explorer inside it.</summary>
public partial class FilePickerWindow : Window
{
    public FilePickerWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is FilePickerViewModel { IsSave: true })
                Dispatcher.UIThread.Post(() =>
                {
                    NameBox.Focus();
                    // The name without its extension picked, ready to type over, as Windows does.
                    var name = NameBox.Text ?? "";
                    var dot = name.LastIndexOf('.');
                    NameBox.SelectionStart = 0;
                    NameBox.SelectionEnd = dot > 0 ? dot : name.Length;
                });
        };
        Closed += (_, _) => (DataContext as FilePickerViewModel)?.Dispose();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is FilePickerViewModel picker)
            {
                picker.CancelCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is FilePickerViewModel picker)
            picker.Close = Close;
    }

    private void NameKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FilePickerViewModel picker)
        {
            picker.ChooseCommand.Execute(null);
            e.Handled = true;
        }
    }
}

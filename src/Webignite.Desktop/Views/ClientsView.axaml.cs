using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ClientsView : UserControl
{
    public ClientsView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, RowPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    // Ctrl+click or a middle click on a client opens it in a tab of its own.
    private void RowPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Properties;
        if (!point.IsMiddleButtonPressed && !(point.IsLeftButtonPressed && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)))
            return;
        if ((e.Source as Avalonia.StyledElement)?.DataContext is ClientRow row && App.Services is { } services)
        {
            services.Go.ClientInNewTab(row.Client);
            e.Handled = true;
        }
    }

    private void PaneResized(string pane, double width) => (DataContext as ClientsViewModel)?.Panels.Resized(pane, width);
}

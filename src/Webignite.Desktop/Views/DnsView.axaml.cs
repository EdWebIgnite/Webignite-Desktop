using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// DNS records: look up any record, see whether a change has spread, and build the SPF and DMARC records that
/// decide whether a domain's mail is trusted.
/// </summary>
public partial class DnsView : UserControl
{
    public DnsView() => InitializeComponent();

    private ToolsViewModel? Tools => DataContext as ToolsViewModel;

    private void Copy(string? text, string what) =>
        (Tools?.CopyWebAsync(TopLevel.GetTopLevel(this), text, what) ?? Task.CompletedTask).Forget("Copying from DNS records", tell: true);

    private void CopyRow(object? sender, RoutedEventArgs e) => Copy((sender as Control)?.Tag as string, "Record");

    private void CopySpf(object? sender, RoutedEventArgs e) => Copy(Tools?.SpfRecord.Value, "SPF record");

    private void CopyDmarc(object? sender, RoutedEventArgs e) => Copy(Tools?.DmarcRecord.Value, "DMARC record");
}

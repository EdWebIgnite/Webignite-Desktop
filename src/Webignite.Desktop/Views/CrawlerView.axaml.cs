using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The site crawler's page: an address, the crawl's progress, and the table of pages with what to fix.</summary>
public partial class CrawlerView : UserControl
{
    public CrawlerView() => InitializeComponent();

    private CrawlerViewModel? Crawler => DataContext as CrawlerViewModel;

    private void SaveSpreadsheet(object? sender, RoutedEventArgs e) => SaveAsync().Forget("Saving the crawl as a spreadsheet", tell: true);

    private async Task SaveAsync()
    {
        if (Crawler is not { HasResults: true } crawler || await Picker.Save(this, "Save the crawl", crawler.CsvName, ["*.csv"], "Spreadsheet (CSV)") is not { } path)
            return;
        // With a byte-order mark, so Excel reads accents in page titles correctly.
        await File.WriteAllTextAsync(path, crawler.Csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        crawler.SavedAs(Path.GetFileName(path));
    }

    // Opens in the person's own browser, never inside the app.
    private void OpenPage(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string address && Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            AppServices.OpenInBrowser(uri).Forget("Opening the page in the browser", tell: true);
    }

    private void CopyAddress(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string address && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, address).Forget("Copying the address", tell: true);
    }
}

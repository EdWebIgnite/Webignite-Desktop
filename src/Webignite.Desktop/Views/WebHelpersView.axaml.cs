using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Small helpers for building a website: filler text, placeholder pictures, px and rem, smaller CSS and JavaScript,
/// tidy JSON, slugs and campaign links.
/// </summary>
public partial class WebHelpersView : UserControl
{
    public WebHelpersView() => InitializeComponent();

    private WebHelpersToolViewModel? Tools => DataContext as WebHelpersToolViewModel;

    private void Copy(string? text, string what) =>
        (Tools?.CopyWebAsync(TopLevel.GetTopLevel(this), text, what) ?? Task.CompletedTask).Forget("Copying from Web helpers", tell: true);

    private void CopyLorem(object? sender, RoutedEventArgs e) => Copy(Tools?.LoremText, "Filler text");

    private void CopyUnit(object? sender, RoutedEventArgs e) => Copy(Tools?.UnitAnswer, Tools?.UnitAnswer ?? "");

    private void CopyMinified(object? sender, RoutedEventArgs e) => Copy(Tools?.MinifyOutput, "Smaller code");

    private void CopyJson(object? sender, RoutedEventArgs e) => Copy(Tools?.JsonOutput, "JSON");

    private void CopyUtm(object? sender, RoutedEventArgs e) => Copy(Tools?.UtmLink, "Link");

    private void CopyCase(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TextCase one)
            Copy(one.Text, one.Name);
    }

    private void SavePlaceholder(object? sender, RoutedEventArgs e) => SavePlaceholderAsync().Forget("Saving the placeholder picture", tell: true);

    private async Task SavePlaceholderAsync()
    {
        if (Tools is not { } tools || await Picker.Save(this, "Save the placeholder picture", tools.PlaceholderName, ["*.png"], "PNG image") is not { } path)
            return;
        await File.WriteAllBytesAsync(path, tools.PlaceholderPng());
        tools.PlaceholderSaved(System.IO.Path.GetFileName(path));
    }
}

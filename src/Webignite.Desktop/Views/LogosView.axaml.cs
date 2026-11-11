using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Logo pack and favicons: pick a master logo, then save the pack as a zip or the favicon set into a folder.</summary>
public partial class LogosView : UserControl
{
    private ToolsViewModel? tools;

    public LogosView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        tools = DataContext as ToolsViewModel;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        tools?.ClearLogoPreviews();
        tools = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void ChooseLogo(object? sender, RoutedEventArgs e) => ChooseAsync().Forget("Opening a logo", tell: true);

    private async Task ChooseAsync()
    {
        var picked = (await Picker.Files(this, "The master logo", ["*.svg", "*.png", "*.jpg", "*.jpeg", "*.webp"], kindName: "Logos")).FirstOrDefault();
        if (picked is not null && DataContext is ToolsViewModel model)
            await model.OpenLogoAsync(picked);
    }

    private void SavePack(object? sender, RoutedEventArgs e) => SavePackAsync().Forget("Making the logo pack", tell: true);

    private async Task SavePackAsync()
    {
        if (DataContext is not ToolsViewModel model)
            return;
        var zip = await Picker.Save(this, "Save the logo pack", LogoPack.ZipName(model.LogoName), ["*.zip"], "Zip");
        if (zip is not null)
            await model.SaveLogoPackToCommand.ExecuteAsync(zip);
    }

    private void SaveFavicons(object? sender, RoutedEventArgs e) => SaveFaviconsAsync().Forget("Making the favicons", tell: true);

    private async Task SaveFaviconsAsync()
    {
        if (DataContext is not ToolsViewModel model)
            return;
        var folder = await Picker.Folder(this, "The folder to put the favicons in");
        if (folder is not null)
            await model.SaveFaviconsToCommand.ExecuteAsync(folder);
    }

    private void CopyHtml(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel model)
            model.CopyWebAsync(TopLevel.GetTopLevel(this), model.FaviconHtml, "The lines").Forget("Copying the favicon lines", tell: true);
    }
}

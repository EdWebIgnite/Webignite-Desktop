using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Social images: pick a picture, see how each size is cut, save them all into a folder.</summary>
public partial class SocialView : UserControl
{
    private SocialToolViewModel? tools;

    public SocialView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        tools = DataContext as SocialToolViewModel;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        tools?.ClearSocialPreviews();
        tools = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void ChoosePicture(object? sender, RoutedEventArgs e) => ChooseAsync().Forget("Opening a picture", tell: true);

    private async Task ChooseAsync()
    {
        var picked = (await Picker.Files(this, "The picture to cut", ["*.jpg", "*.jpeg", "*.png", "*.webp"], kindName: "Pictures")).FirstOrDefault();
        if (picked is not null && DataContext is SocialToolViewModel model)
            await model.OpenSocialAsync(picked);
    }

    private void SaveAll(object? sender, RoutedEventArgs e) => SaveAsync().Forget("Saving the social pictures", tell: true);

    private async Task SaveAsync()
    {
        if (DataContext is not SocialToolViewModel model)
            return;
        var folder = await Picker.Folder(this, "The folder to save the pictures in", Path.GetDirectoryName(model.SocialPath));
        if (folder is not null)
            await model.SaveSocialToCommand.ExecuteAsync(folder);
    }
}

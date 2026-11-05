using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The picture viewer: pictures opened from Files or a client's page show here instead of in another program.
/// One viewer window at a time; opening another picture shows it there and brings the window forward.
/// </summary>
public partial class MainWindow
{
    private PictureWindow? pictureWindow;

    private void WirePictures(AppServices services)
    {
        services.Go.OpenPictureAction = path => ShowPicture(path, services);
        Closing += (_, _) => pictureWindow?.Close();
    }

    /// <summary>Opens a picture in the viewer. Returns the window, for the screenshot tool.</summary>
    public PictureWindow ShowPicture(string path, AppServices services)
    {
        if (pictureWindow is { } open)
        {
            open.Picture.Show(path);
            open.Activate();
            return open;
        }
        PictureWindow? window = null;
        var picture = new PictureViewModel(services, () => window?.Close());
        window = new PictureWindow(picture);
        window.Closed += (_, _) => pictureWindow = null;
        pictureWindow = window;
        picture.Show(path);
        window.Show(this);
        return window;
    }
}

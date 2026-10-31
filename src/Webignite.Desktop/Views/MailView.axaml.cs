using Avalonia.Controls;
using TheArtOfDev.HtmlRenderer.Avalonia;

namespace Webignite.Desktop.Views;

public partial class MailView : UserControl
{
    public MailView()
    {
        InitializeComponent();
        // Links in mail open in the normal browser, never inside the app.
        Body.LinkClicked += (_, e) =>
        {
            e.Event.Handled = true;
            if (Uri.TryCreate(e.Event.Link, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" or "mailto")
                _ = AppServices.OpenInBrowser(url);
        };
    }
}

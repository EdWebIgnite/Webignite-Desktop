using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// A small window holding one tool, opened from the sidebar beside whatever page is open. Each tool has one
/// window; closing it lets go of the tool and everything it held.
/// </summary>
public sealed class ToolWindow : Window
{
    public ToolWindow(ToolKind kind, ToolsViewModel tools)
    {
        Kind = kind;
        Tools = tools;
        DataContext = tools;
        Title = TitleOf(kind);
        Width = kind switch
        {
            ToolKind.Calculator => 380,
            ToolKind.Passwords => 460,
            ToolKind.Colours => 480,
            ToolKind.Clipboard => 460,
            ToolKind.Qr => 620,
            ToolKind.WebHelpers => 720,
            ToolKind.Dns => 760,
            ToolKind.PageCapture => 1180,
            ToolKind.Ruler => 440,
            ToolKind.Snippets => 480,
            ToolKind.ScreenText => 520,
            _ => 640,
        };
        MinWidth = 340;
        // A tool that shows a web page fills a window of its own size; the others are as tall as what is in them.
        if (Fills)
        {
            Height = 800;
            MinHeight = 420;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
            MaxHeight = 860;
        }
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = this.TryFindResource("Canvas", out var canvas) && canvas is IBrush brush ? brush : Brushes.WhiteSmoke;
        Control view = kind switch
        {
            ToolKind.Calculator => new CalculatorView(),
            ToolKind.Passwords => new PasswordView(),
            ToolKind.Colours => new ColourView(),
            ToolKind.Clipboard => new ClipboardView(),
            ToolKind.Qr => new QrView(),
            ToolKind.WebHelpers => new WebHelpersView(),
            ToolKind.Dns => new DnsView(),
            ToolKind.PageCapture => new PageCaptureView(),
            ToolKind.Ruler => new RulerView(),
            ToolKind.Snippets => new SnippetsView(),
            ToolKind.ScreenText => new ScreenTextView(),
            _ => new ImagesView(),
        };
        view.Margin = new Thickness(18, 16);
        View = view;
        Content = Fills ? view : new ScrollViewer { Content = view, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    public ToolKind Kind { get; }

    /// <summary>The tool's own screen inside the window.</summary>
    public Control View { get; }

    /// <summary>True for a tool that shows a web page: it fills a big window that stands on its own.</summary>
    public bool Fills => Kind == ToolKind.PageCapture;

    public ToolsViewModel Tools { get; }

    public static string TitleOf(ToolKind kind) => kind switch
    {
        ToolKind.Calculator => "Calculator",
        ToolKind.Passwords => "Password generator",
        ToolKind.Colours => "Colours",
        ToolKind.Clipboard => "Clipboard history",
        ToolKind.Qr => "QR code",
        ToolKind.WebHelpers => "Web helpers",
        ToolKind.Dns => "DNS records",
        ToolKind.PageCapture => "Page to PDF or picture",
        ToolKind.Ruler => "Screen ruler",
        ToolKind.Snippets => "Text snippets",
        ToolKind.ScreenText => "Text from the screen",
        _ => "Website images",
    };
}

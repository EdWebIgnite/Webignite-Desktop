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
            _ => 640,
        };
        MinWidth = 340;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 860;
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
            _ => new ImagesView(),
        };
        view.Margin = new Thickness(18, 16);
        Content = new ScrollViewer { Content = view, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    public ToolKind Kind { get; }

    public ToolsViewModel Tools { get; }

    public static string TitleOf(ToolKind kind) => kind switch
    {
        ToolKind.Calculator => "Calculator",
        ToolKind.Passwords => "Password generator",
        ToolKind.Colours => "Colours",
        ToolKind.Clipboard => "Clipboard history",
        ToolKind.Qr => "QR code",
        _ => "Website images",
    };
}

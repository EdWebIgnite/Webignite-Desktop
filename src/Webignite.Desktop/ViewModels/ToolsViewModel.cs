namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The small everyday tools. Each tool is its own view model (in ViewModels/Tools), made the first time it is
/// asked for, so a window showing the calculator builds only the calculator. Closing the window (or letting My
/// pages go) frees whatever the tools that were made still hold.
/// </summary>
public sealed class ToolsViewModel(AppServices services) : IDisposable
{
    private CalculatorToolViewModel? calculator;
    private PasswordsToolViewModel? passwords;
    private ColoursToolViewModel? colours;
    private ClipboardToolViewModel? clipboard;
    private QrToolViewModel? qr;
    private ImagesToolViewModel? images;
    private WebHelpersToolViewModel? webHelpers;
    private DnsToolViewModel? dns;
    private PageCaptureToolViewModel? pageCapture;
    private BrowserToolViewModel? browser;
    private RulerToolViewModel? ruler;
    private SnippetsToolViewModel? snippets;
    private ScreenTextToolViewModel? screenText;
    private CompareToolViewModel? compare;
    private DuplicatesToolViewModel? duplicates;
    private LogosToolViewModel? logos;
    private SocialToolViewModel? social;

    public CalculatorToolViewModel Calculator => calculator ??= new(services);
    public PasswordsToolViewModel Passwords => passwords ??= new();
    public ColoursToolViewModel Colours => colours ??= new(services);
    public ClipboardToolViewModel Clipboard => clipboard ??= new(services);
    public QrToolViewModel Qr => qr ??= new();
    public ImagesToolViewModel Images => images ??= new(services);
    public WebHelpersToolViewModel WebHelpers => webHelpers ??= new();
    public DnsToolViewModel Dns => dns ??= new(services);
    public PageCaptureToolViewModel PageCapture => pageCapture ??= new(services);

    /// <summary>The Browser: tabs, the web tools as its extensions, and the device viewer.</summary>
    public BrowserToolViewModel Browser => browser ??= new(services);
    public RulerToolViewModel Ruler => ruler ??= new(services);
    public SnippetsToolViewModel Snippets => snippets ??= new(services);
    public ScreenTextToolViewModel ScreenText => screenText ??= new(services);
    public CompareToolViewModel Compare => compare ??= new();
    public DuplicatesToolViewModel Duplicates => duplicates ??= new(services);
    public LogosToolViewModel Logos => logos ??= new(services);
    public SocialToolViewModel Social => social ??= new(services);

    /// <summary>The view model behind a tool's screen.</summary>
    public ViewModelBase For(ToolKind kind) => kind switch
    {
        ToolKind.Calculator => Calculator,
        ToolKind.Passwords => Passwords,
        ToolKind.Colours => Colours,
        ToolKind.Clipboard => Clipboard,
        ToolKind.Qr => Qr,
        ToolKind.WebHelpers => WebHelpers,
        ToolKind.Dns => Dns,
        ToolKind.PageCapture => PageCapture,
        ToolKind.Browser => Browser,
        ToolKind.Ruler => Ruler,
        ToolKind.Snippets => Snippets,
        ToolKind.ScreenText => ScreenText,
        ToolKind.Compare => Compare,
        ToolKind.Duplicates => Duplicates,
        ToolKind.Logos => Logos,
        ToolKind.Social => Social,
        ToolKind.Images => Images,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This tool has no view model in ToolsViewModel.For."),
    };

    public void Dispose()
    {
        colours?.Dispose();
        clipboard?.Dispose();
        qr?.Dispose();
        pageCapture?.Dispose();
        browser?.Dispose();
        compare?.Dispose();
        duplicates?.Dispose();
        logos?.Dispose();
        social?.Dispose();
    }
}

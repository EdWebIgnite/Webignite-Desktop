using System.Text.RegularExpressions;

namespace Webignite.Tests;

/// <summary>
/// The rules in CLAUDE.md that can be checked by reading the source. A rule only written down drifts; one that
/// fails the build doesn't. Each test names the rule and lists every line that breaks it.
/// </summary>
public sealed partial class HouseRulesTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Src = Path.Combine(Root, "src");

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Webignite.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("The tests must run from inside the repository.");
    }

    private static IEnumerable<string> Files(string pattern, string? under = null) =>
        Directory.EnumerateFiles(under ?? Src, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Every line that matches, as "file:line: text", leaving out comment lines.</summary>
    private static List<string> Lines(IEnumerable<string> files, Regex breaks, Func<string, bool>? allowed = null)
    {
        var found = new List<string>();
        foreach (var file in files)
        {
            if (allowed?.Invoke(file) == true)
                continue;
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                var text = line.TrimStart();
                if (text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith("<!--", StringComparison.Ordinal))
                    continue;
                if (breaks.IsMatch(line))
                    found.Add($"{Path.GetRelativePath(Root, file)}:{number}: {text}");
            }
        }
        return found;
    }

    private static void None(List<string> found, string rule) =>
        Assert.True(found.Count == 0, rule + Environment.NewLine + string.Join(Environment.NewLine, found));

    [GeneratedRegex(@"\basync\s+void\b")]
    private static partial Regex AsyncVoid();

    [GeneratedRegex(@"^\s*_\s*=\s*.*(Async\(|Task\.(Run|Delay|WhenAll|WhenAny)\()")]
    private static partial Regex DroppedTask();

    [GeneratedRegex("FontSize=\"(\\d|1[0-3])(\\.\\d+)?\"")]
    private static partial Regex TypedSmallTextInView();

    [GeneratedRegex(@"\bFontSize\s*=\s*(\d|1[0-3])(\.\d+)?\s*[,;}]")]
    private static partial Regex TypedSmallTextInCode();

    [GeneratedRegex("\"#[0-9A-Fa-f]{3,8}\"")]
    private static partial Regex TypedColour();

    [GeneratedRegex(@"\bDateTime(Offset)?\.(Now|Today|UtcNow)\b")]
    private static partial Regex ReadsTheClock();

    [GeneratedRegex(@"\bStore\.(Save|Update|DeleteDocument|PutAsSynced|SaveClient|UpdateClient|DeleteClient)\(")]
    private static partial Regex WritesStraightToTheStore();

    [GeneratedRegex(@"<(Project|Package)Reference\b")]
    private static partial Regex Reference();

    [Fact]
    public void Dropped_tasks_go_through_Forget_never_async_void()
    {
        var files = Files("*.cs").ToList();
        None(Lines(files, AsyncVoid(), f => Path.GetFileName(f) == "Background.cs"),
            "async void fails in silence or closes the app. Use Background.Forget(\"What it was doing\").");
        None(Lines(files, DroppedTask()),
            "A task dropped with _ = fails in silence. Use Background.Forget(\"What it was doing\").");
    }

    [Fact]
    public void Small_text_is_sized_by_name_never_by_a_typed_number()
    {
        // Sizes.axaml is where the names are given their numbers, and says so in a comment.
        None(Lines(Files("*.axaml"), TypedSmallTextInView(), f => Path.GetFileName(f) == "Sizes.axaml"),
            "Small text is sized by name, FontSize=\"{DynamicResource Text12}\", so a style can change it.");
        None(Lines(Files("*.cs", Path.Combine(Src, "Webignite.Desktop")), TypedSmallTextInCode()),
            "Small text built in code is sized by name too: new TextBlock { ... }.Sized(\"Text12\").");
    }

    [Fact]
    public void Colours_come_only_from_the_palettes()
    {
        None(Lines(Files("*.axaml"), TypedColour(), f => Path.GetFileName(f).StartsWith("Palette", StringComparison.Ordinal)),
            "Colours come only from Styles/Palette*.axaml. Use a {DynamicResource ...} brush.");
    }

    [Fact]
    public void No_em_dashes_anywhere_in_the_app()
    {
        var dash = new Regex("—");
        None(Lines(Files("*.cs").Concat(Files("*.axaml")), dash), "UI copy has no em dashes. Use a comma, a colon or a full stop.");
    }

    [Fact]
    public void Core_stands_alone_and_is_told_the_time()
    {
        var core = Path.Combine(Src, "Webignite.Core");
        None(Lines(Files("*.csproj", core), Reference()),
            "Webignite.Core holds the business rules and depends on nothing, so they can be tested and reused.");
        None(Lines(Files("*.cs", core), ReadsTheClock()),
            "Rules in Core are given the date or time as a parameter, so tests can pick it.");
    }

    [Fact]
    public void Pages_change_documents_and_clients_through_the_services()
    {
        None(Lines(Files("*.cs", Path.Combine(Src, "Webignite.Desktop")), WritesStraightToTheStore()),
            "Pages read and change quotes, invoices and clients through services.Documents and services.Clients, never the store.");
    }

    [Fact]
    public void Heavy_libraries_stay_in_Tools()
    {
        string[] heavy = ["SkiaSharp", "Svg.Skia", "bblanchon.PDFium", "QRCoder", "Microsoft.Web.WebView2"];
        var found = new List<string>();
        foreach (var project in Files("*.csproj").Where(f => Path.GetFileNameWithoutExtension(f) != "Webignite.Tools"))
        {
            var text = File.ReadAllText(project);
            found.AddRange(heavy.Where(name => text.Contains($"Include=\"{name}", StringComparison.OrdinalIgnoreCase))
                .Select(name => $"{Path.GetRelativePath(Root, project)}: {name}"));
        }
        None(found, "Heavy libraries live in Webignite.Tools, which is never touched at start-up.");
    }
}

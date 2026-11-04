namespace Webignite.Core.Writing;

/// <summary>What a paragraph is: ordinary text, a heading, a quote or code.</summary>
public enum BlockStyle
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    Code,
}

public enum ListKind
{
    None,
    Bullet,
    Number,

    /// <summary>A checklist: each item has a box that can be ticked.</summary>
    Check,
}

public enum Alignment
{
    Left,
    Centre,
    Right,
}

/// <summary>The size menu, as in Thunderbird. Normal is the mail's own size.</summary>
public enum TextSize
{
    Tiny = -2,
    Small = -1,
    Normal = 0,
    Large = 1,
    ExtraLarge = 2,
    Huge = 3,
}

public static class TextSizes
{
    public static IReadOnlyList<TextSize> All { get; } = [TextSize.Tiny, TextSize.Small, TextSize.Normal, TextSize.Large, TextSize.ExtraLarge, TextSize.Huge];

    /// <summary>Points in the mail; 10.5 is what the mail body uses.</summary>
    public static double Points(TextSize size) => size switch
    {
        TextSize.Tiny => 7.5,
        TextSize.Small => 9,
        TextSize.Large => 13.5,
        TextSize.ExtraLarge => 18,
        TextSize.Huge => 24,
        _ => 10.5,
    };

    public static string Label(TextSize size) => size switch
    {
        TextSize.Tiny => "Tiny",
        TextSize.Small => "Small",
        TextSize.Large => "Large",
        TextSize.ExtraLarge => "Extra large",
        TextSize.Huge => "Huge",
        _ => "Normal",
    };

    /// <summary>One step smaller or larger, stopping at the ends.</summary>
    public static TextSize Step(TextSize size, int by) => (TextSize)Math.Clamp((int)size + by, (int)TextSize.Tiny, (int)TextSize.Huge);

    /// <summary>The nearest size to a size read from a mail, in points.</summary>
    public static TextSize Nearest(double points) => All.MinBy(s => Math.Abs(Points(s) - points));
}

/// <summary>How a stretch of text looks. Colours are #rrggbb; Link is the address it goes to.</summary>
public readonly record struct RunFormat(
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Fixed = false,
    TextSize Size = TextSize.Normal,
    string? Colour = null,
    string? Highlight = null,
    string? Link = null)
{
    /// <summary>Remove formatting: back to plain text, keeping any link.</summary>
    public RunFormat Cleared() => new(Link: Link);
}

/// <summary>Text that all looks the same. A line break inside a paragraph is "\n".</summary>
public sealed record Run(string Text, RunFormat Format);

public abstract class Block
{
    /// <summary>Part of the mailbox's signature, so changing From can swap it and sending doesn't add it again.</summary>
    public bool IsSignature { get; set; }

    public abstract Block Clone();
}

/// <summary>A paragraph, heading, list item, quote or code line.</summary>
public sealed class TextBlock : Block
{
    public BlockStyle Style { get; set; }
    public Alignment Align { get; set; }
    public ListKind List { get; set; }

    /// <summary>Steps in from the left, 0 to <see cref="MaxIndent"/>.</summary>
    public int Indent { get; set; }

    public const int MaxIndent = 8;

    /// <summary>A checklist item that is ticked off.</summary>
    public bool Checked { get; set; }

    public List<Run> Runs { get; private set; } = [];

    public TextBlock()
    {
    }

    public TextBlock(string text, RunFormat format = default)
    {
        if (text.Length > 0)
            Runs.Add(new Run(text, format));
    }

    public string Text => string.Concat(Runs.Select(r => r.Text));
    public int Length => Runs.Sum(r => r.Text.Length);

    public override TextBlock Clone() => CopyShape(Runs);

    /// <summary>Same style, list, indent and alignment, with these runs.</summary>
    public TextBlock CopyShape(IEnumerable<Run> runs)
    {
        var copy = new TextBlock { Style = Style, Align = Align, List = List, Indent = Indent, Checked = Checked, IsSignature = IsSignature };
        copy.Runs.AddRange(runs);
        return copy;
    }

    /// <summary>The runs with where each starts.</summary>
    public IEnumerable<(int Start, Run Run)> Spans()
    {
        var at = 0;
        foreach (var run in Runs)
        {
            yield return (at, run);
            at += run.Text.Length;
        }
    }

    public void Insert(int offset, string text, RunFormat format)
    {
        if (text.Length == 0)
            return;
        Runs.Insert(SplitAt(Math.Clamp(offset, 0, Length)), new Run(text, format));
        Normalise();
    }

    public void Delete(int start, int end)
    {
        (start, end) = Order(start, end);
        if (start == end)
            return;
        var first = SplitAt(start);
        var last = SplitAt(end);
        Runs.RemoveRange(first, last - first);
        Normalise();
    }

    public void Format(int start, int end, Func<RunFormat, RunFormat> change)
    {
        (start, end) = Order(start, end);
        if (start == end)
            return;
        var first = SplitAt(start);
        var last = SplitAt(end);
        for (var i = first; i < last; i++)
            Runs[i] = Runs[i] with { Format = change(Runs[i].Format) };
        Normalise();
    }

    /// <summary>The look of the text just before the offset, which is what typing there continues.</summary>
    public RunFormat FormatAt(int offset)
    {
        if (Runs.Count == 0)
            return default;
        foreach (var (start, run) in Spans())
        {
            if (offset > start && offset <= start + run.Text.Length)
                return run.Format;
        }
        return Runs[0].Format;
    }

    /// <summary>The runs between two offsets, cut to fit.</summary>
    public IReadOnlyList<Run> Slice(int start, int end)
    {
        (start, end) = Order(start, end);
        var slice = new List<Run>();
        foreach (var (at, run) in Spans())
        {
            var from = Math.Max(start, at);
            var to = Math.Min(end, at + run.Text.Length);
            if (to > from)
                slice.Add(run with { Text = run.Text[(from - at)..(to - at)] });
        }
        return slice;
    }

    /// <summary>Cuts the paragraph in two at the offset; this keeps the start, the returned one has the rest.</summary>
    public TextBlock Cut(int offset)
    {
        var index = SplitAt(Math.Clamp(offset, 0, Length));
        var tail = CopyShape(Runs.Skip(index));
        Runs.RemoveRange(index, Runs.Count - index);
        return tail;
    }

    public void Append(TextBlock other) => Append(other.Runs);

    public void Append(IEnumerable<Run> runs)
    {
        Runs.AddRange(runs);
        Normalise();
    }

    public void Clear() => Runs.Clear();

    // The index of the run that starts at offset, splitting a run when the offset falls inside it.
    private int SplitAt(int offset)
    {
        var at = 0;
        for (var i = 0; i < Runs.Count; i++)
        {
            var run = Runs[i];
            if (offset == at)
                return i;
            if (offset < at + run.Text.Length)
            {
                var cut = offset - at;
                Runs[i] = run with { Text = run.Text[..cut] };
                Runs.Insert(i + 1, run with { Text = run.Text[cut..] });
                return i + 1;
            }
            at += run.Text.Length;
        }
        return Runs.Count;
    }

    private void Normalise()
    {
        var tidy = new List<Run>(Runs.Count);
        foreach (var run in Runs)
        {
            if (run.Text.Length == 0)
                continue;
            if (tidy.Count > 0 && tidy[^1].Format == run.Format)
                tidy[^1] = tidy[^1] with { Text = tidy[^1].Text + run.Text };
            else
                tidy.Add(run);
        }
        Runs = tidy;
    }

    private (int, int) Order(int start, int end)
    {
        start = Math.Clamp(start, 0, Length);
        end = Math.Clamp(end, 0, Length);
        return start <= end ? (start, end) : (end, start);
    }
}

/// <summary>A picture on its own line, such as the signature logo.</summary>
public sealed class PictureBlock(byte[] data, string contentType, int pixelWidth, int pixelHeight) : Block
{
    /// <summary>Wider pictures are shown and sent at this width; the file itself isn't changed.</summary>
    public const int MaxWidth = 600;

    public byte[] Data { get; } = data;
    public string ContentType { get; } = contentType;
    public int PixelWidth { get; } = pixelWidth;
    public int PixelHeight { get; } = pixelHeight;
    public Alignment Align { get; set; }

    /// <summary>The width it is shown at, in pixels.</summary>
    public int Width { get; set; } = Math.Min(Math.Max(pixelWidth, 1), MaxWidth);

    public int Height => PixelWidth <= 0 ? PixelHeight : (int)Math.Round(PixelHeight * (double)Width / PixelWidth);

    public override PictureBlock Clone() => new(Data, ContentType, PixelWidth, PixelHeight) { Align = Align, Width = Width, IsSignature = IsSignature };
}

/// <summary>A horizontal line.</summary>
public sealed class RuleBlock : Block
{
    public override RuleBlock Clone() => new() { IsSignature = IsSignature };
}

/// <summary>A simple table: rows of cells, each cell one paragraph of text.</summary>
public sealed class TableBlock : Block
{
    public List<List<TextBlock>> Rows { get; } = [];

    public int Columns => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);

    public static TableBlock Empty(int rows, int columns)
    {
        var table = new TableBlock();
        for (var r = 0; r < Math.Max(rows, 1); r++)
            table.Rows.Add([.. Enumerable.Range(0, Math.Max(columns, 1)).Select(_ => new TextBlock())]);
        return table;
    }

    public override TableBlock Clone()
    {
        var copy = new TableBlock { IsSignature = IsSignature };
        foreach (var row in Rows)
            copy.Rows.Add([.. row.Select(c => c.Clone())]);
        return copy;
    }
}

/// <summary>Formatted writing: a mail, later a note. Always holds at least one block.</summary>
public sealed class RichDocument
{
    public List<Block> Blocks { get; } = [];

    public static RichDocument Empty()
    {
        var document = new RichDocument();
        document.Blocks.Add(new TextBlock());
        return document;
    }

    public RichDocument Clone()
    {
        var copy = new RichDocument();
        copy.Blocks.AddRange(Blocks.Select(b => b.Clone()));
        return copy;
    }

    /// <summary>Nothing typed: no text, pictures, tables or lines, the signature aside.</summary>
    public bool IsBlank => Blocks.Where(b => !b.IsSignature).All(b => b is TextBlock t && t.Text.Trim().Length == 0);

    public bool HasSignature => Blocks.Any(b => b.IsSignature);

    /// <summary>Makes sure there's something to put the caret in.</summary>
    public void EnsureNotEmpty()
    {
        if (Blocks.Count == 0)
            Blocks.Add(new TextBlock());
    }
}

/// <summary>
/// The colours offered for text and highlighting in a mail. These are part of what is sent, not the app's own
/// look, so they live here rather than in the palette.
/// </summary>
public static class MailColours
{
    public static IReadOnlyList<string> Text { get; } =
        ["#1f2a26", "#5f6b66", "#c0392b", "#d35400", "#b7950b", "#1e7a4c", "#1a5fb4", "#6a4c93"];

    public static IReadOnlyList<string> Highlight { get; } =
        ["#fff3a0", "#ffd8a8", "#d3f9d8", "#d0ebff", "#f3d9fa", "#e9ecef"];

    public static string Name(string colour) => colour switch
    {
        "#1f2a26" => "Black",
        "#5f6b66" => "Grey",
        "#c0392b" => "Red",
        "#d35400" => "Orange",
        "#b7950b" => "Gold",
        "#1e7a4c" => "Green",
        "#1a5fb4" => "Blue",
        "#6a4c93" => "Purple",
        "#fff3a0" => "Yellow",
        "#ffd8a8" => "Peach",
        "#d3f9d8" => "Mint",
        "#d0ebff" => "Sky",
        "#f3d9fa" => "Lilac",
        "#e9ecef" => "Grey",
        _ => colour,
    };
}

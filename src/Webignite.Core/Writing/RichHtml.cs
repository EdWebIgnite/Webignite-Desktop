using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Webignite.Core.Writing;

/// <summary>
/// Writing as mail HTML and back. Out: plain tags with inline styles, which every mail program shows the
/// same. In: that HTML again (drafts, templates) and the usual HTML other programs write, kept to what the
/// editor can show.
/// </summary>
public static partial class RichHtml
{
    public const string SignatureClass = "webignite-signature";
    public const string LinkColour = "#1a5fb4";
    private const string LineColour = "#c8cdd0";
    private const string Fixed = "Consolas, 'Courier New', monospace";

    /// <summary>
    /// The HTML for a document. <paramref name="source"/> gives each picture's src: a cid: for a mail, a data:
    /// address to keep it in one piece.
    /// </summary>
    public static string Write(RichDocument document, Func<PictureBlock, string> source)
    {
        var html = new StringBuilder();
        var inSignature = false;
        var blocks = document.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block.IsSignature != inSignature)
            {
                html.Append(block.IsSignature ? $"<div class=\"{SignatureClass}\">" : "</div>");
                inSignature = block.IsSignature;
            }
            switch (block)
            {
                case TextBlock { List: not ListKind.None }:
                    i = WriteList(html, blocks, i) - 1;
                    break;
                case TextBlock { Style: BlockStyle.Quote }:
                    html.Append("<blockquote style=\"margin:0 0 0 .8ex; border-left:2px solid ").Append(LineColour).Append("; padding-left:1ex\">");
                    while (i < blocks.Count && blocks[i] is TextBlock { Style: BlockStyle.Quote, List: ListKind.None } quote && quote.IsSignature == inSignature)
                    {
                        html.Append("<div").Append(BlockStyleAttribute(quote, "")).Append('>').Append(Runs(quote)).Append("</div>");
                        i++;
                    }
                    i--;
                    html.Append("</blockquote>");
                    break;
                case TextBlock paragraph:
                    WriteParagraph(html, paragraph);
                    break;
                case PictureBlock picture:
                    html.Append("<div style=\"margin:4px 0").Append(AlignStyle(picture.Align)).Append("\"><img src=\"")
                        .Append(WebUtility.HtmlEncode(source(picture))).Append("\" width=\"").Append(picture.Width.ToString(CultureInfo.InvariantCulture))
                        .Append("\" alt=\"\" style=\"max-width:100%\"></div>");
                    break;
                case RuleBlock:
                    html.Append("<hr style=\"border:none; border-top:1px solid ").Append(LineColour).Append("; margin:8px 0\">");
                    break;
                case TableBlock table:
                    html.Append("<table style=\"border-collapse:collapse; margin:4px 0\">");
                    foreach (var row in table.Rows)
                    {
                        html.Append("<tr>");
                        foreach (var cell in row)
                        {
                            html.Append("<td style=\"border:1px solid ").Append(LineColour).Append("; padding:4px 8px; vertical-align:top")
                                .Append(AlignStyle(cell.Align)).Append("\">").Append(cell.Length == 0 ? "&nbsp;" : Runs(cell)).Append("</td>");
                        }
                        html.Append("</tr>");
                    }
                    html.Append("</table>");
                    break;
            }
        }
        if (inSignature)
            html.Append("</div>");
        return html.ToString();
    }

    private static void WriteParagraph(StringBuilder html, TextBlock paragraph)
    {
        var (tag, style) = paragraph.Style switch
        {
            BlockStyle.Heading1 => ("h1", "margin:12px 0 4px; font-size:18pt"),
            BlockStyle.Heading2 => ("h2", "margin:10px 0 4px; font-size:15pt"),
            BlockStyle.Heading3 => ("h3", "margin:8px 0 4px; font-size:12pt"),
            BlockStyle.Code => ("pre", $"margin:0; font-family:{Fixed}; background-color:#f4f5f5; padding:2px 4px; white-space:pre-wrap"),
            _ => ("p", "margin:0"),
        };
        html.Append('<').Append(tag).Append(BlockStyleAttribute(paragraph, style)).Append('>')
            .Append(paragraph.Length == 0 ? "<br>" : Runs(paragraph)).Append("</").Append(tag).Append('>');
    }

    // Lists next to each other become one list; a deeper indent starts a list inside the last item's list.
    private static int WriteList(StringBuilder html, List<Block> blocks, int i)
    {
        var first = (TextBlock)blocks[i];
        var tag = first.List == ListKind.Number ? "ol" : "ul";
        var check = first.List == ListKind.Check;
        html.Append('<').Append(tag).Append(check ? " data-check style=\"margin:0; padding-left:4px; list-style:none" : " style=\"margin:0; padding-left:24px")
            .Append(first.Indent > 0 ? $"; margin-left:{(first.Indent * 24).ToString(CultureInfo.InvariantCulture)}px" : "").Append("\">");
        while (i < blocks.Count && blocks[i] is TextBlock item && item.List == first.List && item.Indent == first.Indent && item.IsSignature == first.IsSignature)
        {
            html.Append("<li").Append(check && item.Checked ? " data-checked" : "").Append(AlignAttribute(item.Align)).Append('>');
            // The box is written as a character too, so the list reads right in any mail program.
            if (check)
                html.Append(item.Checked ? "\u2611 " : "\u2610 ");
            html.Append(item.Length == 0 && !check ? "<br>" : Runs(item)).Append("</li>");
            i++;
        }
        html.Append("</").Append(tag).Append('>');
        return i;
    }

    private static string BlockStyleAttribute(TextBlock block, string style)
    {
        var all = style + AlignStyle(block.Align) + (block.Indent > 0 ? $"; margin-left:{(block.Indent * 24).ToString(CultureInfo.InvariantCulture)}px" : "");
        all = all.TrimStart(';', ' ');
        return all.Length == 0 ? "" : $" style=\"{all}\"";
    }

    private static string AlignAttribute(Alignment align) => align == Alignment.Left ? "" : $" style=\"{AlignStyle(align).TrimStart(';', ' ')}\"";

    private static string AlignStyle(Alignment align) => align switch
    {
        Alignment.Centre => "; text-align:center",
        Alignment.Right => "; text-align:right",
        _ => "",
    };

    /// <summary>A paragraph's runs as inline HTML.</summary>
    public static string Runs(TextBlock block)
    {
        var html = new StringBuilder();
        foreach (var run in block.Runs)
        {
            var f = run.Format;
            var text = WebUtility.HtmlEncode(run.Text).Replace("\n", "<br>");
            var styles = new List<string>();
            if (f.Colour is { } colour)
                styles.Add("color:" + colour);
            if (f.Highlight is { } highlight)
                styles.Add("background-color:" + highlight);
            if (f.Size != TextSize.Normal)
                styles.Add("font-size:" + TextSizes.Points(f.Size).ToString(CultureInfo.InvariantCulture) + "pt");
            if (f.Fixed)
                styles.Add("font-family:" + Fixed);
            if (f.Underline)
                text = "<u>" + text + "</u>";
            if (f.Italic)
                text = "<i>" + text + "</i>";
            if (f.Bold)
                text = "<b>" + text + "</b>";
            if (f.Link is { } link)
            {
                var linkStyle = f.Colour is null ? $" style=\"color:{LinkColour}\"" : "";
                if (styles.Count > 0)
                    text = $"<span style=\"{string.Join("; ", styles)}\">{text}</span>";
                html.Append("<a href=\"").Append(WebUtility.HtmlEncode(link)).Append('"').Append(linkStyle).Append('>').Append(text).Append("</a>");
            }
            else
            {
                html.Append(styles.Count > 0 ? $"<span style=\"{string.Join("; ", styles)}\">{text}</span>" : text);
            }
        }
        return html.ToString();
    }

    /// <summary>
    /// Reads HTML into writing. <paramref name="picture"/> turns an img src into its bytes and type, or null
    /// to leave the picture out (pictures on the web are never fetched).
    /// </summary>
    public static RichDocument Read(string html, Func<string, (byte[] Data, string Type)?>? picture = null)
    {
        var reader = new Reader(picture ?? DataAddress);
        reader.Run(html);
        return reader.Document;
    }

    /// <summary>A picture written into the HTML itself as a data: address.</summary>
    public static (byte[] Data, string Type)? DataAddress(string source)
    {
        var m = DataSource().Match(source);
        if (!m.Success)
            return null;
        try
        {
            return (Convert.FromBase64String(m.Groups[2].Value), m.Groups[1].Value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string ToDataAddress(PictureBlock picture) => $"data:{picture.ContentType};base64,{Convert.ToBase64String(picture.Data)}";

    private sealed class Reader(Func<string, (byte[] Data, string Type)?> picture)
    {
        public RichDocument Document { get; } = new();

        private sealed record Context(RunFormat Format, BlockStyle Style, Alignment Align, int Indent, bool Signature, bool Pre);

        private readonly Stack<(string Tag, Context Saved)> open = new();
        private readonly Stack<ListKind> lists = new();
        private Context context = new(default, BlockStyle.Paragraph, Alignment.Left, 0, false, false);
        private TextBlock? paragraph;
        private TableBlock? table;
        private TextBlock? cell;
        private int skipping;
        private bool ticked;

        public void Run(string html)
        {
            var at = 0;
            foreach (Match m in Tag().Matches(html))
            {
                if (m.Index > at)
                    Text(html[at..m.Index]);
                at = m.Index + m.Length;
                if (m.Value.StartsWith("<!", StringComparison.Ordinal) || m.Value.StartsWith("<?", StringComparison.Ordinal))
                    continue;
                var name = m.Groups["name"].Value.ToLowerInvariant();
                if (m.Groups["close"].Success)
                    Close(name);
                else
                    Open(name, Attributes(m.Groups["attrs"].Value), m.Value.EndsWith("/>", StringComparison.Ordinal));
            }
            if (at < html.Length)
                Text(html[at..]);
            EndParagraph();
            EndTable();
            Document.EnsureNotEmpty();
        }

        private static readonly HashSet<string> Skipped = ["style", "script", "head", "title", "xml"];
        private static readonly HashSet<string> Empty = ["br", "img", "hr", "meta", "link", "input", "col", "wbr"];
        private static readonly HashSet<string> Blocks = ["p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "li", "ul", "ol", "table", "tr", "td", "th", "center", "section", "article", "header", "footer"];

        private void Open(string name, Dictionary<string, string> attrs, bool selfClosing)
        {
            if (Skipped.Contains(name))
            {
                if (!selfClosing)
                    skipping++;
                return;
            }
            if (skipping > 0)
                return;
            switch (name)
            {
                case "br":
                    AddText("\n");
                    return;
                case "img":
                    Picture(attrs);
                    return;
                case "hr":
                    if (cell is null)
                    {
                        EndParagraph();
                        Document.Blocks.Add(new RuleBlock { IsSignature = context.Signature });
                    }
                    return;
            }
            if (Empty.Contains(name) || selfClosing)
                return;

            open.Push((name, context));
            if (Blocks.Contains(name))
                BlockBreak();
            var style = attrs.GetValueOrDefault("style", "");
            var f = context.Format;
            switch (name)
            {
                case "b" or "strong":
                    f = f with { Bold = true };
                    break;
                case "i" or "em" or "cite":
                    f = f with { Italic = true };
                    break;
                case "u" or "ins":
                    f = f with { Underline = true };
                    break;
                case "code" or "tt" or "kbd" or "samp":
                    f = f with { Fixed = true };
                    break;
                case "a" when attrs.GetValueOrDefault("href") is { Length: > 0 } href:
                    f = f with { Link = WebUtility.HtmlDecode(href) };
                    break;
                case "font":
                    if (attrs.GetValueOrDefault("color") is { } fontColour && Colour(fontColour) is { } fc)
                        f = f with { Colour = fc };
                    if (attrs.GetValueOrDefault("size") is { } size && int.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                        f = f with { Size = (TextSize)Math.Clamp(s - 3, -2, 3) };
                    if (attrs.GetValueOrDefault("face") is { } face && face.Contains("mono", StringComparison.OrdinalIgnoreCase))
                        f = f with { Fixed = true };
                    break;
                case "ul":
                    lists.Push(attrs.ContainsKey("data-check") ? ListKind.Check : ListKind.Bullet);
                    break;
                case "li":
                    ticked = attrs.ContainsKey("data-checked");
                    break;
                case "ol":
                    lists.Push(ListKind.Number);
                    break;
                case "table" when cell is null:
                    EndParagraph();
                    EndTable();
                    table = new TableBlock { IsSignature = context.Signature };
                    break;
                case "tr" when table is not null:
                    table.Rows.Add([]);
                    break;
                case "td" or "th" when table is not null:
                    if (table.Rows.Count == 0)
                        table.Rows.Add([]);
                    cell = new TextBlock();
                    table.Rows[^1].Add(cell);
                    if (name == "th")
                        f = f with { Bold = true };
                    break;
            }
            f = Styled(f, style);
            // A heading's or code block's own size, font and shading come with its style, not the words.
            if (name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "pre")
                f = f with { Size = context.Format.Size, Fixed = context.Format.Fixed, Highlight = context.Format.Highlight, Bold = context.Format.Bold };
            // The blue every link is written with isn't a colour someone chose.
            if (name == "a" && f.Colour == LinkColour)
                f = f with { Colour = context.Format.Colour };
            var blockStyle = name switch
            {
                "h1" => BlockStyle.Heading1,
                "h2" => BlockStyle.Heading2,
                "h3" or "h4" or "h5" or "h6" => BlockStyle.Heading3,
                "blockquote" => BlockStyle.Quote,
                "pre" => BlockStyle.Code,
                _ => context.Style,
            };
            var align = context.Align;
            if (TextAlign().Match(style) is { Success: true } a)
                align = a.Groups[1].Value.ToLowerInvariant() switch { "center" => Alignment.Centre, "right" => Alignment.Right, _ => Alignment.Left };
            else if (name == "center" || attrs.GetValueOrDefault("align")?.ToLowerInvariant() == "center")
                align = Alignment.Centre;
            else if (attrs.GetValueOrDefault("align")?.ToLowerInvariant() == "right")
                align = Alignment.Right;
            var indent = context.Indent;
            if (name is "p" or "div" && MarginLeft().Match(style) is { Success: true } margin
                && double.TryParse(margin.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
                indent = Math.Clamp(context.Indent + (int)Math.Round(px / 24), 0, TextBlock.MaxIndent);
            var signature = context.Signature || attrs.GetValueOrDefault("class", "").Split(' ').Contains(SignatureClass);
            context = new Context(f, blockStyle, align, indent, signature, context.Pre || name == "pre");
        }

        private void Close(string name)
        {
            if (Skipped.Contains(name))
            {
                skipping = Math.Max(0, skipping - 1);
                return;
            }
            if (skipping > 0 || !open.Any(o => o.Tag == name))
                return;
            while (open.Count > 0)
            {
                var (tag, saved) = open.Pop();
                if (Blocks.Contains(tag))
                    BlockBreak();
                if (tag is "ul" or "ol" && lists.Count > 0)
                    lists.Pop();
                if (tag is "td" or "th")
                    cell = null;
                if (tag == "table")
                    EndTable();
                context = saved;
                if (tag == name)
                    break;
            }
        }

        private void Text(string raw)
        {
            if (skipping > 0)
                return;
            var text = WebUtility.HtmlDecode(raw);
            if (!context.Pre)
            {
                text = Spaces().Replace(text, " ");
                var current = cell ?? paragraph;
                if (current is null || current.Length == 0 || current.Text.EndsWith('\n') || current.Text.EndsWith(' '))
                    text = text.TrimStart();
                if (text.Length == 0)
                    return;
            }
            AddText(text.Replace(" ", " "));
        }

        private void AddText(string text)
        {
            if (text.Length == 0)
                return;
            if (table is not null)
            {
                if (cell is null)
                    return; // stray text between cells
                cell.Insert(cell.Length, text, context.Format);
                return;
            }
            paragraph ??= StartParagraph();
            paragraph.Insert(paragraph.Length, text, context.Format);
        }

        private TextBlock StartParagraph()
        {
            var block = new TextBlock
            {
                Style = context.Style,
                Align = context.Align,
                IsSignature = context.Signature,
                List = open.Any(o => o.Tag == "li") && lists.Count > 0 ? lists.Peek() : ListKind.None,
                Indent = Math.Clamp(context.Indent + Math.Max(0, lists.Count - 1), 0, TextBlock.MaxIndent),
            };
            block.Checked = block.List == ListKind.Check && ticked;
            Document.Blocks.Add(block);
            return block;
        }

        // A block tag inside a table cell is a line break; elsewhere it ends the paragraph.
        private void BlockBreak()
        {
            if (cell is not null)
            {
                if (cell.Length > 0 && !cell.Text.EndsWith('\n'))
                    cell.Insert(cell.Length, "\n", default);
                return;
            }
            EndParagraph();
        }

        private void EndParagraph()
        {
            if (paragraph is null)
                return;
            // A final <br> doesn't make a line of its own; it's how an empty paragraph is written.
            var text = paragraph.Text;
            if (text.EndsWith('\n'))
                paragraph.Delete(text.Length - 1, text.Length);
            // A checklist item's box was written as a character for other mail programs; the item has its own.
            if (paragraph.List == ListKind.Check && paragraph.Text is ['\u2610' or '\u2611', ..] boxed)
                paragraph.Delete(0, boxed.Length > 1 && boxed[1] == ' ' ? 2 : 1);
            var trimmed = paragraph.Text;
            var end = trimmed.Length;
            while (end > 0 && trimmed[end - 1] == ' ')
                end--;
            if (end < trimmed.Length)
                paragraph.Delete(end, trimmed.Length);
            paragraph = null;
        }

        private void EndTable()
        {
            if (table is null)
                return;
            foreach (var c in table.Rows.SelectMany(r => r))
            {
                var text = c.Text;
                if (text.EndsWith('\n'))
                    c.Delete(text.Length - 1, text.Length);
            }
            table.Rows.RemoveAll(r => r.Count == 0);
            if (table.Rows.Count > 0)
            {
                var columns = table.Columns;
                foreach (var row in table.Rows)
                {
                    while (row.Count < columns)
                        row.Add(new TextBlock());
                }
                Document.Blocks.Add(table);
            }
            table = null;
            cell = null;
        }

        private void Picture(Dictionary<string, string> attrs)
        {
            if (cell is not null || attrs.GetValueOrDefault("src") is not { Length: > 0 } src || picture(WebUtility.HtmlDecode(src)) is not { } found)
                return;
            var size = ImageSize.Of(found.Data);
            var block = new PictureBlock(found.Data, found.Type, size?.Width ?? 0, size?.Height ?? 0) { Align = context.Align, IsSignature = context.Signature };
            if (attrs.GetValueOrDefault("width") is { } w && int.TryParse(w.TrimEnd('p', 'x'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0)
                block.Width = Math.Min(width, PictureBlock.MaxWidth);
            EndParagraph();
            Document.Blocks.Add(block);
        }

        private static RunFormat Styled(RunFormat f, string style)
        {
            if (style.Length == 0)
                return f;
            foreach (var part in style.Split(';'))
            {
                var colon = part.IndexOf(':');
                if (colon < 0)
                    continue;
                var key = part[..colon].Trim().ToLowerInvariant();
                var value = part[(colon + 1)..].Trim();
                switch (key)
                {
                    case "color" when Colour(value) is { } c:
                        f = f with { Colour = c };
                        break;
                    case "background-color" or "background" when Colour(value) is { } h && h != "#ffffff":
                        f = f with { Highlight = h };
                        break;
                    case "font-weight":
                        f = f with { Bold = value is "bold" or "bolder" or "600" or "700" or "800" or "900" };
                        break;
                    case "font-style":
                        f = f with { Italic = value == "italic" };
                        break;
                    case "text-decoration" or "text-decoration-line":
                        if (value.Contains("underline", StringComparison.OrdinalIgnoreCase))
                            f = f with { Underline = true };
                        break;
                    case "font-family":
                        if (value.Contains("mono", StringComparison.OrdinalIgnoreCase) || value.Contains("consolas", StringComparison.OrdinalIgnoreCase)
                            || value.Contains("courier", StringComparison.OrdinalIgnoreCase))
                            f = f with { Fixed = true };
                        break;
                    case "font-size" when FontSize(value) is { } points:
                        f = f with { Size = TextSizes.Nearest(points) };
                        break;
                }
            }
            return f;
        }

        private static double? FontSize(string value)
        {
            var m = SizeValue().Match(value);
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                return value.ToLowerInvariant() switch { "small" => 9, "x-small" => 7.5, "large" => 13.5, "x-large" => 18, "xx-large" => 24, _ => null };
            return m.Groups[2].Value.ToLowerInvariant() switch
            {
                "px" => n * 0.75,
                "em" or "rem" => n * 10.5,
                "%" => n / 100 * 10.5,
                _ => n,
            };
        }

        private static Dictionary<string, string> Attributes(string text)
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Attribute().Matches(text))
                attrs[m.Groups[1].Value] = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value;
            return attrs;
        }
    }

    /// <summary>A CSS or HTML colour as #rrggbb, or null when it can't be read.</summary>
    public static string? Colour(string value)
    {
        value = value.Trim().ToLowerInvariant();
        if (HexColour().Match(value) is { Success: true } hex)
        {
            var digits = hex.Groups[1].Value;
            return digits.Length == 3 ? $"#{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}" : "#" + digits;
        }
        if (RgbColour().Match(value) is { Success: true } rgb)
        {
            static int Part(string s) => Math.Clamp(int.Parse(s, CultureInfo.InvariantCulture), 0, 255);
            return $"#{Part(rgb.Groups[1].Value):x2}{Part(rgb.Groups[2].Value):x2}{Part(rgb.Groups[3].Value):x2}";
        }
        return value switch
        {
            "black" => "#000000",
            "white" => "#ffffff",
            "red" => "#ff0000",
            "green" => "#008000",
            "blue" => "#0000ff",
            "yellow" => "#ffff00",
            "grey" or "gray" => "#808080",
            "orange" => "#ffa500",
            "purple" => "#800080",
            _ => null,
        };
    }

    [GeneratedRegex(@"<!--.*?-->|<!\[CDATA\[.*?\]\]>|<[!?][^>]*>|<(?<close>/)?(?<name>[a-zA-Z][a-zA-Z0-9:-]*)(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Singleline)]
    private static partial Regex Tag();

    [GeneratedRegex(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)(?:\s*=\s*(""([^""]*)""|'([^']*)'|([^\s""'>]+)))?")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"text-align\s*:\s*(left|center|right|justify)", RegexOptions.IgnoreCase)]
    private static partial Regex TextAlign();

    [GeneratedRegex(@"margin-left\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase)]
    private static partial Regex MarginLeft();

    [GeneratedRegex(@"^([\d.]+)\s*(pt|px|em|rem|%)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SizeValue();

    [GeneratedRegex(@"^#([0-9a-f]{6}|[0-9a-f]{3})$")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)")]
    private static partial Regex RgbColour();

    [GeneratedRegex(@"^data:(image/[a-z+.-]+);base64,(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DataSource();
}

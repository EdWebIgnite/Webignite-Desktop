using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Webignite.Core.Tools;

// The small helpers of the Web helpers tool. Everything here makes text that goes into a website, so numbers are
// written the way CSS and web addresses want them, whatever the PC's region.

public enum LoremKind
{
    Paragraphs,
    Sentences,
    Words,
}

/// <summary>Filler text for a design. The same request always gives the same words, so a page doesn't jump about.</summary>
public static class LoremIpsum
{
    public const string Opening = "Lorem ipsum dolor sit amet, consectetur adipiscing elit.";

    private static readonly string[] Pool =
        ("lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna " +
         "aliqua enim ad minim veniam quis nostrud exercitation ullamco laboris nisi aliquip ex ea commodo consequat duis aute " +
         "irure in reprehenderit voluptate velit esse cillum fugiat nulla pariatur excepteur sint occaecat cupidatat non " +
         "proident sunt culpa qui officia deserunt mollit anim id est laborum vitae sapien pellentesque habitant morbi " +
         "tristique senectus netus fames turpis egestas integer feugiat scelerisque varius nunc pulvinar mattis").Split(' ');

    public static int Most(LoremKind kind) => kind switch
    {
        LoremKind.Paragraphs => 20,
        LoremKind.Sentences => 60,
        _ => 1000,
    };

    public static string Make(LoremKind kind, int count, bool classicStart = true)
    {
        count = Math.Clamp(count, 1, Most(kind));
        var next = new Picker();
        switch (kind)
        {
            case LoremKind.Words:
                var words = new List<string>();
                if (classicStart)
                    words.AddRange(Pool.Take(Math.Min(count, 8)));
                while (words.Count < count)
                    words.Add(next.Word());
                words[0] = Capital(words[0]);
                return string.Join(' ', words);
            case LoremKind.Sentences:
                return string.Join(' ', Sentences(next, count, classicStart));
            default:
                var paragraphs = new List<string>();
                for (var p = 0; p < count; p++)
                    paragraphs.Add(string.Join(' ', Sentences(next, 4 + next.Below(4), classicStart && p == 0)));
                return string.Join("\n\n", paragraphs);
        }
    }

    private static List<string> Sentences(Picker next, int count, bool classicStart)
    {
        var sentences = new List<string>();
        if (classicStart)
            sentences.Add(Opening);
        while (sentences.Count < count)
        {
            var length = 6 + next.Below(10);
            var comma = length > 9 ? 3 + next.Below(length - 6) : -1;
            var text = new StringBuilder();
            for (var w = 0; w < length; w++)
            {
                var word = next.Word();
                text.Append(w == 0 ? Capital(word) : word);
                text.Append(w == length - 1 ? "." : w == comma ? ", " : " ");
            }
            sentences.Add(text.ToString());
        }
        return sentences;
    }

    private static string Capital(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    // A fixed sequence rather than chance, so the text is the same every time.
    private sealed class Picker
    {
        private uint state = 20240229;
        private string last = "";

        public int Below(int limit)
        {
            state = unchecked(state * 1664525 + 1013904223);
            return (int)((state >> 8) % (uint)limit);
        }

        public string Word()
        {
            var word = Pool[Below(Pool.Length)];
            if (word == last)
                word = Pool[Below(Pool.Length)];
            return last = word;
        }
    }
}

/// <summary>Pixels to rem and back, for a stylesheet.</summary>
public static class CssUnits
{
    public const double UsualBase = 16;

    /// <summary>
    /// "24" or "24px" gives "1.5rem"; "1.5rem" or "1.5em" gives "24px". Null when what was typed isn't a size.
    /// </summary>
    public static string? Convert(string? typed, double basePx = UsualBase)
    {
        if (basePx <= 0 || Read(typed) is not var (number, unit))
            return null;
        return unit == "px" ? Number(number / basePx) + "rem" : Number(number * basePx) + "px";
    }

    /// <summary>The same size written out in full, e.g. "24px = 1.5rem".</summary>
    public static string? Sum(string? typed, double basePx = UsualBase)
    {
        if (Convert(typed, basePx) is not { } result || Read(typed) is not var (number, unit))
            return null;
        return $"{Number(number)}{unit} = {result}";
    }

    /// <summary>A base size as typed ("16", "18px", "17,5"), or null when it isn't one.</summary>
    public static double? Base(string? typed) => Read(typed) is var (number, unit) && unit == "px" && number > 0 ? number : null;

    private static (double Number, string Unit)? Read(string? typed)
    {
        var text = (typed ?? "").Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        var unit = "px";
        foreach (var ending in (string[])["rem", "em", "px"])
            if (text.EndsWith(ending, StringComparison.Ordinal))
            {
                unit = ending;
                text = text[..^ending.Length];
                break;
            }
        // A comma is how many regions type a decimal; CSS itself only ever has a point.
        text = text.Replace(',', '.');
        return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)
            ? (number, unit)
            : null;
    }

    private static string Number(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>
/// Makes CSS and JavaScript smaller to send. Careful rather than clever: it takes out comments and space that can't
/// matter and never renames or rewrites anything, so what comes out runs as what went in.
/// </summary>
public static class Minify
{
    /// <summary>Comments and needless space out of a stylesheet. Comments starting /*! (licences) stay.</summary>
    public static string Css(string? css)
    {
        var text = css ?? "";
        var result = new StringBuilder(text.Length);
        var space = false;
        var kept = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                if (i + 2 < text.Length && text[i + 2] == '!')
                {
                    result.Append(text, i, end - i);
                    kept = result.Length;
                    space = false;
                }
                else
                    space = true;
                i = end - 1;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }
            // Nothing is needed between a comment that was kept and what follows it.
            var last = result.Length > kept ? result[^1] : '\0';
            if (c == '}' && last == ';')
                result.Length--;
            else if (space && last != '\0' && !"{};,:".Contains(last) && !"{};,".Contains(c))
                result.Append(' ');
            space = false;
            if (c is '"' or '\'')
            {
                i = Quoted(text, i, result);
                continue;
            }
            result.Append(c);
            // An unquoted url( ) is copied as it is: it may hold // or spaces that mean something.
            if (c == '(' && result.Length >= 4 && result.ToString(result.Length - 4, 4).Equals("url(", StringComparison.OrdinalIgnoreCase))
            {
                var close = text.IndexOf(')', i + 1);
                if (close > i)
                {
                    result.Append(text.AsSpan(i + 1, close - i - 1).Trim());
                    i = close - 1;
                }
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Comments, indents and empty lines out of a script. Line breaks between statements stay, as JavaScript reads
    /// meaning into them; what is inside quotes, back-ticks and patterns is left alone.
    /// </summary>
    public static string Js(string? js)
    {
        var text = (js ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var result = new StringBuilder(text.Length);
        // How many { are open inside each ${ } of the back-tick strings we are inside.
        var templates = new Stack<int>();
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var following = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '\n')
            {
                NewLine(result);
                continue;
            }
            if (c is ' ' or '\t')
            {
                if (result.Length > 0 && result[^1] is not ('\n' or ' '))
                    result.Append(' ');
                continue;
            }
            if (c == '/' && following == '/')
            {
                var end = text.IndexOf('\n', i);
                i = (end < 0 ? text.Length : end) - 1;
                continue;
            }
            if (c == '/' && following == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                if (i + 2 < text.Length && text[i + 2] == '!')
                    result.Append(text, i, end - i);
                else if (text.AsSpan(i, end - i).Contains('\n'))
                    NewLine(result);
                else if (result.Length > 0 && result[^1] is not ('\n' or ' '))
                    result.Append(' ');
                i = end - 1;
                continue;
            }
            if (c is '"' or '\'')
            {
                i = Quoted(text, i, result);
                continue;
            }
            if (c == '`')
            {
                i = Template(text, i + 1, result.Append('`'), templates, ref depth);
                continue;
            }
            if (c == '/' && StartsPattern(result))
            {
                i = Pattern(text, i, result);
                continue;
            }
            if (c == '{')
                depth++;
            else if (c == '}')
            {
                if (depth == 0 && templates.Count > 0)
                {
                    // The end of a ${ }: back into the back-tick string it was in.
                    depth = templates.Pop();
                    i = Template(text, i + 1, result.Append('}'), templates, ref depth);
                    continue;
                }
                depth = Math.Max(0, depth - 1);
            }
            result.Append(c);
        }
        return result.ToString().Trim();
    }

    /// <summary>How much smaller, as a whole percentage; 0 when nothing was saved.</summary>
    public static int Saved(string? before, string? after)
    {
        var was = (before ?? "").Length;
        var now = (after ?? "").Length;
        return was == 0 || now >= was ? 0 : (int)Math.Round(100.0 * (was - now) / was);
    }

    private static void NewLine(StringBuilder result)
    {
        while (result.Length > 0 && result[^1] == ' ')
            result.Length--;
        if (result.Length > 0 && result[^1] != '\n')
            result.Append('\n');
    }

    // Copies a quoted string whole and returns where it ended.
    private static int Quoted(string text, int start, StringBuilder result)
    {
        var quote = text[start];
        var i = start;
        result.Append(quote);
        for (i++; i < text.Length; i++)
        {
            result.Append(text[i]);
            if (text[i] == '\\' && i + 1 < text.Length)
                result.Append(text[++i]);
            else if (text[i] == quote || text[i] == '\n')
                break;
        }
        return i;
    }

    // Copies a back-tick string from just inside it, up to its end or the start of a ${ }, and returns where it stopped.
    private static int Template(string text, int start, StringBuilder result, Stack<int> templates, ref int depth)
    {
        for (var i = start; i < text.Length; i++)
        {
            result.Append(text[i]);
            if (text[i] == '\\' && i + 1 < text.Length)
                result.Append(text[++i]);
            else if (text[i] == '`')
                return i;
            else if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                result.Append('{');
                templates.Push(depth);
                depth = 0;
                return i + 1;
            }
        }
        return text.Length;
    }

    // A / starts a pattern (a regular expression) where a value is expected, and is a divide after one.
    private static bool StartsPattern(StringBuilder result)
    {
        var end = result.Length;
        while (end > 0 && result[end - 1] is ' ' or '\n')
            end--;
        if (end == 0)
            return true;
        var last = result[end - 1];
        if ("(,=:[!&|?{};+-*%<>~^".Contains(last))
            return true;
        if (!char.IsLetter(last))
            return false;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(result[start - 1]) || result[start - 1] is '_' or '$'))
            start--;
        return result.ToString(start, end - start) is "return" or "typeof" or "case" or "in" or "of" or "delete" or "void" or "throw" or "new" or "else" or "do";
    }

    private static int Pattern(string text, int start, StringBuilder result)
    {
        var inClass = false;
        var i = start;
        result.Append('/');
        for (i++; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
                return i - 1;
            result.Append(c);
            if (c == '\\' && i + 1 < text.Length && text[i + 1] != '\n')
                result.Append(text[++i]);
            else if (c == '[')
                inClass = true;
            else if (c == ']')
                inClass = false;
            else if (c == '/' && !inClass)
                break;
        }
        return i;
    }
}

public sealed record JsonTidied(string Text, string? Problem)
{
    public bool Ok => Problem is null;
}

/// <summary>JSON laid out to read, or on one line to send. Says where it is broken when it is.</summary>
public static class JsonTidy
{
    public static JsonTidied Tidy(string? json, bool oneLine = false, int indent = 2)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JsonTidied("", null);
        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            var options = new JsonWriterOptions
            {
                Indented = !oneLine,
                IndentSize = Math.Clamp(indent, 1, 8),
                // Left as typed: accents and the like aren't turned into \u codes.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                NewLine = "\n",
            };
            using (var writer = new Utf8JsonWriter(stream, options))
                document.RootElement.WriteTo(writer);
            return new JsonTidied(Encoding.UTF8.GetString(stream.ToArray()), null);
        }
        catch (JsonException e)
        {
            var where = e.LineNumber is { } line
                ? string.Create(CultureInfo.InvariantCulture, $" Look at line {line + 1}, about {(e.BytePositionInLine ?? 0) + 1} characters in.")
                : "";
            return new JsonTidied("", "This isn't valid JSON yet." + where);
        }
    }
}

public sealed record TextCase(string Name, string Text);

/// <summary>The same words written every way a website needs them: a slug for the address, names for code.</summary>
public static class TextCases
{
    private static readonly HashSet<string> Small = ["a", "an", "and", "as", "at", "but", "by", "for", "in", "of", "on", "or", "the", "to", "vs", "with"];

    public static IReadOnlyList<TextCase> All(string? text)
    {
        var typed = (text ?? "").Trim();
        return typed.Length == 0
            ? []
            :
            [
                new("Slug", Slug(typed)),
                new("Title Case", Title(typed)),
                new("Sentence case", Sentence(typed)),
                new("lower case", typed.ToLowerInvariant()),
                new("UPPER CASE", typed.ToUpperInvariant()),
                new("camelCase", Camel(typed)),
                new("PascalCase", Pascal(typed)),
                new("snake_case", string.Join('_', Words(typed))),
                new("kebab-case", string.Join('-', Words(typed))),
            ];
    }

    /// <summary>What goes in a web address: lower case, accents off, hyphens for everything else.</summary>
    public static string Slug(string? text)
    {
        var slug = new StringBuilder();
        foreach (var c in Plain(text ?? "").ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                slug.Append(c);
            else if (c is '\'' or '’')
                continue;
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }
        return slug.ToString().Trim('-');
    }

    public static string Title(string? text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var lower = words[i].ToLowerInvariant();
            words[i] = i > 0 && i < words.Length - 1 && Small.Contains(lower) ? lower : Capital(lower);
        }
        return string.Join(' ', words);
    }

    public static string Sentence(string? text)
    {
        var chars = (text ?? "").ToLowerInvariant().ToCharArray();
        var start = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (start && char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                start = false;
            }
            else if (chars[i] is '.' or '!' or '?' or '\n')
                start = true;
        }
        return new string(chars);
    }

    public static string Camel(string? text)
    {
        var words = Words(text);
        return string.Concat(words.Select((w, i) => i == 0 ? w : Capital(w)));
    }

    public static string Pascal(string? text) => string.Concat(Words(text).Select(Capital));

    /// <summary>The words in lower case without accents, split at spaces, punctuation and the humps of camelCase.</summary>
    public static IReadOnlyList<string> Words(string? text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var plain = Plain(text ?? "");
        for (var i = 0; i < plain.Length; i++)
        {
            var c = plain[i];
            if (!char.IsAsciiLetterOrDigit(c))
            {
                if (c is not ('\'' or '’'))
                    End();
                continue;
            }
            // A hump: "siteName" is two words, and so is the end of "HTMLPage" before "Page".
            if (char.IsAsciiLetterUpper(c) && word.Length > 0
                && (char.IsAsciiLetterLower(plain[i - 1]) || char.IsAsciiDigit(plain[i - 1])
                    || (i + 1 < plain.Length && char.IsAsciiLetterLower(plain[i + 1]) && char.IsAsciiLetterUpper(plain[i - 1]))))
                End();
            word.Append(char.ToLowerInvariant(c));
        }
        End();
        return words;

        void End()
        {
            if (word.Length > 0)
                words.Add(word.ToString());
            word.Clear();
        }
    }

    private static string Capital(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    // Accents off: "Café Süd" reads "Cafe Sud".
    private static string Plain(string text)
    {
        var plain = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                plain.Append(c);
        return plain.ToString().Normalize(NormalizationForm.FormC);
    }
}

public sealed record UtmLink(string Link, string? Problem)
{
    public bool Ok => Problem is null;
}

/// <summary>A link with campaign tags on, so Google Analytics can say which advert, mail or post a visit came from.</summary>
public static class UtmLinks
{
    public static UtmLink Build(string? address, string? source, string? medium, string? campaign, string? term = null, string? content = null)
    {
        var typed = (address ?? "").Trim();
        if (typed.Length == 0)
            return new UtmLink("", null);
        if (!typed.Contains("://", StringComparison.Ordinal))
            typed = "https://" + typed;
        if (!Uri.TryCreate(typed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.'))
            return new UtmLink("", "That doesn't look like a web address.");
        if (string.IsNullOrWhiteSpace(source))
            return new UtmLink("", "Add a source, such as newsletter or facebook: Analytics needs one.");

        var hash = typed.IndexOf('#', StringComparison.Ordinal);
        var fragment = hash < 0 ? "" : typed[hash..];
        var rest = hash < 0 ? typed : typed[..hash];
        var question = rest.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? rest : rest[..question];
        // Tags already on the address are replaced, anything else in it is kept.
        var query = (question < 0 ? "" : rest[(question + 1)..])
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Add("utm_source", source);
        Add("utm_medium", medium);
        Add("utm_campaign", campaign);
        Add("utm_term", term);
        Add("utm_content", content);
        if (uri.AbsolutePath == "/" && !path.EndsWith('/'))
            path += "/";
        return new UtmLink(path + "?" + string.Join('&', query) + fragment, null);

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                query.Add(name + "=" + Uri.EscapeDataString(value.Trim()));
        }
    }
}

/// <summary>A plain picture of a set size to stand in for one that is still to come.</summary>
public static class Placeholders
{
    public const int Most = 4000;
    public const string UsualColour = "#D0D5DD";

    public static (int Width, int Height) Size(int width, int height) => (Math.Clamp(width, 1, Most), Math.Clamp(height, 1, Most));

    /// <summary>What is written on it when nothing else is typed: its size.</summary>
    public static string Label(int width, int height, string? typed = null)
    {
        var (w, h) = Size(width, height);
        return string.IsNullOrWhiteSpace(typed) ? string.Create(CultureInfo.InvariantCulture, $"{w} × {h}") : typed.Trim();
    }

    public static string FileName(int width, int height)
    {
        var (w, h) = Size(width, height);
        return string.Create(CultureInfo.InvariantCulture, $"placeholder-{w}x{h}.png");
    }

    /// <summary>Black or white writing, whichever reads better on the colour.</summary>
    public static Rgb Ink(Rgb background)
    {
        var black = new Rgb(0, 0, 0);
        var white = new Rgb(255, 255, 255);
        return Colours.Contrast(background, black) >= Colours.Contrast(background, white) ? black : white;
    }

    /// <summary>How big the writing can be to fit across the picture with room either side.</summary>
    public static double FontSize(int width, int height, string label)
    {
        var (w, h) = Size(width, height);
        var across = w * 0.8 / Math.Max(1, label.Length) / 0.55;
        return Math.Max(1, Math.Min(across, h * 0.4));
    }
}

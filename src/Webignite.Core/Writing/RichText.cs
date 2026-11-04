using System.Text;
using System.Text.RegularExpressions;
using Webignite.Core.Mail;

namespace Webignite.Core.Writing;

/// <summary>
/// Plain text into writing and back. Text in can carry the light marks older mails and templates use:
/// **bold**, *italic*, [words](link), "- " and "1. " lists, and bare web addresses.
/// </summary>
public static partial class RichText
{
    public static RichDocument FromText(string? text)
    {
        var document = new RichDocument();
        foreach (var line in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var (kind, item) = MailFormat.ListItem(line);
            var block = new TextBlock { List = kind switch { "ul" => ListKind.Bullet, "ol" => ListKind.Number, _ => ListKind.None } };
            block.Append(Inline(kind is null ? line : item));
            document.Blocks.Add(block);
        }
        document.EnsureNotEmpty();
        return document;
    }

    /// <summary>The marks in one line turned into formatted runs.</summary>
    public static IReadOnlyList<Run> Inline(string text)
    {
        var runs = new List<Run>();
        var at = 0;
        foreach (Match m in Marks().Matches(text))
        {
            if (m.Index > at)
                runs.Add(new Run(text[at..m.Index], default));
            if (m.Groups["words"].Success)
            {
                runs.Add(new Run(m.Groups["words"].Value, new RunFormat(Link: m.Groups["url"].Value)));
            }
            else if (m.Groups["bold"].Success)
            {
                runs.Add(new Run(m.Groups["bold"].Value, new RunFormat(Bold: true)));
            }
            else if (m.Groups["italic"].Success)
            {
                runs.Add(new Run(m.Groups["italic"].Value, new RunFormat(Italic: true)));
            }
            else
            {
                var url = m.Value.TrimEnd('.', ',', ')', ';', ':', '!', '?');
                runs.Add(new Run(url, new RunFormat(Link: url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + url : url)));
                if (url.Length < m.Value.Length)
                    runs.Add(new Run(m.Value[url.Length..], default));
            }
            at = m.Index + m.Length;
        }
        if (at < text.Length)
            runs.Add(new Run(text[at..], default));
        return runs;
    }

    /// <summary>The plain-text version: lists keep their marks, links show their address, tables go row by row.</summary>
    public static string ToText(RichDocument document)
    {
        var text = new StringBuilder();
        var number = 0;
        var numberIndent = -1;
        foreach (var block in document.Blocks)
        {
            if (text.Length > 0)
                text.Append('\n');
            switch (block)
            {
                case TextBlock paragraph:
                    if (paragraph.List == ListKind.Number)
                    {
                        number = numberIndent == paragraph.Indent ? number + 1 : 1;
                        numberIndent = paragraph.Indent;
                    }
                    else
                    {
                        numberIndent = -1;
                    }
                    var pad = new string(' ', paragraph.Indent * 2);
                    var mark = paragraph.List switch
                    {
                        ListKind.Bullet => "- ",
                        ListKind.Number => $"{number}. ",
                        _ => paragraph.Style == BlockStyle.Quote ? "> " : "",
                    };
                    text.Append(pad).Append(mark).Append(Words(paragraph).Replace("\n", "\n" + pad + new string(' ', mark.Length)));
                    break;
                case TableBlock table:
                    numberIndent = -1;
                    text.AppendJoin('\n', table.Rows.Select(row => string.Join(" | ", row.Select(c => Words(c).Replace('\n', ' ')))));
                    break;
                case RuleBlock:
                    numberIndent = -1;
                    text.Append("----------");
                    break;
                default:
                    numberIndent = -1;
                    // Pictures have no words; the line stays so the spacing reads the same.
                    break;
            }
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>A paragraph's words, with each link's address after its words when they differ.</summary>
    public static string Words(TextBlock block)
    {
        var text = new StringBuilder();
        foreach (var run in block.Runs)
        {
            text.Append(run.Text);
            if (run.Format.Link is { } link)
            {
                var shown = link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? link[7..] : link;
                if (!string.Equals(shown, run.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(shown, "https://" + run.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                    text.Append(" (").Append(shown).Append(')');
            }
        }
        return text.ToString();
    }

    /// <summary>A mailbox's signature as writing: its text, then its picture.</summary>
    public static IReadOnlyList<Block> Signature(string? text, byte[]? picture, string? pictureType)
    {
        var blocks = new List<Block>();
        if (!string.IsNullOrWhiteSpace(text))
            blocks.AddRange(FromText(text.TrimEnd()).Blocks);
        if (picture is { Length: > 0 } && ImageSize.Of(picture) is { } size)
            blocks.Add(new PictureBlock(picture, pictureType ?? size.ContentType, size.Width, size.Height));
        foreach (var block in blocks)
            block.IsSignature = true;
        return blocks;
    }

    /// <summary>A new mail's writing: the text, a blank line and the signature, or just room to type.</summary>
    public static RichDocument Starting(string? text, IReadOnlyList<Block> signature)
    {
        var document = string.IsNullOrEmpty(text?.Trim()) ? RichDocument.Empty() : FromText(text.TrimEnd());
        if (signature.Count > 0)
        {
            // A blank line between the writing (or the empty line to type on) and the signature.
            document.Blocks.Add(new TextBlock());
            RichEditing.InsertSignature(document, signature);
        }
        return document;
    }

    [GeneratedRegex(@"\[(?<words>[^\]\n]+)\]\((?<url>(?:https?://|mailto:)[^\s)]+)\)|\*\*(?=\S)(?<bold>.+?)(?<=\S)\*\*|(?<![*\w])\*(?=[^\s*])(?<italic>.+?)(?<=[^\s*])\*(?![*\w])|\b(?:https?://|www\.)[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex Marks();
}

/// <summary>A picture's size and type read from the start of the file (PNG, JPEG, GIF), without opening it.</summary>
public static class ImageSize
{
    public sealed record Size(int Width, int Height, string ContentType);

    public static Size? Of(byte[] bytes)
    {
        try
        {
            if (bytes is [0x89, (byte)'P', (byte)'N', (byte)'G', ..] && bytes.Length >= 24)
                return new(BigEndian(bytes, 16), BigEndian(bytes, 20), "image/png");
            if (bytes is [(byte)'G', (byte)'I', (byte)'F', ..] && bytes.Length >= 10)
                return new(bytes[6] | (bytes[7] << 8), bytes[8] | (bytes[9] << 8), "image/gif");
            if (bytes is [0xFF, 0xD8, ..])
                return Jpeg(bytes);
        }
        catch (IndexOutOfRangeException)
        {
        }
        return null;
    }

    private static Size? Jpeg(byte[] bytes)
    {
        var i = 2;
        while (i + 9 < bytes.Length)
        {
            if (bytes[i] != 0xFF)
            {
                i++;
                continue;
            }
            var marker = bytes[i + 1];
            var length = (bytes[i + 2] << 8) | bytes[i + 3];
            // Start-of-frame markers carry the size (C4, C8 and CC are something else).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return new((bytes[i + 7] << 8) | bytes[i + 8], (bytes[i + 5] << 8) | bytes[i + 6], "image/jpeg");
            i += 2 + length;
        }
        return null;
    }

    private static int BigEndian(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
}

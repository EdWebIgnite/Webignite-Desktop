using Webignite.Core.Writing;
using TextBlock = Webignite.Core.Writing.TextBlock;

namespace Webignite.Desktop.Controls;

/// <summary>What the toolbar asks for, and what it shows: the look and paragraph at the caret.</summary>
public sealed partial class RichTextEditor
{
    /// <summary>How the text at the caret or at the start of the selection looks.</summary>
    public RunFormat CurrentFormat => pending ?? (Document is null ? default : RichEditing.FormatOf(Document, Selection.Start, Selection.End));

    /// <summary>The paragraph the caret is in, or null on a picture or line.</summary>
    public TextBlock? CurrentParagraph => Document is null ? null : RichEditing.Slots(Document)[caret.Slot].Text;

    public bool InTable => Document is not null && RichEditing.Slots(Document)[caret.Slot].InTable;

    /// <summary>The selected words, or empty.</summary>
    public string SelectedText => Document is null || !HasSelection ? "" : RichText.ToText(RichEditing.Copy(Document, Selection.Start, Selection.End));

    public void ToggleBold() => Look(f => f with { Bold = !CurrentFormat.Bold });
    public void ToggleItalic() => Look(f => f with { Italic = !CurrentFormat.Italic });
    public void ToggleUnderline() => Look(f => f with { Underline = !CurrentFormat.Underline });
    public void SetFixed(bool on) => Look(f => f with { Fixed = on });
    public void SetSize(TextSize size) => Look(f => f with { Size = size });
    public void StepSize(int by) => Look(f => f with { Size = TextSizes.Step(f.Size, by) });

    /// <summary>Text colour, or null for the normal colour.</summary>
    public void SetColour(string? colour) => Look(f => f with { Colour = colour });

    public void SetHighlight(string? colour) => Look(f => f with { Highlight = colour });

    /// <summary>Back to plain text. With nothing selected, the word typed next is plain.</summary>
    public void ClearFormatting() => Look(f => f.Cleared());

    // With a selection the change applies to it; without, it applies to what is typed next.
    private void Look(Func<RunFormat, RunFormat> change)
    {
        if (Document is null)
            return;
        if (!HasSelection)
        {
            pending = change(CurrentFormat);
            Relayout();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Focus();
            return;
        }
        Restyle((d, s, e) => RichEditing.Format(d, s, e, change));
        Focus();
    }

    public void SetStyle(BlockStyle style)
    {
        Restyle((d, s, e) => RichEditing.Paragraphs(d, s, e, b =>
        {
            b.Style = style;
            if (style != BlockStyle.Paragraph)
                b.List = ListKind.None;
        }));
        Focus();
    }

    public void ToggleList(ListKind kind)
    {
        Restyle((d, s, e) => RichEditing.ToggleList(d, s, e, kind));
        Focus();
    }

    public void Indent(int by)
    {
        Restyle((d, s, e) => RichEditing.Indent(d, s, e, by));
        Focus();
    }

    public void Align(Alignment align)
    {
        Restyle((d, s, e) => RichEditing.Align(d, s, e, align));
        Focus();
    }

    /// <summary>Makes the selection a link, or types the address as a link when nothing is selected.</summary>
    public void SetLink(string address)
    {
        if (Document is null)
            return;
        var url = Links.Normalise(address);
        if (url.Length == 0)
            return;
        if (HasSelection)
        {
            Restyle((d, s, e) => RichEditing.Format(d, s, e, f => f with { Link = url }));
        }
        else
        {
            var words = Links.Words(url);
            var format = CurrentFormat with { Link = url };
            Change(d => RichEditing.Insert(d, caret, words, format));
        }
        Focus();
    }

    public void RemoveLink()
    {
        Restyle((d, s, e) => RichEditing.Format(d, s, e, f => f with { Link = null }));
        Focus();
    }

    /// <summary>Types text where the caret is, such as an emoji.</summary>
    public void InsertText(string text)
    {
        if (Document is null)
            return;
        Type(text);
        Focus();
    }

    /// <summary>Puts prepared writing in where the caret is, such as a saved reply.</summary>
    public void InsertDocument(RichDocument piece)
    {
        Change(d => RichEditing.InsertDocument(d, HasSelection ? RichEditing.Delete(d, Selection.Start, Selection.End) : caret, piece));
        Focus();
    }

    public void InsertPicture(byte[] data, string contentType, int width, int height)
    {
        Change(d => RichEditing.InsertBlock(d, caret, new PictureBlock(data, contentType, width, height)));
        Focus();
    }

    public void InsertTable(int rows, int columns)
    {
        Change(d => RichEditing.InsertBlock(d, caret, TableBlock.Empty(rows, columns)));
        Focus();
    }

    public void InsertRule()
    {
        Change(d => RichEditing.InsertBlock(d, caret, new RuleBlock()));
        Focus();
    }

    /// <summary>A row or column added to the table the caret is in.</summary>
    public void AddTableRow()
    {
        if (Document is null || RichEditing.Slots(Document)[caret.Slot] is not { Table: { } table, Row: var row })
            return;
        Change(d =>
        {
            table.Rows.Insert(row + 1, [.. Enumerable.Range(0, table.Columns).Select(_ => new TextBlock())]);
            return RichEditing.Find(d, table.Rows[row + 1][0], 0);
        });
    }

    public void AddTableColumn()
    {
        if (Document is null || RichEditing.Slots(Document)[caret.Slot] is not { Table: { } table, Column: var column })
            return;
        Change(d =>
        {
            foreach (var r in table.Rows)
                r.Insert(Math.Min(column + 1, r.Count), new TextBlock());
            return caret;
        });
    }

    /// <summary>Puts the caret at the very start, ready to type.</summary>
    public void StartTyping()
    {
        MoveTo(RichEditing.Start, extend: false);
        Focus();
    }
}

/// <summary>Turning what was typed into a link address, and back into words to show.</summary>
public static class Links
{
    public static string Normalise(string address)
    {
        var url = address.Trim();
        if (url.Length == 0)
            return "";
        if (!url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            url = url.Contains('@') && !url.Contains('/') ? "mailto:" + url : "https://" + url;
        return url;
    }

    public static string Words(string url) =>
        url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? url[7..] : url.Replace("https://", "", StringComparison.OrdinalIgnoreCase);
}

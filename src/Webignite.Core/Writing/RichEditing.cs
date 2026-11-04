using System.Globalization;

namespace Webignite.Core.Writing;

/// <summary>
/// A place the caret can be: a paragraph or table cell (offset is a character), or a picture or line
/// (offset 0 is before it, 1 after). Slots are numbered in reading order, table cells row by row.
/// </summary>
public readonly record struct DocPosition(int Slot, int Offset) : IComparable<DocPosition>
{
    public int CompareTo(DocPosition other) => Slot != other.Slot ? Slot.CompareTo(other.Slot) : Offset.CompareTo(other.Offset);

    public static bool operator <(DocPosition a, DocPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(DocPosition a, DocPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(DocPosition a, DocPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DocPosition a, DocPosition b) => a.CompareTo(b) >= 0;

    public static DocPosition Min(DocPosition a, DocPosition b) => a <= b ? a : b;
    public static DocPosition Max(DocPosition a, DocPosition b) => a >= b ? a : b;
}

/// <summary>One place the caret can go. <see cref="Top"/> is the index of the top-level block it is in.</summary>
public sealed record Slot(Block Block, int Top, TableBlock? Table = null, int Row = -1, int Column = -1)
{
    public TextBlock? Text => Block as TextBlock;
    public bool IsAtom => Block is not TextBlock;
    public bool InTable => Table is not null;
    public int Length => Block is TextBlock t ? t.Length : 1;
}

/// <summary>Every change the editor makes, kept apart from drawing so it can be tested.</summary>
public static class RichEditing
{
    public static IReadOnlyList<Slot> Slots(RichDocument document)
    {
        var slots = new List<Slot>();
        for (var i = 0; i < document.Blocks.Count; i++)
        {
            if (document.Blocks[i] is TableBlock table)
            {
                for (var r = 0; r < table.Rows.Count; r++)
                {
                    for (var c = 0; c < table.Rows[r].Count; c++)
                        slots.Add(new Slot(table.Rows[r][c], i, table, r, c));
                }
            }
            else
            {
                slots.Add(new Slot(document.Blocks[i], i));
            }
        }
        return slots;
    }

    public static DocPosition Start => new(0, 0);

    public static DocPosition End(RichDocument document)
    {
        var slots = Slots(document);
        return slots.Count == 0 ? Start : new(slots.Count - 1, slots[^1].Length);
    }

    /// <summary>The position kept inside the document, for after a change.</summary>
    public static DocPosition Clamp(RichDocument document, DocPosition position)
    {
        document.EnsureNotEmpty();
        var slots = Slots(document);
        var slot = Math.Clamp(position.Slot, 0, slots.Count - 1);
        return new(slot, Math.Clamp(position.Offset, 0, slots[slot].Length));
    }

    /// <summary>Where a block now is, at the given offset.</summary>
    public static DocPosition Find(RichDocument document, Block block, int offset)
    {
        var slots = Slots(document);
        for (var i = 0; i < slots.Count; i++)
        {
            if (ReferenceEquals(slots[i].Block, block))
                return new(i, Math.Clamp(offset, 0, slots[i].Length));
        }
        return Clamp(document, new(0, 0));
    }

    // Typing

    /// <summary>Types text at the position. Line breaks start new paragraphs, except inside a table cell.</summary>
    public static DocPosition Insert(RichDocument document, DocPosition at, string text, RunFormat format)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        at = Clamp(document, at);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                at = Slots(document)[at.Slot].InTable ? InsertRaw(document, at, "\n", format) : Enter(document, at);
            at = InsertRaw(document, at, lines[i], format);
        }
        return at;
    }

    private static DocPosition InsertRaw(RichDocument document, DocPosition at, string text, RunFormat format)
    {
        var slot = Slots(document)[at.Slot];
        if (slot.Text is { } block)
        {
            block.Insert(at.Offset, text, format);
            return at with { Offset = at.Offset + text.Length };
        }
        if (text.Length == 0)
            return at;
        // Typing beside a picture or line makes a paragraph for the text.
        var paragraph = new TextBlock(text, format);
        document.Blocks.Insert(slot.Top + (at.Offset > 0 ? 1 : 0), paragraph);
        return Find(document, paragraph, text.Length);
    }

    /// <summary>Shift+Enter: a new line inside the same paragraph or cell.</summary>
    public static DocPosition LineBreak(RichDocument document, DocPosition at, RunFormat format) =>
        InsertRaw(document, Clamp(document, at), "\n", format);

    /// <summary>Enter: a new paragraph. In an empty list item it ends the list; in a cell it is a line break.</summary>
    public static DocPosition Enter(RichDocument document, DocPosition at)
    {
        at = Clamp(document, at);
        var slot = Slots(document)[at.Slot];
        if (slot.InTable)
            return InsertRaw(document, at, "\n", slot.Text!.FormatAt(at.Offset));
        if (slot.Text is not { } block)
        {
            var paragraph = new TextBlock { IsSignature = slot.Block.IsSignature };
            document.Blocks.Insert(slot.Top + (at.Offset > 0 ? 1 : 0), paragraph);
            return at.Offset > 0 ? Find(document, paragraph, 0) : Find(document, slot.Block, 0);
        }
        if (block.List != ListKind.None && block.Length == 0)
        {
            block.List = ListKind.None;
            return at;
        }
        var tail = block.Cut(at.Offset);
        // A new checklist item starts unticked.
        tail.Checked = false;
        if (block.Style is BlockStyle.Heading1 or BlockStyle.Heading2 or BlockStyle.Heading3 && tail.Length == 0)
            tail.Style = BlockStyle.Paragraph;
        document.Blocks.Insert(slot.Top + 1, tail);
        return Find(document, tail, 0);
    }

    /// <summary>Backspace with nothing selected.</summary>
    public static DocPosition Backspace(RichDocument document, DocPosition at)
    {
        at = Clamp(document, at);
        var slots = Slots(document);
        var slot = slots[at.Slot];
        if (slot.Text is { } block)
        {
            if (at.Offset > 0)
            {
                var from = PreviousCharacter(block.Text, at.Offset);
                block.Delete(from, at.Offset);
                return at with { Offset = from };
            }
            if (block.List != ListKind.None)
            {
                block.List = ListKind.None;
                return at;
            }
            if (block.Indent > 0)
            {
                block.Indent--;
                return at;
            }
            if (at.Slot == 0 || slot.InTable)
                return at;
            var previous = slots[at.Slot - 1];
            if (previous.InTable)
            {
                if (block.Length != 0)
                    return at;
                document.Blocks.Remove(block);
                return Clamp(document, new(at.Slot - 1, previous.Length));
            }
            if (previous.Text is { } before)
            {
                var join = before.Length;
                before.Append(block);
                document.Blocks.Remove(block);
                return Find(document, before, join);
            }
            document.Blocks.Remove(previous.Block);
            return Find(document, block, 0);
        }
        if (at.Offset == 1)
            return RemoveAtom(document, slot);
        return at.Slot == 0 ? at : new(at.Slot - 1, slots[at.Slot - 1].Length);
    }

    /// <summary>Delete with nothing selected.</summary>
    public static DocPosition DeleteForward(RichDocument document, DocPosition at)
    {
        at = Clamp(document, at);
        var slots = Slots(document);
        var slot = slots[at.Slot];
        if (slot.Text is { } block)
        {
            if (at.Offset < block.Length)
            {
                block.Delete(at.Offset, NextCharacter(block.Text, at.Offset));
                return at;
            }
            if (at.Slot + 1 >= slots.Count || slot.InTable)
                return at;
            var next = slots[at.Slot + 1];
            if (next.InTable)
                return at;
            if (next.Text is { } after)
            {
                block.Append(after);
                document.Blocks.Remove(after);
                return at;
            }
            document.Blocks.Remove(next.Block);
            return Clamp(document, at);
        }
        return at.Offset == 0 ? RemoveAtom(document, slot) : at.Slot + 1 < slots.Count ? new(at.Slot + 1, 0) : at;
    }

    private static DocPosition RemoveAtom(RichDocument document, Slot atom)
    {
        var top = atom.Top;
        document.Blocks.RemoveAt(top);
        document.EnsureNotEmpty();
        if (top < document.Blocks.Count)
            return Find(document, document.Blocks[top] is TableBlock t ? t.Rows[0][0] : document.Blocks[top], 0);
        var last = document.Blocks[^1];
        return Find(document, last is TableBlock table ? table.Rows[^1][^1] : last, int.MaxValue);
    }

    /// <summary>Removes what is between two positions and returns where the caret goes.</summary>
    public static DocPosition Delete(RichDocument document, DocPosition start, DocPosition end)
    {
        (start, end) = (Clamp(document, DocPosition.Min(start, end)), Clamp(document, DocPosition.Max(start, end)));
        if (start == end)
            return start;
        var slots = Slots(document);
        var first = slots[start.Slot];
        var last = slots[end.Slot];
        if (start.Slot == end.Slot)
        {
            if (first.Text is { } only)
            {
                only.Delete(start.Offset, end.Offset);
                return start;
            }
            return RemoveAtom(document, first);
        }

        var remove = new List<Block>();
        if (first.Text is { } head)
            head.Delete(start.Offset, head.Length);
        else if (start.Offset == 0)
            remove.Add(first.Block);
        if (last.Text is { } tail)
            tail.Delete(0, end.Offset);
        else if (end.Offset == 1)
            remove.Add(last.Block);

        for (var i = start.Slot + 1; i < end.Slot; i++)
        {
            var middle = slots[i];
            if (middle.Table is { } table)
            {
                // A whole table inside the selection goes; a partly selected one only loses its text.
                if (middle.Top != first.Top && middle.Top != last.Top)
                {
                    if (!remove.Contains(table))
                        remove.Add(table);
                }
                else
                {
                    middle.Text!.Clear();
                }
            }
            else
            {
                remove.Add(middle.Block);
            }
        }

        var merged = false;
        if (first.Text is { } a && last.Text is { } b && !first.InTable && !last.InTable)
        {
            a.Append(b);
            remove.Add(b);
            merged = true;
        }
        foreach (var block in remove)
            document.Blocks.Remove(block);
        document.EnsureNotEmpty();

        if (document.Blocks.Contains(first.Table ?? first.Block))
            return Find(document, first.Block, first.IsAtom ? 1 : start.Offset);
        if (!merged && document.Blocks.Contains(last.Table ?? last.Block))
            return Find(document, last.Block, 0);
        return Clamp(document, new(start.Slot, 0));
    }

    // Formatting

    public static RunFormat FormatAt(RichDocument document, DocPosition at)
    {
        at = Clamp(document, at);
        return Slots(document)[at.Slot].Text?.FormatAt(at.Offset) ?? default;
    }

    /// <summary>How the selection looks, taken from its first character, as the toolbar shows it.</summary>
    public static RunFormat FormatOf(RichDocument document, DocPosition start, DocPosition end)
    {
        (start, end) = (DocPosition.Min(start, end), DocPosition.Max(start, end));
        if (start == end)
            return FormatAt(document, start);
        var slots = Slots(document);
        for (var i = start.Slot; i <= end.Slot && i < slots.Count; i++)
        {
            if (slots[i].Text is not { } block)
                continue;
            var from = i == start.Slot ? start.Offset : 0;
            var to = i == end.Slot ? end.Offset : block.Length;
            if (to > from)
                return block.FormatAt(from + 1);
        }
        return FormatAt(document, start);
    }

    /// <summary>Changes the look of the text in the selection.</summary>
    public static void Format(RichDocument document, DocPosition start, DocPosition end, Func<RunFormat, RunFormat> change)
    {
        (start, end) = (DocPosition.Min(start, end), DocPosition.Max(start, end));
        var slots = Slots(document);
        for (var i = start.Slot; i <= end.Slot && i < slots.Count; i++)
        {
            if (slots[i].Text is not { } block)
                continue;
            block.Format(i == start.Slot ? start.Offset : 0, i == end.Slot ? end.Offset : block.Length, change);
        }
    }

    /// <summary>The paragraphs and pictures the selection touches (table cells included).</summary>
    public static IReadOnlyList<Block> BlocksIn(RichDocument document, DocPosition start, DocPosition end)
    {
        (start, end) = (DocPosition.Min(start, end), DocPosition.Max(start, end));
        var slots = Slots(document);
        var blocks = new List<Block>();
        for (var i = Math.Max(start.Slot, 0); i <= end.Slot && i < slots.Count; i++)
        {
            // A selection that ends at the very start of a paragraph doesn't take that paragraph.
            if (i == end.Slot && i > start.Slot && end.Offset == 0)
                break;
            blocks.Add(slots[i].Block);
        }
        return blocks;
    }

    /// <summary>Sets style, list, alignment or indent on every paragraph the selection touches.</summary>
    public static void Paragraphs(RichDocument document, DocPosition start, DocPosition end, Action<TextBlock> change)
    {
        foreach (var block in BlocksIn(document, start, end).OfType<TextBlock>())
            change(block);
    }

    /// <summary>A list on or off. Already that kind everywhere turns it off.</summary>
    public static void ToggleList(RichDocument document, DocPosition start, DocPosition end, ListKind kind)
    {
        var blocks = BlocksIn(document, start, end).OfType<TextBlock>().ToList();
        var off = blocks.Count > 0 && blocks.All(b => b.List == kind);
        foreach (var block in blocks)
        {
            block.List = off ? ListKind.None : kind;
            block.Checked = false;
            if (!off && block.Style != BlockStyle.Paragraph)
                block.Style = BlockStyle.Paragraph;
        }
    }

    public static void Indent(RichDocument document, DocPosition start, DocPosition end, int by)
    {
        foreach (var block in BlocksIn(document, start, end).OfType<TextBlock>())
            block.Indent = Math.Clamp(block.Indent + by, 0, TextBlock.MaxIndent);
    }

    public static void Align(RichDocument document, DocPosition start, DocPosition end, Alignment align)
    {
        foreach (var block in BlocksIn(document, start, end))
        {
            if (block is TextBlock text)
                text.Align = align;
            else if (block is PictureBlock picture)
                picture.Align = align;
        }
    }

    // Pictures, lines, tables and pasting

    /// <summary>Puts a picture, line or table in at the caret, on its own line. Returns where typing carries on.</summary>
    public static DocPosition InsertBlock(RichDocument document, DocPosition at, Block block)
    {
        at = Clamp(document, at);
        var slot = Slots(document)[at.Slot];
        if (slot.InTable || slot.IsAtom)
        {
            var place = slot.Top + (slot.IsAtom && at.Offset == 0 ? 0 : 1);
            document.Blocks.Insert(place, block);
            return AfterBlock(document, place);
        }
        var paragraph = slot.Text!;
        var top = slot.Top;
        if (paragraph.Length == 0)
        {
            document.Blocks.Insert(top, block);
            return Find(document, paragraph, 0);
        }
        if (at.Offset == 0)
        {
            document.Blocks.Insert(top, block);
            return Find(document, paragraph, 0);
        }
        if (at.Offset < paragraph.Length)
        {
            var tail = paragraph.Cut(at.Offset);
            document.Blocks.Insert(top + 1, tail);
        }
        document.Blocks.Insert(top + 1, block);
        return AfterBlock(document, top + 1);
    }

    // The paragraph after a newly placed block, made if there isn't one.
    private static DocPosition AfterBlock(RichDocument document, int index)
    {
        if (index + 1 >= document.Blocks.Count || document.Blocks[index + 1] is not TextBlock)
            document.Blocks.Insert(index + 1, new TextBlock());
        var placed = document.Blocks[index];
        return placed is TableBlock table ? Find(document, table.Rows[0][0], 0) : Find(document, document.Blocks[index + 1], 0);
    }

    /// <summary>A copy of the selection, for the clipboard.</summary>
    public static RichDocument Copy(RichDocument document, DocPosition start, DocPosition end)
    {
        (start, end) = (DocPosition.Min(start, end), DocPosition.Max(start, end));
        var copy = new RichDocument();
        var slots = Slots(document);
        for (var i = start.Slot; i <= end.Slot && i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.Text is { } block)
            {
                var from = i == start.Slot ? start.Offset : 0;
                var to = i == end.Slot ? end.Offset : block.Length;
                var piece = block.CopyShape(block.Slice(from, to));
                piece.IsSignature = false;
                copy.Blocks.Add(piece);
            }
            else if ((i != start.Slot || start.Offset == 0) && (i != end.Slot || end.Offset == 1))
            {
                var whole = slot.Block.Clone();
                whole.IsSignature = false;
                copy.Blocks.Add(whole);
            }
        }
        copy.EnsureNotEmpty();
        return copy;
    }

    /// <summary>Puts copied or prepared writing in at the caret. Returns the caret after it.</summary>
    public static DocPosition InsertDocument(RichDocument document, DocPosition at, RichDocument piece)
    {
        at = Clamp(document, at);
        var blocks = piece.Blocks.Select(b => b.Clone()).ToList();
        if (blocks.Count == 0)
            return at;
        var slot = Slots(document)[at.Slot];
        if (blocks.All(b => b is TextBlock) && (blocks.Count == 1 || slot.InTable))
        {
            // Words into a paragraph or cell keep their look but take the paragraph's style.
            var runs = new List<Run>();
            for (var i = 0; i < blocks.Count; i++)
            {
                if (i > 0)
                    runs.Add(new Run("\n", default));
                runs.AddRange(((TextBlock)blocks[i]).Runs);
            }
            if (slot.Text is { } target)
            {
                var offset = at.Offset;
                foreach (var run in runs)
                {
                    target.Insert(offset, run.Text, run.Format);
                    offset += run.Text.Length;
                }
                return at with { Offset = offset };
            }
        }
        if (slot.InTable || slot.IsAtom)
        {
            var place = slot.Top + (slot.IsAtom && at.Offset == 0 ? 0 : 1);
            document.Blocks.InsertRange(place, blocks);
            var lastPlaced = blocks[^1];
            return lastPlaced is TextBlock t ? Find(document, t, t.Length) : AfterBlock(document, place + blocks.Count - 1);
        }

        var paragraph = slot.Text!;
        var tail = paragraph.Cut(at.Offset);
        var top = slot.Top;
        if (blocks[0] is TextBlock first)
        {
            paragraph.Append(first);
            blocks.RemoveAt(0);
        }
        if (blocks.Count == 0)
        {
            var join = paragraph.Length;
            paragraph.Append(tail);
            return Find(document, paragraph, join);
        }
        Block caretBlock;
        int caretOffset;
        if (blocks[^1] is TextBlock last)
        {
            caretBlock = last;
            caretOffset = last.Length;
            last.Append(tail);
        }
        else
        {
            // Ends with a picture, line or table: the rest of the paragraph follows on its own line.
            blocks.Add(tail);
            caretBlock = tail;
            caretOffset = 0;
        }
        document.Blocks.InsertRange(top + 1, blocks);
        if (paragraph.Length == 0 && !ReferenceEquals(caretBlock, paragraph) && blocks[0] is not TextBlock)
            document.Blocks.Remove(paragraph);
        return Find(document, caretBlock, caretOffset);
    }

    // Signature

    /// <summary>Takes the signature out. Returns where it was, or -1 when there wasn't one.</summary>
    public static int RemoveSignature(RichDocument document)
    {
        var at = document.Blocks.FindIndex(b => b.IsSignature);
        if (at < 0)
            return -1;
        document.Blocks.RemoveAll(b => b.IsSignature);
        document.EnsureNotEmpty();
        return at;
    }

    /// <summary>Puts a signature in at a block index, or at the end.</summary>
    public static void InsertSignature(RichDocument document, IReadOnlyList<Block> signature, int at = -1)
    {
        if (signature.Count == 0)
            return;
        foreach (var block in signature)
            block.IsSignature = true;
        document.Blocks.InsertRange(at < 0 || at > document.Blocks.Count ? document.Blocks.Count : at, signature);
    }

    /// <summary>Swaps the signature for another, keeping its place in the mail. Returns true when one was there.</summary>
    public static bool SwapSignature(RichDocument document, IReadOnlyList<Block> signature)
    {
        var at = RemoveSignature(document);
        if (at < 0)
            return false;
        InsertSignature(document, signature, at);
        return true;
    }

    // Words and characters

    /// <summary>Where the character before the offset starts, keeping emoji and other pairs whole.</summary>
    public static int PreviousCharacter(string text, int offset)
    {
        if (offset <= 0)
            return 0;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var previous = 0;
        foreach (var start in starts)
        {
            if (start >= offset)
                break;
            previous = start;
        }
        return previous;
    }

    public static int NextCharacter(string text, int offset)
    {
        foreach (var start in StringInfo.ParseCombiningCharacters(text))
        {
            if (start > offset)
                return start;
        }
        return text.Length;
    }

    /// <summary>The start of the word at or before the offset, for Ctrl+Left and double-click.</summary>
    public static int WordStart(string text, int offset)
    {
        var i = Math.Clamp(offset, 0, text.Length);
        while (i > 0 && char.IsWhiteSpace(text[i - 1]))
            i--;
        while (i > 0 && IsWordCharacter(text[i - 1]))
            i--;
        if (i == offset && i > 0)
            i--;
        return i;
    }

    /// <summary>The end of the word at or after the offset, for Ctrl+Right and double-click.</summary>
    public static int WordEnd(string text, int offset)
    {
        var i = Math.Clamp(offset, 0, text.Length);
        while (i < text.Length && IsWordCharacter(text[i]))
            i++;
        if (i == offset && i < text.Length)
            i++;
        while (i < text.Length && char.IsWhiteSpace(text[i]) && text[i] != '\n')
            i++;
        return i;
    }

    /// <summary>The word the offset is in, start and end, for double-click.</summary>
    public static (int Start, int End) WordAt(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset;
        var end = offset;
        while (start > 0 && IsWordCharacter(text[start - 1]))
            start--;
        while (end < text.Length && IsWordCharacter(text[end]))
            end++;
        if (start == end && end < text.Length)
            end = NextCharacter(text, end);
        return (start, end);
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '\'' or '’' || char.IsSurrogate(c);
}

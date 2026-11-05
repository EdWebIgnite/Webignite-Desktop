using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Webignite.Core.Writing;
using TextBlock = Webignite.Core.Writing.TextBlock;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Spell check: a wavy line under words the dictionary doesn't know, and suggestions for them on a right-click.
/// The word being typed is left alone until it's finished, and the signature isn't checked.
/// </summary>
public sealed partial class RichTextEditor
{
    private readonly Dictionary<TextBlock, (string Text, int Revision, IReadOnlyList<Misspelt> Found)> mistakes = new(ReferenceEqualityComparer.Instance);
    private ISpelling? spelling;

    /// <summary>The dictionary to check against; null switches spell check off.</summary>
    public ISpelling? Spelling
    {
        get => spelling;
        set
        {
            spelling = value;
            mistakes.Clear();
            InvalidateVisual();
        }
    }

    /// <summary>The misspelt words in a paragraph, worked out once for each version of its text.</summary>
    private IReadOnlyList<Misspelt> MistakesIn(TextBlock block)
    {
        if (spelling is not { } checker || block.IsSignature)
            return [];
        var text = block.Text;
        if (mistakes.TryGetValue(block, out var known) && known.Text == text && known.Revision == checker.Revision)
            return known.Found;
        if (mistakes.Count > 2000)
            mistakes.Clear();
        var found = Webignite.Core.Writing.Spelling.Mistakes(text, checker);
        mistakes[block] = (text, checker.Revision, found);
        return found;
    }

    private void DrawMistakes(DrawingContext context, Placed p, int slot)
    {
        if (spelling is null || p.Layout is not { } layout || p.Slot?.Text is not { } block)
            return;
        var found = MistakesIn(block);
        if (found.Count == 0)
            return;
        var pen = new Pen(Brush("Attention"), 1);
        foreach (var word in found)
        {
            // Still being typed: the caret is in or just after it.
            if (typingGroup && caret.Slot == slot && caret.Offset >= word.Start && caret.Offset <= word.Start + word.Length)
                continue;
            foreach (var r in layout.HitTestTextRange(word.Start, word.Length))
                DrawWave(context, pen, r.Translate(p.Origin));
        }
    }

    private static void DrawWave(DrawingContext context, Pen pen, Rect under)
    {
        if (under.Width < 2)
            return;
        var wave = new StreamGeometry();
        using (var g = wave.Open())
        {
            var y = under.Bottom + 0.5;
            g.BeginFigure(new Point(under.Left, y), false);
            var up = true;
            for (var x = under.Left + 2; x <= under.Right; x += 2)
            {
                g.LineTo(new Point(x, up ? y - 1.5 : y));
                up = !up;
            }
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, wave);
    }

    /// <summary>
    /// The menu's first items when the caret is on a misspelt word: suggestions, add, ignore. Public so the
    /// screenshot tool can check them.
    /// </summary>
    public List<object> SpellingMenu()
    {
        if (spelling is not { } checker || HasSelection || Document is not { } document)
            return [];
        var slots = RichEditing.Slots(document);
        if (caret.Slot >= slots.Count || slots[caret.Slot].Text is not { IsSignature: false } block)
            return [];
        if (Webignite.Core.Writing.Spelling.At(block.Text, caret.Offset, checker) is not { } word)
            return [];
        var slot = caret.Slot;
        var items = new List<object>();
        var suggestions = checker.Suggest(word.Word);
        foreach (var suggestion in suggestions)
            items.Add(Item(suggestion, null, true, () => Replace(slot, word, suggestion)));
        if (suggestions.Count == 0)
            items.Add(Item("No suggestions", null, false, () => { }));
        items.Add(new Separator());
        items.Add(Item($"Add \"{word.Word}\" to the dictionary", null, true, () =>
        {
            checker.Add(word.Word);
            InvalidateVisual();
        }));
        items.Add(Item("Ignore it in this mail", null, true, () =>
        {
            checker.Ignore(word.Word);
            InvalidateVisual();
        }));
        items.Add(new Separator());
        return items;
    }

    // The word swapped for the suggestion, keeping its look (bold, colour, link).
    private void Replace(int slot, Misspelt word, string with) => Change(d =>
    {
        if (RichEditing.Slots(d)[slot].Text is not { } block || block.Text.Length < word.Start + word.Length
            || block.Text.Substring(word.Start, word.Length) != word.Word)
            return caret;
        var format = block.FormatAt(word.Start + 1);
        var at = RichEditing.Delete(d, new DocPosition(slot, word.Start), new DocPosition(slot, word.Start + word.Length));
        return RichEditing.Insert(d, at, with, format);
    });
}

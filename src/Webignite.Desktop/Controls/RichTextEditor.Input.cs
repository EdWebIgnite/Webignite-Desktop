using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Webignite.Core.Writing;
using TextBlock = Webignite.Core.Writing.TextBlock;

namespace Webignite.Desktop.Controls;

/// <summary>Keys, mouse, clipboard and undo.</summary>
public sealed partial class RichTextEditor
{
    private const int UndoLimit = 200;

    private readonly Stack<(RichDocument Document, DocPosition Caret, DocPosition Anchor)> undo = new();
    private readonly Stack<(RichDocument Document, DocPosition Caret, DocPosition Anchor)> redo = new();
    private bool typingGroup;

    /// <summary>The look the next typed letters take, when B, I or a colour was chosen with nothing selected.</summary>
    private RunFormat? pending;

    private double? desiredX;
    private bool dragging;

    /// <summary>The selection, start first.</summary>
    public (DocPosition Start, DocPosition End) Selection => (DocPosition.Min(caret, anchor), DocPosition.Max(caret, anchor));

    public bool HasSelection => caret != anchor;

    private static readonly DataFormat<RichDocument> Writing = DataFormat.CreateInProcessFormat<RichDocument>("webignite-writing");
    private static readonly DataFormat<string> HtmlFormat = DataFormat.CreateStringPlatformFormat("HTML Format");

    // Changes

    /// <summary>Makes a change with undo, then lays out and moves the caret.</summary>
    private void Change(Func<RichDocument, DocPosition> edit, bool typing = false)
    {
        if (Document is not { } document)
            return;
        if (!typing || !typingGroup)
        {
            undo.Push((document.Clone(), caret, anchor));
            while (undo.Count > UndoLimit)
                Trim(undo);
            redo.Clear();
        }
        typingGroup = typing;
        var at = edit(document);
        document.EnsureNotEmpty();
        caret = anchor = RichEditing.Clamp(document, at);
        desiredX = null;
        Changed();
    }

    /// <summary>Changes the selection's look or paragraphs without moving it.</summary>
    private void Restyle(Action<RichDocument, DocPosition, DocPosition> edit)
    {
        if (Document is not { } document)
            return;
        undo.Push((document.Clone(), caret, anchor));
        redo.Clear();
        typingGroup = false;
        var (start, end) = Selection;
        edit(document, start, end);
        caret = RichEditing.Clamp(document, caret);
        anchor = RichEditing.Clamp(document, anchor);
        Changed();
    }

    private void Changed()
    {
        Revision++;
        Relayout();
        RestartBlink();
        Dispatcher.UIThread.Post(ShowCaret, DispatcherPriority.Render);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void Trim(Stack<(RichDocument, DocPosition, DocPosition)> stack)
    {
        var kept = stack.Take(UndoLimit).Reverse().ToList();
        stack.Clear();
        foreach (var item in kept)
            stack.Push(item);
    }

    public void Undo() => Swap(undo, redo);

    public void Redo() => Swap(redo, undo);

    private void Swap(Stack<(RichDocument Document, DocPosition Caret, DocPosition Anchor)> from, Stack<(RichDocument Document, DocPosition Caret, DocPosition Anchor)> to)
    {
        if (Document is not { } document || from.Count == 0)
            return;
        to.Push((document.Clone(), caret, anchor));
        var (saved, savedCaret, savedAnchor) = from.Pop();
        document.Blocks.Clear();
        document.Blocks.AddRange(saved.Blocks);
        caret = RichEditing.Clamp(document, savedCaret);
        anchor = RichEditing.Clamp(document, savedAnchor);
        typingGroup = false;
        ForgetPictures(keepInDocument: true);
        Changed();
    }

    /// <summary>Moves the caret, keeping the selection's other end when extending.</summary>
    private void MoveTo(DocPosition to, bool extend, bool keepColumn = false)
    {
        if (Document is null)
            return;
        caret = RichEditing.Clamp(Document, to);
        if (!extend)
            anchor = caret;
        if (!keepColumn)
            desiredX = null;
        typingGroup = false;
        pending = null;
        RestartBlink();
        ShowCaret();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowCaret()
    {
        if (CaretRect() is { } rect)
            this.BringIntoView(rect.Inflate(new Thickness(0, 16)));
    }

    // Typing

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text) || Document is null || e.Text.All(char.IsControl))
            return;
        Type(e.Text);
        e.Handled = true;
    }

    private void Type(string text)
    {
        var format = pending ?? (HasSelection ? RichEditing.FormatOf(Document!, Selection.Start, Selection.End) : RichEditing.FormatAt(Document!, caret));
        // A link doesn't carry on past its last letter when typing straight after it.
        if (pending is null && !HasSelection && format.Link is not null && IsEndOfLink())
            format = format with { Link = null };
        var keepPending = pending;
        Change(d =>
        {
            var at = HasSelection ? RichEditing.Delete(d, Selection.Start, Selection.End) : caret;
            return RichEditing.Insert(d, at, text, format);
        }, typing: text != " ");
        pending = keepPending;
    }

    private bool IsEndOfLink()
    {
        if (Document is null || RichEditing.Slots(Document)[caret.Slot].Text is not { } block)
            return false;
        return caret.Offset == block.Length || block.FormatAt(caret.Offset + 1).Link != block.FormatAt(caret.Offset).Link;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Document is not { } document || e.Handled)
            return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var slots = RichEditing.Slots(document);
        var slot = slots[caret.Slot];
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Left:
                MoveTo(HasSelection && !shift ? Selection.Start : Step(slots, -1, ctrl), shift);
                break;
            case Key.Right:
                MoveTo(HasSelection && !shift ? Selection.End : Step(slots, 1, ctrl), shift);
                break;
            case Key.Up:
                MoveTo(Vertical(-1), shift, keepColumn: true);
                break;
            case Key.Down:
                MoveTo(Vertical(1), shift, keepColumn: true);
                break;
            case Key.PageUp:
                MoveTo(Vertical(-1, page: true), shift, keepColumn: true);
                break;
            case Key.PageDown:
                MoveTo(Vertical(1, page: true), shift, keepColumn: true);
                break;
            case Key.Home:
                MoveTo(ctrl ? RichEditing.Start : LineEdge(start: true), shift);
                break;
            case Key.End:
                MoveTo(ctrl ? RichEditing.End(document) : LineEdge(start: false), shift);
                break;
            case Key.Back:
                if (HasSelection)
                    Change(d => RichEditing.Delete(d, Selection.Start, Selection.End));
                else if (ctrl && slot.Text is { } back && caret.Offset > 0)
                    Change(d => RichEditing.Delete(d, caret with { Offset = RichEditing.WordStart(back.Text, caret.Offset) }, caret));
                else
                    Change(d => RichEditing.Backspace(d, caret), typing: true);
                break;
            case Key.Delete:
                if (HasSelection)
                    Change(d => RichEditing.Delete(d, Selection.Start, Selection.End));
                else if (ctrl && slot.Text is { } forward && caret.Offset < forward.Length)
                    Change(d => RichEditing.Delete(d, caret, caret with { Offset = RichEditing.WordEnd(forward.Text, caret.Offset) }));
                else
                    Change(d => RichEditing.DeleteForward(d, caret));
                break;
            case Key.Enter:
                if (shift)
                    Change(d => RichEditing.LineBreak(d, HasSelection ? RichEditing.Delete(d, Selection.Start, Selection.End) : caret,
                        pending ?? RichEditing.FormatAt(d, caret)));
                else
                    Change(d => RichEditing.Enter(d, HasSelection ? RichEditing.Delete(d, Selection.Start, Selection.End) : caret));
                break;
            case Key.Tab when slot.InTable:
                MoveTo(NextCell(slots, shift ? -1 : 1), extend: false);
                break;
            case Key.Tab when slot.Text is { List: not ListKind.None } && !ctrl:
                Restyle((d, s, en) => RichEditing.Indent(d, s, en, shift ? -1 : 1));
                break;
            case Key.A when ctrl:
                anchor = RichEditing.Start;
                MoveTo(RichEditing.End(document), extend: true);
                break;
            case Key.C when ctrl:
                CopyAsync(cut: false).Forget("Copying");
                break;
            case Key.X when ctrl:
                CopyAsync(cut: true).Forget("Cutting");
                break;
            case Key.V when ctrl:
                PasteAsync(plain: shift).Forget("Pasting");
                break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Redo();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.B when ctrl:
                ToggleBold();
                break;
            case Key.I when ctrl:
                ToggleItalic();
                break;
            case Key.U when ctrl:
                ToggleUnderline();
                break;
            case Key.Space when ctrl:
                ClearFormatting();
                break;
            case Key.OemCloseBrackets when ctrl:
            case Key.OemPeriod when ctrl && shift:
                StepSize(1);
                break;
            case Key.OemOpenBrackets when ctrl:
            case Key.OemComma when ctrl && shift:
                StepSize(-1);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    // One character or word left or right, crossing into the next paragraph.
    private DocPosition Step(IReadOnlyList<Slot> slots, int direction, bool word)
    {
        var slot = slots[caret.Slot];
        if (slot.Text is { } block)
        {
            var text = block.Text;
            if (direction < 0 && caret.Offset > 0)
                return caret with { Offset = word ? RichEditing.WordStart(text, caret.Offset) : RichEditing.PreviousCharacter(text, caret.Offset) };
            if (direction > 0 && caret.Offset < text.Length)
                return caret with { Offset = word ? RichEditing.WordEnd(text, caret.Offset) : RichEditing.NextCharacter(text, caret.Offset) };
        }
        else if (direction < 0 && caret.Offset == 1 || direction > 0 && caret.Offset == 0)
        {
            return caret with { Offset = direction > 0 ? 1 : 0 };
        }
        var next = caret.Slot + direction;
        if (next < 0 || next >= slots.Count)
            return caret;
        return new DocPosition(next, direction < 0 ? slots[next].Length : 0);
    }

    private DocPosition Vertical(int direction, bool page = false)
    {
        if (CaretRect() is not { } rect)
            return caret;
        desiredX ??= rect.X;
        var distance = page ? Math.Max(Bounds.Height / 2, 200) : 2;
        var y = direction < 0 ? rect.Top - distance : rect.Bottom + distance;
        if (y < 0)
            return RichEditing.Start;
        if (placed.Count > 0 && y > placed.Max(p => p.Bounds.Bottom))
            return Document is null ? caret : RichEditing.End(Document);
        return PositionAt(new Point(desiredX.Value, y));
    }

    private DocPosition LineEdge(bool start)
    {
        if (PlacedAt(caret.Slot) is not { Layout: { } layout } p || p.Slot?.Text is not { } block)
            return caret with { Offset = start ? 0 : 1 };
        var line = layout.TextLines[layout.GetLineIndexFromCharacterIndex(caret.Offset, false)];
        if (start)
            return caret with { Offset = line.FirstTextSourceIndex };
        var end = Math.Min(line.FirstTextSourceIndex + line.Length, block.Length);
        // A wrapped or broken line ends before its space or line break, as in other editors.
        if (end < block.Length && end > line.FirstTextSourceIndex && block.Text[end - 1] is '\n' or ' ')
            end--;
        return caret with { Offset = end };
    }

    private DocPosition NextCell(IReadOnlyList<Slot> slots, int direction)
    {
        var next = caret.Slot + direction;
        if (next < 0 || next >= slots.Count)
            return caret;
        return new DocPosition(next, direction > 0 ? 0 : slots[next].Length);
    }

    // Mouse

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Document is null)
            return;
        Focus();
        var point = e.GetCurrentPoint(this);
        var at = PositionAt(point.Position);
        if (point.Properties.IsRightButtonPressed)
        {
            if (!HasSelection || at < Selection.Start || at > Selection.End)
                MoveTo(at, extend: false);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.ClickCount == 1 && LinkAt(at) is { } link && LinkOpened is { } open)
        {
            open(link);
            e.Handled = true;
            return;
        }
        if (BoxAt(point.Position) is { } item)
        {
            Change(d =>
            {
                item.Checked = !item.Checked;
                return caret;
            });
            e.Handled = true;
            return;
        }
        var slots = RichEditing.Slots(Document);
        if (e.ClickCount == 2 && slots[at.Slot].Text is { } word)
        {
            var (from, to) = RichEditing.WordAt(word.Text, at.Offset);
            anchor = at with { Offset = from };
            MoveTo(at with { Offset = to }, extend: true);
        }
        else if (e.ClickCount >= 3)
        {
            anchor = at with { Offset = 0 };
            MoveTo(at with { Offset = slots[at.Slot].Length }, extend: true);
        }
        else if (slots[at.Slot].IsAtom)
        {
            // Clicking a picture or line selects it.
            anchor = at with { Offset = 0 };
            MoveTo(at with { Offset = 1 }, extend: true);
        }
        else
        {
            MoveTo(at, extend: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            dragging = true;
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragging)
            MoveTo(PositionAt(e.GetPosition(this)), extend: true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging)
            return;
        dragging = false;
        e.Pointer.Capture(null);
    }

    // Clipboard

    private IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;

    public async Task CopyAsync(bool cut)
    {
        if (Document is not { } document || !HasSelection || Clipboard is not { } clipboard)
            return;
        var (start, end) = Selection;
        var piece = RichEditing.Copy(document, start, end);
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, RichText.ToText(piece)));
        data.Add(DataTransferItem.Create(Writing, piece));
        await clipboard.SetDataAsync(data);
        if (cut)
            Change(d => RichEditing.Delete(d, start, end));
    }

    /// <summary>Pastes writing copied here, a picture, HTML from a browser or Word, or plain text.</summary>
    public async Task PasteAsync(bool plain = false)
    {
        if (Document is null || Clipboard is not { } clipboard)
            return;
        RichDocument? piece = null;
        var formats = await clipboard.GetDataFormatsAsync();
        if (!plain && formats.Contains(Writing))
            piece = (await clipboard.TryGetValueAsync(Writing))?.Clone();
        if (piece is null && !plain && formats.Contains(HtmlFormat) && await clipboard.TryGetValueAsync(HtmlFormat) is { Length: > 0 } html)
            piece = RichHtml.Read(Fragment(html));
        if (piece is null && !plain && formats.Contains(DataFormat.Bitmap) && await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            using (bitmap)
            {
                using var stream = new MemoryStream();
                bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                InsertPicture(stream.ToArray(), "image/png", bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            }
            return;
        }
        if (piece is null && await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            piece = new RichDocument();
            foreach (var line in text.Replace("\r", "").Split('\n'))
                piece.Blocks.Add(new TextBlock(line));
        }
        if (piece is null)
            return;
        var keep = piece;
        Change(d => RichEditing.InsertDocument(d, HasSelection ? RichEditing.Delete(d, Selection.Start, Selection.End) : caret, keep));
    }

    // Windows puts a small header before the HTML; the copied part sits between StartFragment and EndFragment.
    private static string Fragment(string html)
    {
        const string start = "<!--StartFragment-->";
        const string end = "<!--EndFragment-->";
        var from = html.IndexOf(start, StringComparison.Ordinal);
        var to = html.IndexOf(end, StringComparison.Ordinal);
        if (from >= 0 && to > from)
            return html[(from + start.Length)..to];
        var tag = html.IndexOf('<');
        return tag > 0 ? html[tag..] : html;
    }

    /// <summary>A link to open, from Ctrl+click or the right-click menu. Without it, links are only edited.</summary>
    public event Action<string>? LinkOpened;

    private string? LinkAt(DocPosition at)
    {
        if (Document is null || RichEditing.Slots(Document)[at.Slot].Text is not { } block)
            return null;
        // The letter clicked on: the one after the caret position, or the last one at the end of a line.
        var offset = Math.Min(at.Offset + 1, block.Length);
        return offset == 0 ? null : block.FormatAt(offset).Link;
    }

    private void ShowMenu(object? sender, ContextRequestedEventArgs e)
    {
        var link = LinkOpened is null ? null : LinkAt(caret);
        var items = SpellingMenu();
        if (link is not null)
        {
            items.Add(Item("Open link (Ctrl+click)", null, true, () => LinkOpened?.Invoke(link)));
            items.Add(new Separator());
        }
        items.AddRange(
        [
            Item("Cut", "Ctrl+X", HasSelection, () => CopyAsync(cut: true).Forget("Cutting")),
            Item("Copy", "Ctrl+C", HasSelection, () => CopyAsync(cut: false).Forget("Copying")),
            Item("Paste", "Ctrl+V", true, () => PasteAsync().Forget("Pasting")),
            Item("Paste as plain text", "Ctrl+Shift+V", true, () => PasteAsync(plain: true).Forget("Pasting")),
            new Separator(),
            Item("Select all", "Ctrl+A", true, () =>
            {
                anchor = RichEditing.Start;
                MoveTo(RichEditing.End(Document!), extend: true);
            }),
        ]);
        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(this);
        e.Handled = true;
    }

    private static MenuItem Item(string header, string? keys, bool enabled, Action action)
    {
        var item = new MenuItem { Header = header, InputGesture = keys is null ? null : KeyGesture.Parse(keys), IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }
}

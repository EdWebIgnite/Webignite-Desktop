using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Webignite.Core.Mail;

namespace Webignite.Desktop.Controls;

/// <summary>
/// To, Cc, Bcc or Reply-To as chips, the way Gmail and other mail programs show them: each address typed and
/// followed by a comma, Enter or Tab (or a suggestion picked) becomes its own little block with an x to take it
/// out. Backspace in an empty box takes the last one back to edit; a double-click on one does the same. One that
/// isn't a real address is marked as a problem. <see cref="Text"/> is the whole box as text ("a, b, typing"), so
/// the mail and the suggestions read it as before.
/// </summary>
public sealed class AddressBox : Border
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<AddressBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Names known by address (Contacts, clients, people who've mailed), so a known person's chip shows their name alone.</summary>
    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> NamesProperty =
        AvaloniaProperty.Register<AddressBox, IReadOnlyDictionary<string, string>?>(nameof(Names));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<AddressBox, string?>(nameof(PlaceholderText));

    private readonly WrapPanel panel;
    private List<string> chips = [];
    private bool updating;

    public AddressBox()
    {
        Classes.Add("addressbox");
        ClipToBounds = true;
        Box = new TextBox { MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
        Box.Classes.Add("addressinput");
        panel = new WrapPanel { ItemSpacing = 4, LineSpacing = 4, Children = { Box } };
        Child = panel;
        Box.TextChanged += (_, _) => Typed();
        // The text box keeps Backspace to itself, so listen even to keys it has handled.
        Box.AddHandler(KeyDownEvent, Keys, RoutingStrategies.Bubble, handledEventsToo: true);
        // Leaving the box makes a finished address a chip; half a word stays, for a suggestion to finish it.
        Box.LostFocus += (_, _) =>
        {
            if (Recipients.Chip(Box.Text ?? "").Valid)
                Commit();
        };
        // A click on the empty part of the box puts the cursor in it.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source == this || e.Source == panel)
            {
                Box.Focus();
                Box.CaretIndex = Box.Text?.Length ?? 0;
            }
        }, RoutingStrategies.Bubble);
    }

    // Styled as a Border, with the addressbox class.
    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>The box as text: the chips, then what's being typed.</summary>
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public string? PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }

    public IReadOnlyDictionary<string, string>? Names { get => GetValue(NamesProperty); set => SetValue(NamesProperty, value); }

    /// <summary>Where the typing happens, for suggestions and the keys that pick them.</summary>
    public TextBox Box { get; }

    /// <summary>The chips as they stand, for checks.</summary>
    public IReadOnlyList<string> Chips => chips;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && !updating)
            Show(Text);
        else if (change.Property == PlaceholderTextProperty)
            ShowPlaceholder();
        else if (change.Property == NamesProperty)
            Draw();
    }

    // Text set from outside (a reply's people, a suggestion picked): every full address is a chip, and only a
    // half-typed end stays in the box.
    private void Show(string? text)
    {
        var (done, typing) = Recipients.Chips(text);
        chips = [.. done];
        if (Recipients.Chip(typing).Valid)
        {
            chips.Add(typing.Trim());
            typing = "";
        }
        Draw();
        updating = true;
        Box.Text = typing;
        updating = false;
    }

    // The person typed: a comma or semicolon finishes an address.
    private void Typed()
    {
        if (updating)
            return;
        var typed = Box.Text ?? "";
        if (typed.IndexOfAny([',', ';']) >= 0)
        {
            var (done, typing) = Recipients.Chips(Recipients.Join(chips, typed));
            chips = [.. done];
            Draw();
            updating = true;
            Box.Text = typing;
            updating = false;
            Box.CaretIndex = typing.Length;
        }
        Push();
    }

    private void Keys(object? sender, KeyEventArgs e)
    {
        // Enter or Tab that picked a suggestion is done with.
        if (e.Handled && e.Key != Key.Back)
            return;
        var typed = Box.Text ?? "";
        switch (e.Key)
        {
            case Key.Enter when typed.Trim().Length > 0:
                Commit();
                e.Handled = true;
                break;
            case Key.Tab when typed.Trim().Length > 0:
                Commit();
                break;
            case Key.Back when typed.Length == 0 && chips.Count > 0:
                Edit(chips.Count - 1);
                e.Handled = true;
                break;
        }
    }

    private void Commit()
    {
        var typed = (Box.Text ?? "").Trim();
        if (typed.Length == 0)
            return;
        chips.Add(typed);
        Draw();
        updating = true;
        Box.Text = "";
        updating = false;
        Push();
    }

    // A chip back in the box to change it.
    private void Edit(int at)
    {
        var entry = chips[at];
        chips.RemoveAt(at);
        Draw();
        updating = true;
        Box.Text = entry;
        updating = false;
        Box.Focus();
        Box.CaretIndex = entry.Length;
        Push();
    }

    private void Remove(int at)
    {
        chips.RemoveAt(at);
        Draw();
        Push();
    }

    private void Push()
    {
        updating = true;
        SetCurrentValue(TextProperty, Recipients.Join(chips, Box.Text ?? ""));
        updating = false;
    }

    private void Draw()
    {
        panel.Children.Clear();
        for (var i = 0; i < chips.Count; i++)
            panel.Children.Add(MakeChip(i));
        panel.Children.Add(Box);
        ShowPlaceholder();
    }

    private void ShowPlaceholder() => Box.PlaceholderText = chips.Count == 0 ? PlaceholderText : null;

    private Border MakeChip(int at)
    {
        var entry = chips[at];
        var (label, valid) = Recipients.Chip(entry, Names);
        var remove = new Button { Content = new Icon { Data = IconConverter.Instance.Convert("Close", typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) as Geometry, Size = 10 } };
        remove.Classes.Add("chipremove");
        ToolTip.SetTip(remove, "Take it out");
        remove.Click += (_, _) => Remove(at);
        var chip = new Border
        {
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis }, remove },
            },
        };
        chip.Classes.Add("addresschip");
        chip.Classes.Set("bad", !valid);
        ToolTip.SetTip(chip, valid ? entry : $"{entry} isn't a full address. Double-click to change it.");
        chip.DoubleTapped += (_, _) => Edit(at);
        return chip;
    }
}

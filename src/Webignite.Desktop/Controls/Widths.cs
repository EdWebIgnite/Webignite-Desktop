using Avalonia.Data.Converters;

namespace Webignite.Desktop.Controls;

/// <summary>Widths worked out from another control's, for bindings.</summary>
public static class Widths
{
    /// <summary>A list's width less its padding and the item's margins, so a heading in a wrapping list takes a whole line.</summary>
    public static readonly IValueConverter LessMargin = new FuncValueConverter<double, double>(width => Math.Max(0, width - 48));
}

using Avalonia;
using Avalonia.Controls;

namespace Webignite.Desktop.Controls;

/// <summary>Small text built in code is sized by name, like text in a view, so a style can change it.</summary>
public static class NamedSizes
{
    /// <summary>Sizes the text by a name from Styles/Sizes.axaml, such as Text12.</summary>
    public static TextBlock Sized(this TextBlock text, string name)
    {
        text.Bind(TextBlock.FontSizeProperty, text.GetResourceObservable(name));
        return text;
    }
}

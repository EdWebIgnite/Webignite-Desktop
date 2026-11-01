using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Webignite.Desktop.Controls;

/// <summary>Turns an icon name such as "Mail" into its drawing from Styles/Icons.axaml.</summary>
public sealed class IconConverter : IValueConverter
{
    public static readonly IconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name && Application.Current?.TryGetResource("Icon." + name, null, out var found) == true ? found as Geometry : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

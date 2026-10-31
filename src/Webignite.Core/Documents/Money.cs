namespace Webignite.Core.Documents;

/// <summary>Money rules. Amounts are always decimal, never double, and round half away from zero to cents.</summary>
public static class Money
{
    public static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Formats an amount the South African way, e.g. R 12 345.60, with non-breaking spaces.</summary>
    public static string Rand(decimal value)
    {
        var rounded = Cents(value);
        var sign = rounded < 0 ? "-" : "";
        var abs = Math.Abs(rounded);
        var whole = decimal.Truncate(abs).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)
            .Replace(',', ' ');
        var fraction = ((abs - decimal.Truncate(abs)) * 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        return $"{sign}R {whole}.{fraction}";
    }
}

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

    /// <summary>Reads an amount the way people type it: "R1 250", "1250,50" or "1,250.50". Null if it isn't a number.</summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var cleaned = text.Trim().TrimStart('R', 'r').Replace(" ", "").Replace(" ", "");
        // Accept a comma as the decimal mark when there is no dot, as many South Africans type it.
        if (!cleaned.Contains('.') && cleaned.Count(c => c == ',') == 1)
            cleaned = cleaned.Replace(',', '.');
        else
            cleaned = cleaned.Replace(",", "");
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}

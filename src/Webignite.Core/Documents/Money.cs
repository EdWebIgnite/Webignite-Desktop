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

    /// <summary>Reads an amount the way people type it: "R1 250", "1,250", "1250,50" or "1,250.50". Null if it isn't a number.</summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var cleaned = text.Trim().TrimStart('R', 'r').Replace(" ", "").Replace(" ", "");
        // Accept a comma as the decimal mark when there is no dot, as many South Africans type it, except where it
        // can only be grouping: "1,250" is R 1 250, while "1250,50" and "12,5" are decimals.
        if (!cleaned.Contains('.') && cleaned.Count(c => c == ',') == 1 && !IsThousands(cleaned))
            cleaned = cleaned.Replace(',', '.');
        else
            cleaned = cleaned.Replace(",", "");
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    // One to three digits (not starting with 0), a comma, then exactly three digits: "1,250", "-12,500".
    private static bool IsThousands(string text)
    {
        var comma = text.IndexOf(',');
        var before = text[..comma].TrimStart('-');
        var after = text[(comma + 1)..];
        return before.Length is >= 1 and <= 3 && before[0] != '0' && before.All(char.IsDigit)
            && after.Length == 3 && after.All(char.IsDigit);
    }
}

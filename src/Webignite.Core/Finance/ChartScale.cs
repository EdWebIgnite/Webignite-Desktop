using System.Globalization;

namespace Webignite.Core.Finance;

/// <summary>The scale on the money chart: a round top figure and short amounts for its labels.</summary>
public static class ChartScale
{
    /// <summary>A round top for the scale: 1, 2, 2.5 or 5 times a power of ten, at least R 1 000.</summary>
    public static decimal NiceTop(decimal max)
    {
        if (max <= 1000)
            return 1000;
        var power = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)max)));
        foreach (var step in new[] { 1m, 2m, 2.5m, 5m, 10m })
            if (step * power >= max)
                return step * power;
        return 10 * power;
    }

    /// <summary>R 12k, R 1.5k, R 800.</summary>
    public static string Short(decimal amount) => amount switch
    {
        0 => "R 0",
        >= 1_000_000 => $"R {(amount / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture)}m",
        >= 1000 => $"R {(amount / 1000).ToString("0.#", CultureInfo.InvariantCulture)}k",
        _ => $"R {amount.ToString("0", CultureInfo.InvariantCulture)}",
    };
}

using System.Text.Json;

namespace Webignite.Core.Web;

/// <summary>The few rules behind the Browser tool that need no browser to try: where typed words lead, and stepping back and forward.</summary>
public static class Browsing
{
    /// <summary>
    /// Where what was typed in the address box leads: the address itself when it is one ("karoocoffee.co.za"),
    /// otherwise a web search for the words. Null when nothing was typed.
    /// </summary>
    public static Uri? Destination(string? typed)
    {
        var text = (typed ?? "").Trim();
        if (text.Length == 0)
            return null;
        // Words with spaces, or with no dot in them, are a search; "localhost" and addresses with a scheme are not.
        var looksLikeAddress = !text.Contains(' ') && (text.Contains('.') || text.Contains("://", StringComparison.Ordinal) || text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));
        if (looksLikeAddress && PageCapture.Address(text) is { } address)
            return address;
        return new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(text));
    }

    /// <summary>
    /// The page to go to for a step back (-1) or forward (1), from the browser's own list of where it has been
    /// (the answer to Page.getNavigationHistory). Null at either end of the list, or when the answer can't be read.
    /// </summary>
    public static int? HistoryStep(string historyJson, int by)
    {
        try
        {
            using var history = JsonDocument.Parse(historyJson);
            var root = history.RootElement;
            if (!root.TryGetProperty("currentIndex", out var current) || !root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return null;
            var wanted = current.GetInt32() + by;
            return wanted >= 0 && wanted < entries.GetArrayLength() && entries[wanted].TryGetProperty("id", out var id) ? id.GetInt32() : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static string GoToMessage(int entryId) => JsonSerializer.Serialize(new Dictionary<string, object> { ["entryId"] = entryId });
}

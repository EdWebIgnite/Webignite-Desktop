using System.Text.Json;

namespace Webignite.Core.Web;

/// <summary>
/// A page's HTML as it stands after its scripts have run, asked of a browser through its developer tools. For
/// sites that send an almost empty page and build the rest in JavaScript, where reading what the server sent
/// finds no links and no headings.
/// </summary>
public static class RenderedHtml
{
    /// <summary>The longest the page is given to finish building itself, in milliseconds.</summary>
    public const int LongestWait = 6000;

    /// <summary>How long the page must have stopped changing before it is called finished, in milliseconds.</summary>
    public const int Quiet = 500;

    /// <summary>
    /// Waits until the page has stopped changing for half a second (or six seconds at most), then gives its HTML.
    /// For Runtime.evaluate.
    /// </summary>
    public static string Message { get; } = JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["expression"] = $$"""
            (async () => {
              let changed = Date.now();
              const watch = new MutationObserver(() => { changed = Date.now(); });
              watch.observe(document.documentElement, { childList: true, subtree: true, attributes: true, characterData: true });
              const started = Date.now();
              while (Date.now() - changed < {{Quiet}} && Date.now() - started < {{LongestWait}}) // still changing, and time left
                await new Promise(r => setTimeout(r, 100));
              watch.disconnect();
              return document.documentElement.outerHTML;
            })()
            """,
        ["awaitPromise"] = true,
        ["returnByValue"] = true,
    });

    /// <summary>The HTML from the browser's answer, or null when it gave none.</summary>
    public static string? Read(string? answer)
    {
        try
        {
            using var json = JsonDocument.Parse(answer ?? "");
            return json.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } html ? html : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

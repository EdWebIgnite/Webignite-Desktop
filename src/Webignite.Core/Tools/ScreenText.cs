namespace Webignite.Core.Tools;

/// <summary>Words read off a picture of the screen: tidied, and summed up for the note that says they were copied.</summary>
public static class ScreenText
{
    /// <summary>The lines as read, each trimmed, empty ones dropped, one per line.</summary>
    public static string Tidy(IEnumerable<string> lines) =>
        string.Join(Environment.NewLine, lines.Select(line => line.Trim()).Where(line => line.Length > 0));

    /// <summary>"3 words copied: Small-batch coffee from…", for the note.</summary>
    public static string Summary(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var start = string.Join(' ', words.Take(8));
        if (start.Length > 60)
            start = start[..60].TrimEnd();
        return (words.Length == 1 ? "1 word" : $"{words.Length:N0} words") + " copied: " + start + (words.Length > 8 || start.Length == 60 ? "…" : "");
    }

    /// <summary>How much a small picture is enlarged before reading: small print reads far better at twice the size.</summary>
    public static int Enlarge(int width, int height, int most) =>
        width <= 0 || height <= 0 ? 1 : width * 2 <= most && height * 2 <= most && height < 1600 ? 2 : 1;

    public const string Nothing = "No words were found there. Try a bigger area, or text that is larger and sharper.";

    public const string NoReader = "This PC has no text reading installed for its language. In Windows Settings, under Time and language, Language and region, add the language's optional feature for text recognition.";
}

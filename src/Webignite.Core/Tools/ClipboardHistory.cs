using System.Globalization;
using System.Security.Cryptography;

namespace Webignite.Core.Tools;

/// <summary>Something copied: text, or a picture kept as PNG with its size.</summary>
public sealed record Clip(string? Text, byte[]? Picture = null, int Width = 0, int Height = 0)
{
    /// <summary>What makes two copies the same: the text itself, or the picture's fingerprint.</summary>
    public string Key { get; } = Text ?? (Picture is null ? "" : "picture:" + Convert.ToHexString(SHA256.HashData(Picture)));

    public bool IsPicture => Picture is not null;
}

/// <summary>
/// The last things copied, newest first, kept in memory while Webignite is open and never written to disk.
/// Copies marked as private (passwords, from Webignite's site logins or a password manager) never get here.
/// </summary>
public static class ClipboardHistory
{
    public const int Most = 25;

    /// <summary>Pictures take room, so only the newest few are kept among the 25.</summary>
    public const int MostPictures = 8;

    /// <summary>Text longer than this isn't kept: a whole document copied isn't what the history is for.</summary>
    public const int LongestKept = 100_000;

    /// <summary>A picture bigger than this (as PNG) isn't kept.</summary>
    public const int LargestPicture = 8 * 1024 * 1024;

    /// <summary>The list with this copy at the top. Copying something again moves it up instead of adding it twice.</summary>
    public static IReadOnlyList<Clip> Add(IReadOnlyList<Clip> items, Clip clip)
    {
        if (clip.IsPicture ? clip.Picture!.Length == 0 || clip.Picture.Length > LargestPicture : string.IsNullOrWhiteSpace(clip.Text) || clip.Text.Length > LongestKept)
            return items;
        var kept = new List<Clip> { clip };
        var pictures = clip.IsPicture ? 1 : 0;
        foreach (var item in items.Where(i => i.Key != clip.Key))
        {
            if (kept.Count == Most)
                break;
            if (item.IsPicture && ++pictures > MostPictures)
                continue;
            kept.Add(item);
        }
        return kept;
    }

    public static IReadOnlyList<Clip> Remove(IReadOnlyList<Clip> items, string key) => [.. items.Where(i => i.Key != key)];

    /// <summary>Whether a copy matches what's typed in the search: every word in its text; "picture" finds the pictures.</summary>
    public static bool Matches(Clip clip, string? search)
    {
        var words = (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return true;
        var text = clip.IsPicture ? $"picture screenshot image {clip.Width}x{clip.Height}" : clip.Text!;
        return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>One line to show in the list: spaces and line breaks squashed, cut short with an ellipsis.</summary>
    public static string Preview(string text, int length = 90)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= length ? line : line[..(length - 1)].TrimEnd() + "…";
    }

    /// <summary>"3 lines, 240 characters", shown under a long copy; "Picture, 1920 × 1080" for a picture.</summary>
    public static string Size(Clip clip)
    {
        if (clip.IsPicture)
            return $"Picture, {clip.Width} × {clip.Height}";
        var text = clip.Text!;
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Length;
        var characters = text.Length.ToString("N0", CultureInfo.CurrentCulture);
        return lines > 1 ? $"{lines} lines, {characters} characters" : $"{characters} characters";
    }
}

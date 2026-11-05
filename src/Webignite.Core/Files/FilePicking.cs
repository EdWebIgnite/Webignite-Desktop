namespace Webignite.Core.Files;

/// <summary>
/// Picking files in the app's own explorer, in place of Windows' Open and Save boxes: which files a pick shows,
/// and the name a file is saved under.
/// </summary>
public static class FilePicking
{
    /// <summary>Whether a file's name fits patterns such as "*.png". No patterns means any file.</summary>
    public static bool Fits(string name, IReadOnlyList<string> patterns) =>
        patterns.Count == 0 || patterns.Any(p => p == "*" || p == "*.*"
            || (p.StartsWith("*.", StringComparison.Ordinal) && name.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase))
            || string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The name typed for a file to save, made safe for Windows and given the extension the patterns ask for
    /// when it has none of them. Null when nothing usable was typed.
    /// </summary>
    public static string? SaveName(string typed, IReadOnlyList<string> patterns)
    {
        if (typed.Trim().Trim('.').Trim().Length == 0)
            return null;
        var name = FileNames.Safe(typed);
        if (patterns.Count == 0 || Fits(name, patterns))
            return name;
        var extension = patterns.FirstOrDefault(p => p.StartsWith("*.", StringComparison.Ordinal) && p.Length > 2)?[1..];
        return extension is null ? name : name + extension;
    }

    /// <summary>"PNG image (*.png)" style words for what a pick shows, or "" for any file.</summary>
    public static string Describe(string? kind, IReadOnlyList<string> patterns) =>
        patterns.Count == 0 ? "" : $"{kind ?? "Files"} ({string.Join(", ", patterns)})";
}

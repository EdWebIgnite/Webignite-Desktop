namespace Webignite.Core.Files;

/// <summary>
/// What Windows needs to offer Webignite for pictures: in "Open with", and in Default apps so the person can
/// choose it for each kind. All under the signed-in user (HKEY_CURRENT_USER), so no administrator is needed, and
/// taken away again in one go. Windows only lets the person themselves set the default.
/// </summary>
public static class PictureAssociation
{
    public const string ProgId = "Webignite.Picture";
    public const string AppName = "Webignite";
    public const string CapabilitiesKey = @"Software\Webignite\Desktop\Capabilities";

    /// <summary>The kinds of picture the viewer opens, as Windows names them.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    /// <summary>One value to write: the key under HKEY_CURRENT_USER, the value's name (null for the key's default) and the text.</summary>
    public sealed record Entry(string Key, string? Name, string Value);

    /// <summary>
    /// The program to start: the installer's launcher beside the "current" folder when there is one, as it stays
    /// put when Webignite updates itself; otherwise the running program.
    /// </summary>
    public static string Launcher(string processPath, Func<string, bool> exists)
    {
        var folder = Path.GetDirectoryName(processPath);
        if (folder is not null && string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(folder) is { } root)
        {
            var launcher = Path.Combine(root, Path.GetFileName(processPath));
            if (exists(launcher))
                return launcher;
        }
        return processPath;
    }

    public static string Command(string exe) => $"\"{exe}\" \"%1\"";

    public static IReadOnlyList<Entry> Entries(string exe)
    {
        var entries = new List<Entry>
        {
            new($@"Software\Classes\{ProgId}", null, "Picture"),
            new($@"Software\Classes\{ProgId}", "FriendlyTypeName", "Picture"),
            new($@"Software\Classes\{ProgId}\DefaultIcon", null, $"\"{exe}\",0"),
            new($@"Software\Classes\{ProgId}\shell\open\command", null, Command(exe)),
            new(CapabilitiesKey, "ApplicationName", AppName),
            new(CapabilitiesKey, "ApplicationDescription", "Quotes, invoices and more, with a picture viewer"),
            new(@"Software\RegisteredApplications", AppName, CapabilitiesKey),
        };
        foreach (var extension in Extensions)
        {
            entries.Add(new($@"Software\Classes\{extension}\OpenWithProgids", ProgId, ""));
            entries.Add(new($@"{CapabilitiesKey}\FileAssociations", extension, ProgId));
        }
        return entries;
    }

    /// <summary>Keys taken away whole when Webignite stops being offered.</summary>
    public static readonly IReadOnlyList<string> KeysToRemove = [$@"Software\Classes\{ProgId}", CapabilitiesKey];

    /// <summary>Single values taken away, from keys other programs share.</summary>
    public static IReadOnlyList<Entry> ValuesToRemove =>
        [new(@"Software\RegisteredApplications", AppName, ""), .. Extensions.Select(e => new Entry($@"Software\Classes\{e}\OpenWithProgids", ProgId, ""))];

    /// <summary>Where Windows' Default apps page opens on Webignite, to pick it for each kind of picture.</summary>
    public const string DefaultAppsPage = "ms-settings:defaultapps?registeredAppUser=Webignite";

    /// <summary>The first thing on the command line that is a picture, e.g. from a double-click in Explorer.</summary>
    public static string? PictureFrom(IEnumerable<string> args, Func<string, bool> exists) =>
        args.FirstOrDefault(a => !a.StartsWith('-') && FileKinds.IsPicture(a) && exists(a));
}

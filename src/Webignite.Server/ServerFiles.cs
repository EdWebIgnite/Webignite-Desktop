namespace Webignite.Server;

/// <summary>
/// The server's secret and licence key kept as files in its data folder, for a server set up without a settings
/// file (the Synology NAS). Each is made the first time and readable by the server alone; a value given in the
/// settings always wins. Back up the whole data folder, as losing these means two-factor and licences start again.
/// </summary>
public static class ServerFiles
{
    public const string Secret = "server-secret";
    public const string LicenceKey = "licence-key";

    /// <summary>The value kept in <paramref name="file"/> in <paramref name="folder"/>, made with <paramref name="make"/> if it isn't there yet.</summary>
    public static string Kept(string folder, string file, Func<string> make)
    {
        var path = Path.Combine(folder, file);
        if (File.Exists(path))
            return File.ReadAllText(path).Trim();
        var value = make();
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var writer = new StreamWriter(path, System.Text.Encoding.ASCII, options))
            writer.Write(value);
        return value;
    }
}

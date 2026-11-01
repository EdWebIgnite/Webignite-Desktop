namespace Webignite.Data;

public static class DataPaths
{
    public const string EnvironmentVariable = "WEBIGNITE_DATA_DIR";

    /// <summary>%LOCALAPPDATA%\Webignite, unless WEBIGNITE_DATA_DIR says otherwise.</summary>
    public static string Root() =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Webignite");
}

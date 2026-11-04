using Webignite.Core.Mail;

namespace Webignite.Desktop;

/// <summary>Connecting Claude Desktop and ChatGPT to the Webignite connector on this PC.</summary>
public static class AssistantSetup
{
    /// <summary>The connector program, shipped next to Webignite.exe.</summary>
    public static string ConnectorPath() =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Webignite.Connector.exe" : "Webignite.Connector");

    public static bool ConnectorInstalled() => File.Exists(ConnectorPath());

    /// <summary>Every place Claude Desktop may keep its settings file on this PC (rules in ClaudeDesktopConfig.Paths).</summary>
    public static IReadOnlyList<string> ClaudeConfigPaths() => ClaudeDesktopConfig.Paths(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        (folder, pattern) => Directory.Exists(folder) ? Directory.GetDirectories(folder, pattern) : []);

    public static bool ClaudeConnected() =>
        ClaudeConfigPaths().Any(p => File.Exists(p) && ClaudeDesktopConfig.HasConnector(File.ReadAllText(p), ConnectorPath()));

    /// <summary>Adds the connector to every Claude Desktop install found, keeping a backup of each file. Returns how many.</summary>
    public static int ConnectClaude()
    {
        var command = ConnectorPath();
        var changed = 0;
        foreach (var path in ClaudeConfigPaths())
        {
            if (!Directory.Exists(Path.GetDirectoryName(path)))
                continue;
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            var updated = ClaudeDesktopConfig.WithConnector(existing, command);
            if (existing is not null)
                File.Copy(path, path + $".backup-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            File.WriteAllText(path, updated);
            changed++;
        }
        return changed;
    }
}

using Webignite.Core.Mail;

namespace Webignite.Desktop;

/// <summary>Connecting Claude Desktop and ChatGPT to the Webignite connector on this PC.</summary>
public static class AssistantSetup
{
    /// <summary>The connector program, shipped next to Webignite.exe.</summary>
    public static string ConnectorPath() =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Webignite.Connector.exe" : "Webignite.Connector");

    public static bool ConnectorInstalled() => File.Exists(ConnectorPath());

    /// <summary>Claude Desktop's settings file. The Microsoft Store version keeps it in its package folder.</summary>
    public static IReadOnlyList<string> ClaudeConfigPaths()
    {
        var paths = new List<string>();
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (roaming.Length > 0)
            paths.Add(Path.Combine(roaming, "Claude", "claude_desktop_config.json"));
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (Directory.Exists(packages))
            paths.AddRange(Directory.GetDirectories(packages, "Claude_*")
                .Select(p => Path.Combine(p, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json")));
        return paths;
    }

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

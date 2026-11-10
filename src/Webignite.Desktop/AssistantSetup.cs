using Webignite.Core.Mail;

namespace Webignite.Desktop;

/// <summary>Connecting Claude Desktop and ChatGPT to the Webignite connector on this PC.</summary>
public static class AssistantSetup
{
    /// <summary>The connector program, shipped next to Webignite.exe.</summary>
    public static string ConnectorPath() =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Webignite.Connector.exe" : "Webignite.Connector");

    public static bool ConnectorInstalled() => File.Exists(ConnectorPath());

    /// <summary>
    /// Stops every copy of the connector started from this install (Claude Desktop keeps one running while it's open),
    /// so an update can replace the install's files. Claude starts it again when it next needs it. Returns how many.
    /// </summary>
    public static int StopConnectors() => StopProgramsIn(Path.GetFileNameWithoutExtension(ConnectorPath()), AppContext.BaseDirectory);

    /// <summary>Ends the running programs of this name whose file is in <paramref name="folder"/>; others are left alone.</summary>
    internal static int StopProgramsIn(string name, string folder)
    {
        var stopped = 0;
        var inside = Path.GetFullPath(folder);
        foreach (var running in System.Diagnostics.Process.GetProcessesByName(name))
        {
            using (running)
            {
                try
                {
                    if (running.MainModule?.FileName is { } file && Path.GetFullPath(file).StartsWith(inside, StringComparison.OrdinalIgnoreCase))
                    {
                        running.Kill(entireProcessTree: true);
                        stopped++;
                    }
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    Webignite.Data.AppLog.Write($"Couldn't stop {name}", e);
                }
            }
        }
        return stopped;
    }

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

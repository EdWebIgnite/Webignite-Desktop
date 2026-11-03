using System.Text.Json;
using System.Text.Json.Nodes;

namespace Webignite.Core.Mail;

/// <summary>
/// Adds the Webignite connector to Claude Desktop's settings file, keeping everything else in it as it was.
/// </summary>
public static class ClaudeDesktopConfig
{
    public const string ServerName = "webignite";

    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The settings file's text with our connector added or updated. Throws when the file isn't valid.</summary>
    public static string WithConnector(string? existing, string command)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(existing))
            root = [];
        else
        {
            try
            {
                root = JsonNode.Parse(existing, documentOptions: Lenient) as JsonObject
                    ?? throw new FormatException("Claude Desktop's settings file isn't in the usual format, so it was left alone.");
            }
            catch (JsonException e)
            {
                throw new FormatException("Claude Desktop's settings file has a mistake in it, so it was left alone.", e);
            }
        }
        if (root["mcpServers"] is not JsonObject servers)
            root["mcpServers"] = servers = [];
        servers[ServerName] = new JsonObject { ["command"] = command, ["args"] = new JsonArray() };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public const string FileName = "claude_desktop_config.json";

    /// <summary>
    /// Every place Claude Desktop may keep its settings file on Windows, given the roaming and local app data
    /// folders (%APPDATA% and %LOCALAPPDATA%):
    /// <list type="bullet">
    /// <item>%APPDATA%\Claude, the classic install.</item>
    /// <item>%LOCALAPPDATA%\Claude-Data\&lt;each folder&gt;, the packaged install from claude.ai (for example
    /// Claude-Data\claude_pzs8sxrjxfjjc). This is the file Claude's own "Edit config" opens.</item>
    /// <item>%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude, the Microsoft Store version.</item>
    /// </list>
    /// Folders that aren't there are skipped, except the classic one, which is always listed. Empty arguments are ignored.
    /// </summary>
    public static IReadOnlyList<string> Paths(string? roaming, string? local)
    {
        var paths = new List<string>();
        if (!string.IsNullOrEmpty(roaming))
            paths.Add(Path.Combine(roaming, "Claude", FileName));
        if (string.IsNullOrEmpty(local))
            return paths;

        var data = Path.Combine(local, "Claude-Data");
        if (Directory.Exists(data))
            paths.AddRange(Directory.GetDirectories(data).Order(StringComparer.OrdinalIgnoreCase)
                .Select(folder => Path.Combine(folder, FileName)));

        var packages = Path.Combine(local, "Packages");
        if (Directory.Exists(packages))
            paths.AddRange(Directory.GetDirectories(packages, "Claude_*").Order(StringComparer.OrdinalIgnoreCase)
                .Select(folder => Path.Combine(folder, "LocalCache", "Roaming", "Claude", FileName)));
        return paths;
    }

    public static bool HasConnector(string? existing, string command)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(existing)
                && JsonNode.Parse(existing, documentOptions: Lenient)?["mcpServers"]?[ServerName]?["command"]?.GetValue<string>() == command;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

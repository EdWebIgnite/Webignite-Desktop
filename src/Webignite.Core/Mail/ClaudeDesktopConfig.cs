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

using System.Text.RegularExpressions;

namespace Webignite.Core.Mail;

/// <summary>What's kept on this PC for ChatGPT's tunnel: the tunnel's id and whether it should run. The key is in Windows Credential Manager.</summary>
public sealed record ChatGptTunnelSettings(string TunnelId = "", bool On = false);

/// <summary>
/// ChatGPT reaches Webignite on this PC through OpenAI's Secure MCP Tunnel: OpenAI's tunnel-client runs here, connects
/// out to OpenAI and starts Webignite's connector for each request. These are the rules for setting it up; running
/// it is in the app.
/// </summary>
public static partial class ChatGptTunnel
{
    public const string TunnelsPage = "https://platform.openai.com/settings/organization/tunnels";
    public const string KeysPage = "https://platform.openai.com/settings/organization/api-keys";
    public const string PluginsPage = "https://chatgpt.com/#settings/Connectors";
    public const string Guide = "https://developers.openai.com/api/docs/guides/secure-mcp-tunnels";
    public const string LatestRelease = "https://api.github.com/repos/openai/tunnel-client/releases/latest";

    /// <summary>Where the running tunnel answers on this PC, for checking it's connected.</summary>
    public const string HealthAddress = "127.0.0.1:47121";

    // Found anywhere in what was pasted: a copy from a web page can bring invisible characters, or words, along with it.
    [GeneratedRegex("tunnel_(?:[a-z0-9]{4}_)?[a-z0-9]{32}(?![a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex TunnelIdPattern();

    /// <summary>The tunnel id as typed, trimmed, or a reason it isn't one ("tunnel_" and 32 small letters or digits, as OpenAI shows it).</summary>
    public static (string? Id, string? Problem) TunnelId(string? typed)
    {
        // Invisible characters a copy can bring (zero-width spaces, a byte-order mark) are taken out first.
        var id = new string((typed ?? "").Where(c => !char.IsWhiteSpace(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray());
        if (id.Length == 0)
            return (null, "Paste the tunnel's ID from OpenAI's Tunnels page. It starts with tunnel_.");
        return TunnelIdPattern().Match(id) is { Success: true } found
            ? (found.Value.ToLowerInvariant(), null)
            : (null, "That isn't a tunnel ID. Copy it again from OpenAI's Tunnels page: it starts with tunnel_ and has 32 letters and digits after it.");
    }

    /// <summary>Null when the key looks right (OpenAI's keys start with sk-), else what's wrong.</summary>
    public static string? KeyProblem(string? typed)
    {
        var key = (typed ?? "").Trim();
        if (key.Length == 0)
            return "Paste the key you made on OpenAI's API keys page.";
        return key.StartsWith("sk-", StringComparison.Ordinal) && key.Length > 20 && !key.Any(char.IsWhiteSpace)
            ? null
            : "That isn't an OpenAI key. It starts with sk-. Copy it again from the API keys page.";
    }

    /// <summary>
    /// Which file of a tunnel-client release to download for this PC: the plain client for Windows on this processor
    /// (not the runtime builds), e.g. tunnel-client-v0.0.12-windows-amd64.zip. Null when the release has none.
    /// </summary>
    public static string? PickDownload(IEnumerable<string> assetNames, bool arm64)
    {
        var platform = arm64 ? "windows-arm64" : "windows-amd64";
        var fits = assetNames
            .Where(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && n.Contains(platform, StringComparison.OrdinalIgnoreCase)
                && !n.Contains("runtime", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return fits.FirstOrDefault(n => n.StartsWith("tunnel-client-v", StringComparison.OrdinalIgnoreCase)) ?? fits.FirstOrDefault();
    }

    /// <summary>The SHA-256 a release's SHA256SUMS.txt gives for a file, lower case, or null when it isn't listed.</summary>
    public static string? Checksum(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split((char[])[' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Length == 64 && string.Equals(parts[1].Trim().TrimStart('*'), fileName, StringComparison.Ordinal))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>
    /// The connector as tunnel-client's --mcp.command wants it: the program's path in quotes, as it may hold spaces,
    /// and with forward slashes, as tunnel-client reads a backslash as an escape and drops it (C:\Users\Ed became
    /// C:UsersEd). Windows takes forward slashes in a path just as well.
    /// </summary>
    public static string Command(string connectorPath) => "\"" + connectorPath.Replace('\\', '/') + "\"";
}

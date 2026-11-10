using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Webignite.Core.Mail;
using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>
/// Runs OpenAI's tunnel-client in the background while Webignite is open, so ChatGPT can reach the connector on this
/// PC: downloads it once from OpenAI's release page (checked against the release's own checksums), starts it with the
/// tunnel's id and key, starts it again if it stops, and stops it when Webignite closes. The key stays in Windows
/// Credential Manager and is handed to the program in its environment, never on its command line.
/// </summary>
public sealed class ChatGptTunnelRunner(AppSettings settings, ISecretStore secrets) : IDisposable
{
    public const string KeyName = "chatgpt-tunnel-key";
    private static readonly HttpClient Http = MakeHttp();
    private Process? process;
    private string lastLines = "";
    private DateTimeOffset lastStart = DateTimeOffset.MinValue;

    /// <summary>Where tunnel-client is kept on this PC.</summary>
    public static string Folder => Path.Combine(DataPaths.Root(), "chatgpt-tunnel");

    public string? InstalledProgram =>
        Directory.Exists(Folder) ? Directory.GetFiles(Folder, OperatingSystem.IsWindows() ? "tunnel-client.exe" : "tunnel-client", SearchOption.AllDirectories).FirstOrDefault() : null;

    public bool IsRunning => process is { HasExited: false };

    public bool HasKey => secrets.Read(KeyName) is { Length: > 0 };

    /// <summary>Saves the tunnel's id and key, downloads tunnel-client if needed and starts it.</summary>
    public async Task<string?> ConnectAsync(string tunnelId, string? key, CancellationToken cancel = default)
    {
        if (!string.IsNullOrWhiteSpace(key))
            secrets.Write(KeyName, key.Trim());
        settings.ChatGptTunnel = new ChatGptTunnelSettings(tunnelId, On: true);
        if (InstalledProgram is null)
            await InstallAsync(cancel);
        Stop();
        return Start();
    }

    /// <summary>Stops the tunnel and keeps it off; ChatGPT can't reach this PC until it's connected again.</summary>
    public void Disconnect()
    {
        settings.ChatGptTunnel = settings.ChatGptTunnel with { On = false };
        Stop();
    }

    /// <summary>Starts it again when it should be running and isn't; run every minute. Waits a minute between tries.</summary>
    public void KeepRunning()
    {
        if (settings.ChatGptTunnel is { On: true } && !IsRunning && InstalledProgram is not null
            && DateTimeOffset.UtcNow - lastStart > TimeSpan.FromMinutes(1))
        {
            if (Start() is { } problem)
                AppLog.Write("ChatGPT's tunnel didn't start: " + problem, null);
        }
    }

    /// <summary>Whether the running tunnel says it's connected to OpenAI and ready.</summary>
    public async Task<bool> ReadyAsync(CancellationToken cancel = default)
    {
        if (!IsRunning)
            return false;
        try
        {
            using var reply = await Http.GetAsync(new Uri($"http://{ChatGptTunnel.HealthAddress}/readyz"), cancel);
            return reply.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>The last lines tunnel-client wrote, for when it stopped.</summary>
    public string LastLines => lastLines;

    private string? Start()
    {
        lastStart = DateTimeOffset.UtcNow;
        var program = InstalledProgram;
        var tunnel = settings.ChatGptTunnel;
        if (program is null)
            return "OpenAI's tunnel program isn't on this PC yet. Click Connect ChatGPT.";
        if (secrets.Read(KeyName) is not { Length: > 0 } key)
            return "The OpenAI key is missing. Paste it and click Connect ChatGPT again.";
        if (!AssistantSetup.ConnectorInstalled())
            return "The connector program isn't next to Webignite.exe. Unzip the whole folder, not just Webignite.exe.";
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Folder,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--mcp.command");
        start.ArgumentList.Add(ChatGptTunnel.Command(AssistantSetup.ConnectorPath()));
        start.ArgumentList.Add("--log.format=struct-text");
        start.Environment["CONTROL_PLANE_API_KEY"] = key;
        start.Environment["CONTROL_PLANE_TUNNEL_ID"] = tunnel.TunnelId;
        start.Environment["HEALTH_LISTEN_ADDR"] = ChatGptTunnel.HealthAddress;
        try
        {
            var started = Process.Start(start) ?? throw new InvalidOperationException("Windows didn't start it.");
            started.OutputDataReceived += (_, e) => Remember(e.Data);
            started.ErrorDataReceived += (_, e) => Remember(e.Data);
            started.BeginOutputReadLine();
            started.BeginErrorReadLine();
            process = started;
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return e.Message;
        }
    }

    // Keeps the last few lines tunnel-client wrote, with the key taken out should it ever show one.
    private void Remember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        if (secrets.Read(KeyName) is { Length: > 0 } key)
            line = line.Replace(key, "sk-…", StringComparison.Ordinal);
        var lines = (lastLines + "\n" + line).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lastLines = string.Join("\n", lines.TakeLast(8));
    }

    private void Stop()
    {
        if (process is { } running)
        {
            try
            {
                if (!running.HasExited)
                    running.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                AppLog.Write("ChatGPT's tunnel didn't stop", e);
            }
            running.Dispose();
        }
        process = null;
    }

    // Downloads the latest tunnel-client for Windows from OpenAI's release page and unpacks it, after checking the
    // download against the release's own SHA256SUMS.txt.
    private static async Task InstallAsync(CancellationToken cancel)
    {
        using var release = JsonDocument.Parse(await Http.GetStringAsync(new Uri(ChatGptTunnel.LatestRelease), cancel));
        var assets = release.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
            .ToList();
        var name = ChatGptTunnel.PickDownload(assets.Select(a => a.Name), RuntimeInformation.OSArchitecture == Architecture.Arm64)
            ?? throw new InvalidOperationException("OpenAI's latest tunnel release has no download for Windows.");
        var sumsUrl = assets.FirstOrDefault(a => a.Name == "SHA256SUMS.txt").Url;
        if (string.IsNullOrEmpty(sumsUrl) || ChatGptTunnel.Checksum(await Http.GetStringAsync(new Uri(sumsUrl), cancel), name) is not { } expected)
            throw new InvalidOperationException("OpenAI's tunnel release has no checksum for its Windows download, so it wasn't installed.");
        var zip = await Http.GetByteArrayAsync(new Uri(assets.First(a => a.Name == name).Url), cancel);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(zip)), expected, StringComparison.Ordinal))
            throw new InvalidOperationException("The tunnel download didn't match OpenAI's checksum, so it wasn't installed. Try again.");
        if (Directory.Exists(Folder))
            Directory.Delete(Folder, recursive: true);
        Directory.CreateDirectory(Folder);
        using var archive = new ZipArchive(new MemoryStream(zip));
        archive.ExtractToDirectory(Folder, overwriteFiles: true);
    }

    private static HttpClient MakeHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // GitHub's API asks every caller to name itself.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Webignite");
        return http;
    }

    public void Dispose() => Stop();
}

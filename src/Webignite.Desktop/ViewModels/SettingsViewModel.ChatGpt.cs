using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

// Settings, Claude and ChatGPT: connecting ChatGPT through OpenAI's Secure MCP Tunnel, step by step. Webignite runs
// OpenAI's tunnel program in the background; the person only makes a tunnel and a key on OpenAI's site and adds the
// connector in ChatGPT.
public partial class SettingsViewModel
{
    [ObservableProperty] public partial string ChatGptTunnelId { get; set; } = "";
    [ObservableProperty] public partial string ChatGptKey { get; set; } = "";
    [ObservableProperty] public partial string ChatGptStatus { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChatGptWorking))]
    public partial bool ChatGptProblem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChatGptWorking))]
    public partial bool ChatGptConnected { get; set; }

    /// <summary>On and without a problem: the status shows as done (green) only then.</summary>
    public bool ChatGptWorking => ChatGptConnected && !ChatGptProblem;
    [ObservableProperty] public partial bool ChatGptBusy { get; set; }

    /// <summary>Whether a key is kept already, so the box can be left empty when connecting again.</summary>
    public bool ChatGptHasKey => services.ChatGpt.HasKey;

    public string ChatGptKeyHint => ChatGptHasKey ? "Kept safely on this PC. Paste a new one only to change it." : "sk-…";

    private void LoadChatGpt()
    {
        var tunnel = services.Settings.ChatGptTunnel;
        ChatGptTunnelId = tunnel.TunnelId;
        ChatGptKey = "";
        ChatGptConnected = tunnel.On;
        ChatGptProblem = false;
        ChatGptStatus = tunnel.On
            ? services.ChatGpt.IsRunning ? "Running on this PC. Checking it's connected to OpenAI…" : "Connected before; starting the tunnel again."
            : "Not connected yet.";
        OnPropertyChanged(nameof(ChatGptHasKey));
        OnPropertyChanged(nameof(ChatGptKeyHint));
        if (tunnel.On)
            CheckChatGptAsync().Forget("Checking ChatGPT's tunnel");
    }

    [RelayCommand]
    private static Task OpenTunnelsPage() => AppServices.OpenInBrowser(new Uri(ChatGptTunnel.TunnelsPage));

    [RelayCommand]
    private static Task OpenKeysPage() => AppServices.OpenInBrowser(new Uri(ChatGptTunnel.KeysPage));

    [RelayCommand]
    private static Task OpenChatGptPlugins() => AppServices.OpenInBrowser(new Uri(ChatGptTunnel.PluginsPage));

    [RelayCommand]
    private async Task ConnectChatGpt()
    {
        if (ChatGptBusy)
            return;
        var (id, idProblem) = ChatGptTunnel.TunnelId(ChatGptTunnelId);
        var keyProblem = ChatGptKey.Length == 0 && ChatGptHasKey ? null : ChatGptTunnel.KeyProblem(ChatGptKey);
        if ((idProblem ?? keyProblem) is { } problem)
        {
            SayChatGpt(problem, problem: true);
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            SayChatGpt("ChatGPT's tunnel runs on Windows.", problem: true);
            return;
        }
        ChatGptBusy = true;
        SayChatGpt(services.ChatGpt.InstalledProgram is null ? "Downloading OpenAI's tunnel program and starting it…" : "Starting the tunnel…", problem: false);
        try
        {
            if (await services.ChatGpt.ConnectAsync(id!, ChatGptKey.Length > 0 ? ChatGptKey : null) is { } notStarted)
            {
                SayChatGpt(notStarted, problem: true);
                return;
            }
            ChatGptKey = "";
            ChatGptConnected = true;
            OnPropertyChanged(nameof(ChatGptHasKey));
            OnPropertyChanged(nameof(ChatGptKeyHint));
            await CheckChatGptAsync();
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException
            or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            SayChatGpt("Couldn't set up the tunnel: " + e.Message, problem: true);
        }
        finally
        {
            ChatGptBusy = false;
        }
    }

    [RelayCommand]
    private void DisconnectChatGpt()
    {
        services.ChatGpt.Disconnect();
        ChatGptConnected = false;
        SayChatGpt("Stopped. ChatGPT can't reach Webignite on this PC until you connect again.", problem: false);
    }

    // Waits a little for the tunnel to reach OpenAI, then says how it stands.
    private async Task CheckChatGptAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            if (await services.ChatGpt.ReadyAsync())
            {
                SayChatGpt("Connected to OpenAI. Now add it in ChatGPT (step 4) if you haven't yet.", problem: false);
                return;
            }
            if (!services.ChatGpt.IsRunning && i > 0)
                break;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        SayChatGpt(services.ChatGpt.IsRunning
            ? "The tunnel is running but isn't connected to OpenAI yet. Check the tunnel ID and key match the same OpenAI organisation."
            : "The tunnel stopped. " + ChatGptStopReason(services.ChatGpt.LastLines), problem: true);
    }

    private static string ChatGptStopReason(string lines) =>
        lines.Length == 0 ? "Check the tunnel ID and key, then connect again." : "It said: " + lines.Split('\n')[^1];

    private void SayChatGpt(string message, bool problem)
    {
        ChatGptStatus = message;
        ChatGptProblem = problem;
    }
}

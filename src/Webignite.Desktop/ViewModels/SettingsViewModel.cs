using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Xero.Auth;

namespace Webignite.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices services;
    private CancellationTokenSource? signIn;

    public SettingsViewModel(AppServices services)
    {
        this.services = services;
        var documents = services.DocumentSettings;
        InvoiceDueDays = documents.InvoiceDueDays.ToString();
        QuoteExpiryDays = documents.QuoteExpiryDays.ToString();
        var xero = services.XeroSettings;
        ClientId = xero.ClientId ?? "";
        UpdateXeroStatus();
        UpdateAssistantStatus();
    }

    // Claude and ChatGPT

    public string ConnectorCommand => "\"" + AssistantSetup.ConnectorPath() + "\"";

    [ObservableProperty] public partial string ClaudeStatus { get; set; } = "";
    [ObservableProperty] public partial bool ClaudeConnected { get; set; }
    [ObservableProperty] public partial bool ClaudeProblem { get; set; }
    [ObservableProperty] public partial string CopyMessage { get; set; } = "";

    public void UpdateAssistantStatus()
    {
        ClaudeConnected = AssistantSetup.ClaudeConnected();
        ClaudeProblem = false;
        ClaudeStatus = !AssistantSetup.ConnectorInstalled()
            ? "The connector program isn't next to Webignite.exe. Unzip the whole folder, not just Webignite.exe."
            : ClaudeConnected ? "Claude Desktop is connected. In Claude, ask something like: what came in from Karoo Coffee this week?"
            : "Not connected yet.";
    }

    [RelayCommand]
    private void ConnectClaude()
    {
        try
        {
            if (!AssistantSetup.ConnectorInstalled())
            {
                UpdateAssistantStatus();
                return;
            }
            var count = AssistantSetup.ConnectClaude();
            if (count == 0)
            {
                ClaudeStatus = "Claude Desktop wasn't found on this PC. Install it from claude.ai/download, open it once, then try again.";
                ClaudeProblem = true;
                return;
            }
            ClaudeConnected = true;
            ClaudeProblem = false;
            ClaudeStatus = "Connected. Quit Claude Desktop completely (also from the tray by the clock) and open it again to load Webignite.";
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            ClaudeStatus = e.Message;
            ClaudeProblem = true;
        }
    }

    [RelayCommand]
    private async Task CopyCommand(Avalonia.Controls.TopLevel? top)
    {
        if (top?.Clipboard is { } clipboard)
        {
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, ConnectorCommand);
            CopyMessage = "Copied.";
        }
    }

    [RelayCommand]
    private static Task OpenTunnelGuide() => AppServices.OpenInBrowser(new Uri("https://developers.openai.com/api/docs/guides/secure-mcp-tunnels"));

    [ObservableProperty]
    public partial string InvoiceDueDays { get; set; }

    [ObservableProperty]
    public partial string QuoteExpiryDays { get; set; }

    [ObservableProperty]
    public partial string DefaultsMessage { get; set; } = "";

    [ObservableProperty]
    public partial string ClientId { get; set; }

    [ObservableProperty]
    public partial string XeroStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsConnecting { get; set; }

    [RelayCommand]
    private void SaveDefaults()
    {
        if (!int.TryParse(InvoiceDueDays, out var due) || due is < 0 or > 365
            || !int.TryParse(QuoteExpiryDays, out var expiry) || expiry is < 0 or > 365)
        {
            DefaultsMessage = "Use whole days between 0 and 365.";
            return;
        }
        services.DocumentSettings = services.DocumentSettings with { InvoiceDueDays = due, QuoteExpiryDays = expiry };
        DefaultsMessage = "Saved. New documents will use these.";
    }

    private bool CanConnect() => !IsConnecting;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task Connect()
    {
        var clientId = ClientId.Trim();
        if (clientId.Length != 32 || !clientId.All(Uri.IsHexDigit))
        {
            XeroStatus = "That doesn't look like a Xero Client ID. It's 32 letters and numbers, from your app at developer.xero.com.";
            return;
        }
        services.XeroSettings = services.XeroSettings with { ClientId = clientId };

        IsConnecting = true;
        XeroStatus = "Finish signing in to Xero in your browser. This page updates by itself.";
        signIn = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var tenants = await services.Xero!.SignInAsync(AppServices.OpenInBrowser, signIn.Token);
            if (tenants.Count == 0)
            {
                XeroStatus = "Signed in, but no Xero organisation was shared. Connect again and tick your organisation.";
                return;
            }
            // Use the first organisation for now. A picker comes if more than one is ever shared.
            var tenant = tenants[0];
            services.XeroSettings = services.XeroSettings with { TenantId = tenant.TenantId, TenantName = tenant.TenantName };
            UpdateXeroStatus();
        }
        catch (OperationCanceledException)
        {
            XeroStatus = "Sign-in took too long. Try again.";
        }
        catch (Exception error) when (error is XeroSignInException or HttpRequestException or System.Net.HttpListenerException)
        {
            XeroStatus = error.Message;
        }
        finally
        {
            IsConnecting = false;
            signIn.Dispose();
            signIn = null;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        services.Xero?.SignOut();
        services.XeroSettings = services.XeroSettings with { TenantId = null, TenantName = null };
        UpdateXeroStatus();
    }

    private void UpdateXeroStatus()
    {
        var xero = services.XeroSettings;
        IsConnected = xero.TenantId is not null && services.Xero?.HasSavedSignIn == true;
        XeroStatus = IsConnected
            ? $"Connected to {xero.TenantName}. Webignite can create drafts only. Approving, sending and payments stay in your hands."
            : "Not connected.";
    }
}

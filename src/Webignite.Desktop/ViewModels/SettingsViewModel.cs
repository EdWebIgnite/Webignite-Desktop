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
        LoadTemplates(services.EmailTemplates);
    }

    // Invoice and quote emails

    [ObservableProperty] public partial string InvoiceSubject { get; set; } = "";
    [ObservableProperty] public partial string InvoiceBody { get; set; } = "";
    [ObservableProperty] public partial string QuoteSubject { get; set; } = "";
    [ObservableProperty] public partial string QuoteBody { get; set; } = "";
    [ObservableProperty] public partial string TemplatesMessage { get; set; } = "";

    public string InvoicePlaceholders => string.Join("  ", DocumentEmail.Placeholders(DocumentKind.Invoice));
    public string QuotePlaceholders => string.Join("  ", DocumentEmail.Placeholders(DocumentKind.Quote));

    private void LoadTemplates(EmailTemplates templates)
    {
        InvoiceSubject = templates.Invoice.Subject;
        InvoiceBody = templates.Invoice.Body;
        QuoteSubject = templates.Quote.Subject;
        QuoteBody = templates.Quote.Body;
    }

    [RelayCommand]
    private void SaveTemplates()
    {
        services.EmailTemplates = new EmailTemplates
        {
            Invoice = new EmailTemplate(InvoiceSubject.Trim(), InvoiceBody.Replace("\r", "").TrimEnd()),
            Quote = new EmailTemplate(QuoteSubject.Trim(), QuoteBody.Replace("\r", "").TrimEnd()),
        };
        TemplatesMessage = $"Saved at {DateTime.Now:HH:mm}.";
    }

    [RelayCommand]
    private void ResetTemplates()
    {
        LoadTemplates(new EmailTemplates());
        TemplatesMessage = "Xero's standard wording is back. Save to keep it.";
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

    // Which Xero account sales go to

    public sealed record SalesAccount(string Code, string Name)
    {
        public override string ToString() => $"{Code}  {Name}";
    }

    public System.Collections.ObjectModel.ObservableCollection<SalesAccount> SalesAccounts { get; } = [];

    [ObservableProperty] public partial SalesAccount? ChosenAccount { get; set; }
    [ObservableProperty] public partial string AccountMessage { get; set; } = "";
    [ObservableProperty] public partial bool AccountProblem { get; set; }

    public string AccountText => services.XeroSettings.AccountCode is { } code
        ? $"Quotes and invoices go to {code} {services.XeroSettings.AccountName}, with no VAT."
        : "Choose the Xero account your sales go to before creating drafts.";

    public bool HasAccounts => SalesAccounts.Count > 0;

    [RelayCommand]
    private async Task LoadAccounts()
    {
        if (services.XeroApi is not { } api)
            return;
        AccountMessage = "Loading accounts from Xero…";
        AccountProblem = false;
        try
        {
            var accounts = await api.GetAsync("Accounts");
            var taxes = await api.GetAsync("TaxRates");
            SalesAccounts.Clear();
            foreach (var a in (accounts["Accounts"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>())
                if (Webignite.Core.Xero.XeroRules.IsSalesAccount(Str(a["Type"]), Str(a["Status"])) && Str(a["Code"]) is { Length: > 0 } code)
                    SalesAccounts.Add(new SalesAccount(code, Str(a["Name"])));
            OnPropertyChanged(nameof(HasAccounts));
            var noVat = (taxes["TaxRates"] as System.Text.Json.Nodes.JsonArray ?? []).Any(t => Str(t?["TaxType"]) == "NONE");
            ChosenAccount = SalesAccounts.FirstOrDefault(a => a.Code == services.XeroSettings.AccountCode)
                ?? SalesAccounts.FirstOrDefault(a => a.Code == "200") ?? SalesAccounts.FirstOrDefault();
            AccountMessage = SalesAccounts.Count == 0 ? "Xero has no active sales accounts with a code."
                : !noVat ? "Xero has no \"No VAT\" tax rate (NONE), which drafts need. Check the tax rates in Xero."
                : "";
            AccountProblem = AccountMessage.Length > 0;
        }
        catch (Exception e) when (e is Webignite.Xero.XeroException or XeroSignInException)
        {
            AccountMessage = e.Message;
            AccountProblem = true;
        }
    }

    [RelayCommand]
    private void SaveAccount()
    {
        if (ChosenAccount is not { } account)
            return;
        services.XeroSettings = services.XeroSettings with { AccountCode = account.Code, AccountName = account.Name };
        AccountMessage = "Saved.";
        AccountProblem = false;
        OnPropertyChanged(nameof(AccountText));
    }

    private static string Str(System.Text.Json.Nodes.JsonNode? node) => node?.GetValue<string>() ?? "";

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

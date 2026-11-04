using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Backups;
using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Data;
using Webignite.Xero.Auth;

namespace Webignite.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IRefreshable, IHoldsWork
{
    /// <summary>Settings stays once opened: it is small and may hold something half typed.</summary>
    public bool HoldsWork => true;

    private readonly AppServices services;

    public SettingsViewModel(AppServices services)
    {
        this.services = services;
        var documents = services.Settings.Documents;
        InvoiceDueDays = documents.InvoiceDueDays.ToString();
        QuoteExpiryDays = documents.QuoteExpiryDays.ToString();
        var xero = services.Settings.Xero;
        ClientId = xero.ClientId ?? "";
        UpdateXeroStatus();
        UpdateAssistantStatus();
        LoadTemplates(services.Settings.EmailTemplates);
        UpdateBackupStatus();
        LoadPrices();
        MotionChoice = MotionChoices.First(c => c.Value == services.Settings.Appearance.Motion);
        services.Settings.AppearanceChanged += LookChanged;
        var mailCheck = services.Settings.MailCheck;
        MailCheckMinutes = NewMail.MinuteChoices.Contains(mailCheck.Minutes) ? mailCheck.Minutes : 5;
        NotifyNewMail = mailCheck.Notify;
        if (App.RestoredAtStart)
            BackupMessage = "The backup was restored. If mail or Xero ask, sign in again: passwords aren't kept in backups.";
    }

    // Price list

    public System.Collections.ObjectModel.ObservableCollection<PriceRow> Prices { get; } = [];
    public bool HasPrices => Prices.Count > 0;

    private void LoadPrices()
    {
        Prices.Clear();
        foreach (var entry in services.Settings.PriceList)
            Prices.Add(new PriceRow(entry));
        OnPropertyChanged(nameof(HasPrices));
    }

    [RelayCommand]
    private void DeletePrice(PriceRow row)
    {
        var at = services.Settings.PriceList.ToList().FindIndex(e => e.Id == row.Entry.Id);
        services.Settings.PriceList = PriceList.Remove(services.Settings.PriceList, row.Entry.Id);
        LoadPrices();
        services.OfferUndo($"Deleted {row.Entry.Name} from the price list", () =>
        {
            services.Settings.PriceList = Webignite.Core.Layout.ListUndo.PutBack(services.Settings.PriceList, row.Entry, at, e => e.Id);
            LoadPrices();
        });
    }

    // Motion

    public static IReadOnlyList<Choice<Motion>> MotionChoices { get; } =
    [
        new("Full: pages fade and rise, buttons and cards ease in", Motion.Full),
        new("Reduced: quick fades only, nothing slides", Motion.Reduced),
        new("Off: everything changes at once", Motion.Off),
    ];

    [ObservableProperty] public partial Choice<Motion>? MotionChoice { get; set; }

    partial void OnMotionChoiceChanged(Choice<Motion>? value)
    {
        if (value is not null && value.Value != services.Settings.Appearance.Motion)
            services.Settings.Appearance = services.Settings.Appearance with { Motion = value.Value };
    }

    // Light and dark

    public bool IsLight => services.Settings.Appearance.Theme == Theme.Light;
    public bool IsDark => services.Settings.Appearance.Theme == Theme.Dark;
    public bool IsFollowingWindows => services.Settings.Appearance.Theme == Theme.FollowWindows;
    public bool IsGraphite => services.Settings.Appearance.DarkPalette == DarkPalette.Graphite;
    public bool IsMidnight => services.Settings.Appearance.DarkPalette == DarkPalette.Midnight;
    public bool IsWarm => services.Settings.Appearance.DarkPalette == DarkPalette.Warm;

    [RelayCommand]
    private void UseTheme(string theme)
    {
        if (Enum.TryParse<Theme>(theme, out var value))
            SetLook(services.Settings.Appearance with { Theme = value });
    }

    /// <summary>Picking a dark look while the app is light switches it to dark, so the choice shows straight away.</summary>
    [RelayCommand]
    private void UseDarkPalette(string palette)
    {
        if (Enum.TryParse<DarkPalette>(palette, out var value))
            SetLook(services.Settings.Appearance with
            {
                DarkPalette = value,
                Theme = services.Settings.Appearance.Theme == Theme.Light ? Theme.Dark : services.Settings.Appearance.Theme,
            });
    }

    private void SetLook(AppearanceSettings look) => services.Settings.Appearance = look;

    private void LookChanged(AppearanceSettings look)
    {
        foreach (var name in new[] { nameof(IsLight), nameof(IsDark), nameof(IsFollowingWindows), nameof(IsGraphite), nameof(IsMidnight), nameof(IsWarm) })
            OnPropertyChanged(name);
    }

    // Mail checking

    public IReadOnlyList<int> MailCheckChoices => NewMail.MinuteChoices;
    [ObservableProperty] public partial int MailCheckMinutes { get; set; } = 5;
    [ObservableProperty] public partial bool NotifyNewMail { get; set; } = true;

    partial void OnMailCheckMinutesChanged(int value) =>
        services.Settings.MailCheck = services.Settings.MailCheck with { Minutes = value };

    partial void OnNotifyNewMailChanged(bool value) =>
        services.Settings.MailCheck = services.Settings.MailCheck with { Notify = value };

    // Backups

    [ObservableProperty] public partial string BackupStatus { get; set; } = "";
    [ObservableProperty] public partial bool BackupProblem { get; set; }
    [ObservableProperty] public partial string BackupFolder { get; set; } = "";
    [ObservableProperty] public partial bool HasBackupPassword { get; set; }
    [ObservableProperty] public partial bool ChangingPassword { get; set; }
    [ObservableProperty] public partial string NewBackupPassword { get; set; } = "";
    [ObservableProperty] public partial string RepeatBackupPassword { get; set; } = "";
    [ObservableProperty] public partial string BackupMessage { get; set; } = "";
    [ObservableProperty] public partial bool BackupMessageIsProblem { get; set; }
    [ObservableProperty] public partial bool BackingUp { get; set; }

    [ObservableProperty] public partial string? RestoreFile { get; set; }
    [ObservableProperty] public partial string RestorePassword { get; set; } = "";
    [ObservableProperty] public partial bool RestoreReady { get; set; }

    public bool ShowPasswordFields => ChangingPassword || !HasBackupPassword;
    public bool ShowRestore => RestoreFile is not null && !RestoreReady;
    public string RestoreFileName => RestoreFile is null ? "" : Path.GetFileName(RestoreFile);

    partial void OnChangingPasswordChanged(bool value) => OnPropertyChanged(nameof(ShowPasswordFields));
    partial void OnHasBackupPasswordChanged(bool value) => OnPropertyChanged(nameof(ShowPasswordFields));
    partial void OnRestoreFileChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowRestore));
        OnPropertyChanged(nameof(RestoreFileName));
    }
    partial void OnRestoreReadyChanged(bool value) => OnPropertyChanged(nameof(ShowRestore));

    /// <summary>Looks again when the page is opened, as backups run in the background.</summary>
    public void Refresh()
    {
        LoadPrices();
        UpdateAssistantStatus();
        UpdateBackupStatus();
    }

    private void UpdateBackupStatus()
    {
        var settings = services.Settings.Backups;
        HasBackupPassword = services.HasBackupPassword;
        BackupFolder = services.BackupFolder;
        var now = DateTimeOffset.Now;
        BackupStatus = BackupRules.Status(settings, HasBackupPassword, now);
        BackupProblem = BackupRules.IsProblem(settings, HasBackupPassword, now);
    }

    private void Say(string message, bool problem = false)
    {
        BackupMessage = message;
        BackupMessageIsProblem = problem;
    }

    [RelayCommand]
    private async Task SaveBackupPassword()
    {
        if (BackupRules.PasswordProblem(NewBackupPassword, RepeatBackupPassword) is { } problem)
        {
            Say(problem, true);
            return;
        }
        services.Secrets.Write(AppServices.BackupPasswordName, NewBackupPassword);
        NewBackupPassword = RepeatBackupPassword = "";
        ChangingPassword = false;
        Say("Saved. Write this password down somewhere safe: without it, no backup can be opened.");
        await BackUpNow();
    }

    [RelayCommand]
    private void ChangePassword() => ChangingPassword = true;

    [RelayCommand]
    private void CancelPassword()
    {
        ChangingPassword = false;
        NewBackupPassword = RepeatBackupPassword = "";
    }

    public void SetBackupFolder(string folder)
    {
        services.Settings.Backups = services.Settings.Backups with { Folder = folder };
        UpdateBackupStatus();
        Say("Backups will go to " + folder + ".");
    }

    [RelayCommand]
    private async Task BackUpNow()
    {
        if (!services.HasBackupPassword)
        {
            Say("Choose a backup password first.", true);
            return;
        }
        BackingUp = true;
        AppServices.BackupOutcome outcome;
        try
        {
            outcome = await services.TryBackUpAsync(now: true);
        }
        finally
        {
            BackingUp = false; // a surprise error still frees the Back up now button
        }
        UpdateBackupStatus();
        if (outcome.Problem is { } problem)
            Say(problem, true);
        else if (outcome.Skipped is { } why)
            Say(why);
        else if (outcome.Made && !BackupMessage.StartsWith("Saved.", StringComparison.Ordinal))
            Say("Backed up.");
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        Directory.CreateDirectory(BackupFolder);
        AppServices.OpenFile(BackupFolder);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(Webignite.Data.AppLog.Folder);
        AppServices.OpenFile(Webignite.Data.AppLog.Folder);
    }

    public void ChooseRestoreFile(string path)
    {
        RestoreFile = path;
        RestorePassword = "";
        RestoreReady = false;
        Say("");
    }

    [RelayCommand]
    private void CancelRestore()
    {
        RestoreFile = null;
        RestorePassword = "";
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (RestoreFile is not { } file)
            return;
        var password = RestorePassword;
        var root = DataPaths.Root();
        try
        {
            var made = await Task.Run(() => Backups.Stage(file, password, root, OperatingSystem.IsWindows() ? VaultKey.Protect : null));
            RestorePassword = "";
            RestoreReady = true;
            Say($"The backup from {made.ToLocalTime():d MMMM yyyy} at {made.ToLocalTime():HH:mm} is unlocked. Restart Webignite to switch to it. Your current data is kept in a \"before-restore\" folder.");
        }
        catch (Exception e) when (e is BackupPasswordException or InvalidDataException or IOException
                                      or System.Security.Cryptography.CryptographicException)
        {
            Say(e.Message, true);
        }
    }

    [RelayCommand]
    private void RestartToRestore()
    {
        App.RestartAfterExit = true;
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow?.Close(); // the usual way out, so open work is saved first
    }

    // Invoice and quote emails

    [ObservableProperty] public partial string InvoiceSubject { get; set; } = "";
    [ObservableProperty] public partial string InvoiceBody { get; set; } = "";
    [ObservableProperty] public partial string QuoteSubject { get; set; } = "";
    [ObservableProperty] public partial string QuoteBody { get; set; } = "";
    [ObservableProperty] public partial string FollowUpSubject { get; set; } = "";
    [ObservableProperty] public partial string FollowUpBody { get; set; } = "";
    [ObservableProperty] public partial string ReminderSubject { get; set; } = "";
    [ObservableProperty] public partial string ReminderBody { get; set; } = "";
    [ObservableProperty] public partial string StatementSubject { get; set; } = "";
    [ObservableProperty] public partial string StatementBody { get; set; } = "";
    [ObservableProperty] public partial string TemplatesMessage { get; set; } = "";

    public string InvoicePlaceholders => string.Join("  ", DocumentEmail.Placeholders(DocumentKind.Invoice));
    public string QuotePlaceholders => string.Join("  ", DocumentEmail.Placeholders(DocumentKind.Quote));

    private void LoadTemplates(EmailTemplates templates)
    {
        InvoiceSubject = templates.Invoice.Subject;
        InvoiceBody = templates.Invoice.Body;
        QuoteSubject = templates.Quote.Subject;
        QuoteBody = templates.Quote.Body;
        FollowUpSubject = templates.FollowUp.Subject;
        FollowUpBody = templates.FollowUp.Body;
        ReminderSubject = templates.Reminder.Subject;
        ReminderBody = templates.Reminder.Body;
        StatementSubject = templates.Statement.Subject;
        StatementBody = templates.Statement.Body;
    }

    [RelayCommand]
    private void SaveTemplates()
    {
        services.Settings.EmailTemplates = new EmailTemplates
        {
            Invoice = new EmailTemplate(InvoiceSubject.Trim(), InvoiceBody.Replace("\r", "").TrimEnd()),
            Quote = new EmailTemplate(QuoteSubject.Trim(), QuoteBody.Replace("\r", "").TrimEnd()),
            FollowUp = new EmailTemplate(FollowUpSubject.Trim(), FollowUpBody.Replace("\r", "").TrimEnd()),
            Reminder = new EmailTemplate(ReminderSubject.Trim(), ReminderBody.Replace("\r", "").TrimEnd()),
            Statement = new EmailTemplate(StatementSubject.Trim(), StatementBody.Replace("\r", "").TrimEnd()),
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
        if (DocumentDefaults.ParseDays(InvoiceDueDays) is not { } due || DocumentDefaults.ParseDays(QuoteExpiryDays) is not { } expiry)
        {
            DefaultsMessage = "Use whole days between 0 and 365.";
            return;
        }
        services.Settings.Documents = services.Settings.Documents with { InvoiceDueDays = due, QuoteExpiryDays = expiry };
        DefaultsMessage = "Saved. New documents will use these.";
    }

    private bool CanConnect() => !IsConnecting;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task Connect()
    {
        var clientId = ClientId.Trim();
        if (!Webignite.Core.Xero.XeroRules.LooksLikeClientId(clientId))
        {
            XeroStatus = "That doesn't look like a Xero Client ID. It's 32 letters and numbers, from your app at developer.xero.com.";
            return;
        }
        services.Settings.Xero = services.Settings.Xero with { ClientId = clientId };

        IsConnecting = true;
        XeroStatus = "Finish signing in to Xero in your browser. This page updates by itself.";
        using var signIn = new CancellationTokenSource(TimeSpan.FromMinutes(5));
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
            services.Settings.Xero = services.Settings.Xero with { TenantId = tenant.TenantId, TenantName = tenant.TenantName };
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
        }
    }

    // Which Xero account sales go to

    public System.Collections.ObjectModel.ObservableCollection<Webignite.Core.Xero.XeroSalesAccount> SalesAccounts { get; } = [];

    [ObservableProperty] public partial Webignite.Core.Xero.XeroSalesAccount? ChosenAccount { get; set; }
    [ObservableProperty] public partial string AccountMessage { get; set; } = "";
    [ObservableProperty] public partial bool AccountProblem { get; set; }

    public string AccountText => services.Settings.Xero.AccountCode is { } code
        ? $"Quotes and invoices go to {code} {services.Settings.Xero.AccountName}, with no VAT."
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
            var accounts = await api.SalesAccountsAsync();
            var noVat = await api.HasNoVatRateAsync();
            SalesAccounts.Clear();
            foreach (var account in accounts)
                SalesAccounts.Add(account);
            OnPropertyChanged(nameof(HasAccounts));
            ChosenAccount = SalesAccounts.FirstOrDefault(a => a.Code == services.Settings.Xero.AccountCode)
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
        services.Settings.Xero = services.Settings.Xero with { AccountCode = account.Code, AccountName = account.Name };
        AccountMessage = "Saved.";
        AccountProblem = false;
        OnPropertyChanged(nameof(AccountText));
    }

    [RelayCommand]
    private void Disconnect()
    {
        services.Xero?.SignOut();
        services.Settings.Xero = services.Settings.Xero with { TenantId = null, TenantName = null };
        UpdateXeroStatus();
    }

    private void UpdateXeroStatus()
    {
        var xero = services.Settings.Xero;
        IsConnected = xero.TenantId is not null && services.Xero?.HasSavedSignIn == true;
        XeroStatus = IsConnected
            ? $"Connected to {xero.TenantName}. Webignite can create drafts only. Approving, sending and payments stay in your hands."
            : "Not connected.";
    }
}

/// <summary>One package on the Settings page.</summary>
public sealed record PriceRow(PriceEntry Entry)
{
    public string Name => Entry.Name;
    public string Summary => (Entry.Lines.Count == 1 ? "1 line, " : $"{Entry.Lines.Count} lines, ") + Money.Rand(Entry.Total);
    public string LinesText => string.Join(" · ", Entry.Lines.Select(l => l.Description));
}

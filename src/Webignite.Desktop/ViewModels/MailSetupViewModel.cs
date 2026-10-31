using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Adding and changing mailboxes. The password is typed here, checked against the server, and kept in
/// Windows Credential Manager. It is never written to the database or shown again.
/// </summary>
public partial class MailSetupViewModel(AppServices services, Action<bool> done) : ViewModelBase
{
    private MailAccount? editing;

    public ObservableCollection<MailAccount> Existing { get; } = [];
    public bool HasExisting => Existing.Count > 0;
    public bool IsOpen { get; private set; }

    [ObservableProperty] public partial string Heading { get; set; } = "";
    [ObservableProperty] public partial string Address { get; set; } = "";
    [ObservableProperty] public partial string DisplayName { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string ImapHost { get; set; } = "";
    [ObservableProperty] public partial string ImapPort { get; set; } = "993";
    [ObservableProperty] public partial string SmtpHost { get; set; } = "";
    [ObservableProperty] public partial string SmtpPort { get; set; } = "465";
    [ObservableProperty] public partial string Signature { get; set; } = "";
    [ObservableProperty] public partial bool SendsInvoices { get; set; }
    [ObservableProperty] public partial bool AssistantsMayRead { get; set; } = true;
    [ObservableProperty] public partial bool ShowServers { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool MessageIsProblem { get; set; }
    [ObservableProperty] public partial bool IsChecking { get; set; }
    [ObservableProperty] public partial bool ConfirmingRemove { get; set; }

    public bool IsEditing => editing is not null;
    public string PasswordHint => editing is null ? "The mailbox password" : "Leave empty to keep the saved password";
    public bool CanCancel => services.Store.ListMailAccounts().Count > 0;

    partial void OnAddressChanged(string value)
    {
        if (editing is not null || !value.Contains('@'))
            return;
        var suggested = MailAccounts.Suggest(value, DisplayName);
        ImapHost = suggested.ImapHost;
        SmtpHost = suggested.SmtpHost;
        SendsInvoices = suggested.SendsInvoices;
    }

    public void StartNew()
    {
        editing = null;
        IsOpen = true;
        Heading = services.Store.ListMailAccounts().Count == 0 ? "Add your first mailbox" : "Add a mailbox";
        Address = DisplayName = Password = Signature = Message = "";
        ImapHost = SmtpHost = "";
        ImapPort = "993";
        SmtpPort = "465";
        SendsInvoices = false;
        AssistantsMayRead = true;
        ShowServers = false;
        ConfirmingRemove = false;
        Refresh();
    }

    public void Edit(MailAccount account)
    {
        editing = account;
        IsOpen = true;
        Heading = account.Address;
        Address = account.Address;
        DisplayName = account.DisplayName;
        Password = "";
        ImapHost = account.ImapHost;
        ImapPort = account.ImapPort.ToString();
        SmtpHost = account.SmtpHost;
        SmtpPort = account.SmtpPort.ToString();
        Signature = account.Signature ?? "";
        SendsInvoices = account.SendsInvoices;
        AssistantsMayRead = account.AssistantsMayRead;
        ShowServers = false;
        Message = services.Mail.HasPassword(account) ? "" : "The password isn't saved on this PC yet.";
        MessageIsProblem = Message.Length > 0;
        ConfirmingRemove = false;
        Refresh();
    }

    private void Refresh()
    {
        Existing.Clear();
        foreach (var account in services.Store.ListMailAccounts())
            Existing.Add(account);
        OnPropertyChanged(nameof(HasExisting));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(PasswordHint));
        OnPropertyChanged(nameof(CanCancel));
    }

    [RelayCommand]
    private void Pick(MailAccount account) => Edit(account);

    [RelayCommand]
    private void AddAnother() => StartNew();

    private MailAccount Build() => (editing ?? new MailAccount
    {
        Id = MailAccount.NewId(),
        DisplayName = "",
        Address = "",
        ImapHost = "",
        SmtpHost = "",
        Created = DateTimeOffset.UtcNow,
    }) with
    {
        Address = Address.Trim().ToLowerInvariant(),
        DisplayName = DisplayName.Trim(),
        ImapHost = ImapHost.Trim(),
        ImapPort = int.TryParse(ImapPort, out var imap) ? imap : 0,
        SmtpHost = SmtpHost.Trim(),
        SmtpPort = int.TryParse(SmtpPort, out var smtp) ? smtp : 0,
        Signature = string.IsNullOrWhiteSpace(Signature) ? null : Signature.TrimEnd(),
        SendsInvoices = SendsInvoices,
        AssistantsMayRead = AssistantsMayRead,
    };

    [RelayCommand]
    private async Task Save()
    {
        var account = Build();
        if (MailAccounts.Check(account, services.Store.ListMailAccounts()).FirstOrDefault() is { } problem)
        {
            Say(problem, true);
            return;
        }
        var needsPassword = editing is null || !services.Mail.HasPassword(account);
        if (Password.Length == 0 && needsPassword)
        {
            Say("Type the mailbox password.", true);
            return;
        }

        var serverChanged = editing is null || editing.ImapHost != account.ImapHost || editing.SmtpHost != account.SmtpHost
            || editing.ImapPort != account.ImapPort || editing.SmtpPort != account.SmtpPort || editing.Address != account.Address;
        if (Password.Length > 0 || serverChanged)
        {
            IsChecking = true;
            Say("Checking with the mail server…", false);
            var password = Password.Length > 0 ? Password : services.Secrets.Read(MailService.SecretName(editing!)) ?? "";
            var error = await MailService.TestAsync(account, password);
            IsChecking = false;
            if (error is not null)
            {
                Say(error, true);
                return;
            }
            if (editing is not null && editing.Address != account.Address)
                services.Secrets.Delete(MailService.SecretName(editing));
            services.Mail.SavePassword(account, password);
        }

        services.Store.SaveMailAccount(account);
        Password = "";
        IsOpen = false;
        done(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Password = "";
        IsOpen = false;
        done(false);
    }

    [RelayCommand]
    private void AskRemove() => ConfirmingRemove = true;

    [RelayCommand]
    private void KeepIt() => ConfirmingRemove = false;

    /// <summary>Removes the mailbox and its copy from this PC. The mail on the server is not touched.</summary>
    [RelayCommand]
    private async Task Remove()
    {
        if (editing is null)
            return;
        await services.Mail.RemoveAccountAsync(editing);
        IsOpen = false;
        done(true);
    }

    private void Say(string message, bool problem)
    {
        Message = message;
        MessageIsProblem = problem;
    }
}

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Accounts;

namespace Webignite.Desktop.ViewModels;

// Settings, Account and sync: the sync server, signing in or creating an account (or joining with an invite),
// two-factor, and inviting your partner. Passwords are cleared from the screen as soon as they're used.
public partial class SettingsViewModel
{
    [ObservableProperty] public partial string SyncServer { get; set; } = "";
    [ObservableProperty] public partial string AccountEmail { get; set; } = "";
    [ObservableProperty] public partial string AccountPassword { get; set; } = "";
    [ObservableProperty] public partial string AccountPasswordAgain { get; set; } = "";
    [ObservableProperty] public partial string NewWorkspaceName { get; set; } = "";
    [ObservableProperty] public partial string JoinCode { get; set; } = "";
    [ObservableProperty] public partial string SignInCode { get; set; } = "";

    /// <summary>What the form is for: signing in, a new account with its own workspace, or joining with an invite.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignInForm), nameof(IsNewAccountForm), nameof(IsJoinForm), nameof(IsCreating), nameof(IsRecoverForm), nameof(NeedsNewPassword))]
    public partial string AccountForm { get; set; } = "sign-in";

    public bool IsSignInForm => AccountForm == "sign-in";
    public bool IsNewAccountForm => AccountForm == "new";
    public bool IsJoinForm => AccountForm == "join";
    public bool IsRecoverForm => AccountForm == "recover";
    public bool IsCreating => IsNewAccountForm || IsJoinForm;

    /// <summary>The second password box: for a new account, joining, or a new password after recovery.</summary>
    public bool NeedsNewPassword => !IsSignInForm;

    [ObservableProperty] public partial string RecoveryKeyTyped { get; set; } = "";

    /// <summary>The recovery key of a new account, shown once until it's been kept.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewRecoveryKey))]
    public partial string NewRecoveryKey { get; private set; } = "";

    public bool HasNewRecoveryKey => NewRecoveryKey.Length > 0;

    [ObservableProperty] public partial bool HasWorkspaceKey { get; private set; }

    [ObservableProperty] public partial bool NeedsSignInCode { get; private set; }
    [ObservableProperty] public partial bool AccountBusy { get; private set; }
    [ObservableProperty] public partial string SyncMessage { get; private set; } = "";
    [ObservableProperty] public partial bool SyncProblem { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    public partial bool IsSignedIn { get; private set; }

    public bool IsSignedOut => !IsSignedIn;

    [ObservableProperty] public partial string SignedInAs { get; private set; } = "";
    [ObservableProperty] public partial string WorkspaceMembers { get; private set; } = "";
    [ObservableProperty] public partial bool IsWorkspaceOwner { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TwoFactorOff))]
    public partial bool TwoFactorOn { get; private set; }

    public bool TwoFactorOff => !TwoFactorOn;

    // Setting up two-factor: the secret, its QR code for the phone, and the code typed back.
    [ObservableProperty] public partial string TwoFactorSecret { get; private set; } = "";
    [ObservableProperty] public partial Bitmap? TwoFactorQr { get; private set; }
    [ObservableProperty] public partial bool SettingUpTwoFactor { get; private set; }
    [ObservableProperty] public partial string TwoFactorCode { get; set; } = "";
    [ObservableProperty] public partial string RecoveryCodesText { get; private set; } = "";
    public bool HasRecoveryCodes => RecoveryCodesText.Length > 0;

    [ObservableProperty] public partial string InviteText { get; private set; } = "";

    /// <summary>Where this workspace's licence stands: the trial's days left, paid until, or run out.</summary>
    [ObservableProperty] public partial string LicenceText { get; private set; } = "";
    [ObservableProperty] public partial bool LicenceProblem { get; private set; }

    private void ShowLicence()
    {
        if (!services.Account.ChecksLicences)
        {
            LicenceText = "Licences aren't set up in this copy of Webignite yet.";
            LicenceProblem = false;
            return;
        }
        var standing = Licences.Standing(services.Account.Licence, DateTimeOffset.UtcNow);
        LicenceText = standing.Text;
        LicenceProblem = standing.State is LicenceState.Grace or LicenceState.Locked;
    }

    /// <summary>When shared projects last synced on this PC, in the PC's own way of writing dates.</summary>
    [ObservableProperty] public partial string LastSyncedText { get; private set; } = "";
    public bool HasInvite => InviteText.Length > 0;

    partial void OnRecoveryCodesTextChanged(string value) => OnPropertyChanged(nameof(HasRecoveryCodes));
    partial void OnInviteTextChanged(string value) => OnPropertyChanged(nameof(HasInvite));

    private void LoadAccount()
    {
        var account = services.Account;
        SyncServer = account.Settings.ServerAddress;
        if (AccountEmail.Length == 0)
            AccountEmail = account.Settings.Email;
        IsSignedIn = account.IsSignedIn;
        HasWorkspaceKey = account.HasWorkspaceKey;
        ShowAccountInfo(account.Account);
        ShowLastSynced();
        ShowLicence();
        if (IsSignedIn && account.Account is null)
            RefreshAccountAsync().Forget("Reading the account from the sync server");
    }

    private async Task RefreshAccountAsync()
    {
        var reply = await services.Account.RefreshAccountAsync();
        IsSignedIn = services.Account.IsSignedIn;
        ShowAccountInfo(reply.Value);
        if (!reply.Worked)
            SaySync(reply.Problem!, problem: true);
    }

    private void ShowAccountInfo(AccountInfo? account)
    {
        if (account is null)
            return;
        SignedInAs = $"Signed in as {account.Email}, in the workspace {account.Workspace.Name}.";
        WorkspaceMembers = string.Join(", ", account.Workspace.Members.Select(m => m.Role == AccountRoles.Owner ? $"{m.Email} (owner)" : m.Email));
        IsWorkspaceOwner = account.Workspace.Role == AccountRoles.Owner;
        TwoFactorOn = account.TwoFactor;
    }

    private void ShowLastSynced() =>
        LastSyncedText = IsSignedIn && services.Sync.State.LastSynced is { } at
            ? $"Last synced {at.ToLocalTime():g}."
            : "Not synced on this PC yet.";

    [RelayCommand]
    private Task SyncNow() => Busy(async () =>
    {
        var result = await services.SyncNowAsync();
        SaySync(result.Summary(), problem: result.Problem is not null || result.Unreadable > 0);
        ShowLastSynced();
        ShowLicence();
    });

    private void SaySync(string message, bool problem = false)
    {
        SyncMessage = message;
        SyncProblem = problem;
    }

    [RelayCommand]
    private void ShowAccountForm(string form)
    {
        AccountForm = form;
        NeedsSignInCode = false;
        SaySync("");
    }

    // Runs one account step at a time, with the server's address checked first.
    private async Task Busy(Func<Task> step)
    {
        if (AccountBusy)
            return;
        if (services.Account.UseServer(SyncServer) is { } problem)
        {
            SaySync(problem, problem: true);
            return;
        }
        AccountBusy = true;
        SaySync("");
        try
        {
            await step();
        }
        finally
        {
            AccountBusy = false;
        }
    }

    [RelayCommand]
    private Task SignIn() => Busy(async () =>
    {
        var reply = await services.Account.SignInAsync(AccountEmail, AccountPassword, NeedsSignInCode ? SignInCode : null);
        if (reply.Need == Needs.Code)
        {
            NeedsSignInCode = true;
            SaySync(reply.Problem!, problem: SignInCode.Length > 0);
            return;
        }
        Signed(reply.Value, reply.Problem);
    });

    [RelayCommand]
    private Task CreateAccount() => Busy(async () =>
    {
        if (AccountPassword != AccountPasswordAgain)
        {
            SaySync("The two passwords aren't the same.", problem: true);
            return;
        }
        var reply = await services.Account.SignUpAsync(AccountEmail, AccountPassword, IsNewAccountForm ? NewWorkspaceName : null, IsJoinForm ? JoinCode : null);
        Signed(reply.Value?.Account, reply.Problem);
        if (reply.Value is { } signedUp)
        {
            NewRecoveryKey = signedUp.RecoveryKey;
            SaySync("Your account is ready. Write down or print your recovery key below and keep it somewhere safe: it's the only way back in if you forget your password. It won't be shown again.");
        }
    });

    [RelayCommand]
    private Task Recover() => Busy(async () =>
    {
        if (AccountPassword != AccountPasswordAgain)
        {
            SaySync("The two passwords aren't the same.", problem: true);
            return;
        }
        var reply = await services.Account.RecoverAsync(AccountEmail, RecoveryKeyTyped, AccountPassword);
        Signed(reply.Value, reply.Problem);
        if (reply.Worked)
        {
            RecoveryKeyTyped = "";
            SaySync("Your new password is set and you're signed in. Your other PCs were signed out; sign in there with the new password.");
        }
    });

    /// <summary>The recovery key has been written down: it goes from the screen.</summary>
    [RelayCommand]
    private void RecoveryKeyKept() => NewRecoveryKey = "";

    private void Signed(AccountInfo? account, string? problem)
    {
        if (account is null)
        {
            SaySync(problem ?? "That didn't work.", problem: true);
            return;
        }
        AccountPassword = AccountPasswordAgain = SignInCode = JoinCode = "";
        NeedsSignInCode = false;
        IsSignedIn = true;
        HasWorkspaceKey = services.Account.HasWorkspaceKey;
        ShowAccountInfo(account);
        SaySync(account.TwoFactor ? "Signed in." : "Signed in. Turn on two-factor below, so your password alone isn't enough to get in.");
    }

    [RelayCommand]
    private Task SignOut() => Busy(async () =>
    {
        await services.Account.SignOutAsync();
        IsSignedIn = false;
        HasWorkspaceKey = false;
        NewRecoveryKey = "";
        SignedInAs = WorkspaceMembers = InviteText = RecoveryCodesText = "";
        ClearTwoFactorSetup();
        SaySync("Signed out on this PC.");
    });

    [RelayCommand]
    private Task StartTwoFactor() => Busy(async () =>
    {
        var reply = await services.Account.StartTwoFactorAsync();
        if (reply.Value is not { } setup)
        {
            SaySync(reply.Problem!, problem: true);
            return;
        }
        ClearTwoFactorSetup();
        TwoFactorSecret = setup.Secret;
        using (var stream = new MemoryStream(Webignite.Tools.QrCodes.Png(setup.Uri, 6)))
            TwoFactorQr = new Bitmap(stream);
        SettingUpTwoFactor = true;
        SaySync("Scan the code with your authenticator app (or type the key into it), then type the six digits it shows.");
    });

    [RelayCommand]
    private Task ConfirmTwoFactor() => Busy(async () =>
    {
        var reply = await services.Account.ConfirmTwoFactorAsync(TwoFactorCode);
        if (reply.Value is not { } codes)
        {
            SaySync(reply.Problem!, problem: true);
            return;
        }
        ClearTwoFactorSetup();
        RecoveryCodesText = string.Join(Environment.NewLine, codes.Codes);
        ShowAccountInfo(services.Account.Account);
        SaySync("Two-factor is on. Keep the recovery codes below somewhere safe: each one gets you in once if you lose your phone. They won't be shown again.");
    });

    [RelayCommand]
    private Task StopTwoFactor() => Busy(async () =>
    {
        var reply = await services.Account.StopTwoFactorAsync(TwoFactorCode);
        if (!reply.Worked)
        {
            SaySync(reply.Problem!, problem: true);
            return;
        }
        TwoFactorCode = "";
        ShowAccountInfo(services.Account.Account);
        SaySync("Two-factor is off.");
    });

    private void ClearTwoFactorSetup()
    {
        var old = TwoFactorQr;
        TwoFactorQr = null;
        old?.Dispose();
        TwoFactorSecret = TwoFactorCode = "";
        SettingUpTwoFactor = false;
    }

    [RelayCommand]
    private Task InvitePartner() => Busy(async () =>
    {
        var reply = await services.Account.InviteAsync();
        if (reply.Value is not { } invite)
        {
            SaySync(reply.Problem!, problem: true);
            return;
        }
        InviteText = invite.Code;
        SaySync($"Give this whole code to your partner, in a message only they can see: it also unlocks your shared projects. In Webignite they choose Join with an invite. It works once, until {invite.Expires.ToLocalTime():d MMMM}.");
    });
}

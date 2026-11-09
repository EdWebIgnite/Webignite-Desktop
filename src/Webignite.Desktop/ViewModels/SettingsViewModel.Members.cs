using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Accounts;

namespace Webignite.Desktop.ViewModels;

/// <summary>Who's in the workspace, taking someone out, signing out other PCs and a new password.</summary>
public partial class SettingsViewModel
{
    public ObservableCollection<MemberRowViewModel> Members { get; } = [];

    /// <summary>The person the owner asked to take out, waiting for a yes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmingRemove), nameof(RemoveQuestion))]
    public partial string? MemberToRemove { get; set; }

    public bool ConfirmingRemove => MemberToRemove is not null;

    public string RemoveQuestion => MemberToRemove is { } email
        ? $"Take {email} out of the workspace? They can't sign in or sync any more, and any invite still open stops working. What they already have on their PC stays there."
        : "";

    [ObservableProperty] public partial bool ChangingAccountPassword { get; set; }
    [ObservableProperty] public partial string PasswordNow { get; set; } = "";
    [ObservableProperty] public partial string PasswordNew { get; set; } = "";
    [ObservableProperty] public partial string PasswordNewAgain { get; set; } = "";

    private void ShowMembers(AccountInfo account)
    {
        Members.Clear();
        var owner = account.Workspace.Role == AccountRoles.Owner;
        foreach (var member in account.Workspace.Members)
            Members.Add(new MemberRowViewModel(member.Email, member.Role == AccountRoles.Owner,
                owner && member.Role != AccountRoles.Owner && member.Email != account.Email, this));
    }

    internal void AskRemove(string email) => MemberToRemove = email;

    [RelayCommand]
    private void KeepMember() => MemberToRemove = null;

    [RelayCommand]
    private Task RemoveMember() => Busy(async () =>
    {
        if (MemberToRemove is not { } email)
            return;
        MemberToRemove = null;
        var reply = await services.Account.RemoveMemberAsync(email);
        if (reply.Value is not { } account)
        {
            SaySync(reply.Problem ?? "That didn't work.", problem: true);
            return;
        }
        ShowAccountInfo(account);
        SaySync($"{email} is out of the workspace. They can't sign in or sync any more.");
    });

    [RelayCommand]
    private Task SignOutOtherPcs() => Busy(async () =>
    {
        var reply = await services.Account.SignOutOtherPcsAsync();
        SaySync(reply.Worked ? "Every other PC on your account is signed out. This one stays signed in." : reply.Problem ?? "That didn't work.", problem: !reply.Worked);
    });

    [RelayCommand]
    private void StartChangeAccountPassword()
    {
        PasswordNow = PasswordNew = PasswordNewAgain = "";
        ChangingAccountPassword = true;
    }

    [RelayCommand]
    private void CancelChangeAccountPassword()
    {
        PasswordNow = PasswordNew = PasswordNewAgain = "";
        ChangingAccountPassword = false;
    }

    [RelayCommand]
    private Task ChangeAccountPassword() => Busy(async () =>
    {
        if (PasswordNew != PasswordNewAgain)
        {
            SaySync("The two new passwords aren't the same.", problem: true);
            return;
        }
        var reply = await services.Account.ChangePasswordAsync(PasswordNow, PasswordNew);
        if (!reply.Worked)
        {
            SaySync(reply.Problem ?? "That didn't work.", problem: true);
            return;
        }
        PasswordNow = PasswordNew = PasswordNewAgain = "";
        ChangingAccountPassword = false;
        SaySync("Your password is changed. Your other PCs were signed out; sign in there with the new password. Your recovery key still works.");
    });
}

/// <summary>One person in the workspace.</summary>
public sealed partial class MemberRowViewModel(string email, bool owner, bool canRemove, SettingsViewModel page) : ViewModelBase
{
    public string Email => email;
    public string Role => owner ? "Owner" : "Member";
    public bool CanRemove => canRemove;

    [RelayCommand]
    private void Remove() => page.AskRemove(email);
}

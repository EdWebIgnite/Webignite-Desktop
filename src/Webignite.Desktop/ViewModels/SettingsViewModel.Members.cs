using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Accounts;
using Webignite.Core.Chat;

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

    /// <summary>The workspace's name and how many are in it, at the top of Your organisation.</summary>
    [ObservableProperty] public partial string OrganisationText { get; private set; } = "";

    // Everyone in the workspace, me first, with the names this PC knows them by (my partner's as their PC sent it)
    // and their status. Signed out, the two of us as this PC has us.
    private void ShowMembers(AccountInfo? account)
    {
        Members.Clear();
        var team = services.Team();
        var chat = services.Chat;
        if (account is null)
        {
            OrganisationText = "Not signed in. Sign in under Account and sync to share projects and chat with your partner.";
            Members.Add(new MemberRowViewModel(team.Me!.Name, "", "", true, PresenceState.Offline, false, this));
            Members.Add(new MemberRowViewModel(team.Partner!.Name, team.Partner.Email ?? "", "", false, PresenceState.Offline, false, this));
            return;
        }
        var owner = account.Workspace.Role == AccountRoles.Owner;
        var count = account.Workspace.Members.Count;
        OrganisationText = $"{account.Workspace.Name}: {count} {(count == 1 ? "person" : "people")}";
        foreach (var member in account.Workspace.Members.OrderBy(m => string.Equals(m.Email, account.Email, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            var isMe = string.Equals(member.Email, account.Email, StringComparison.OrdinalIgnoreCase);
            var role = member.Role == AccountRoles.Owner ? "Owner" : "Member";
            Members.Add(new MemberRowViewModel(isMe ? team.Me!.Name : team.Partner!.Name, member.Email, role, isMe,
                isMe ? chat.MyShown : chat.PartnerShown, owner && member.Role != AccountRoles.Owner && !isMe, this));
        }
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

/// <summary>One person in the workspace: their initials, name, email, role and chat status.</summary>
public sealed partial class MemberRowViewModel(string name, string email, string role, bool isMe, PresenceState shown, bool canRemove, SettingsViewModel page) : ViewModelBase
{
    public string Name => name;
    public string Email => email;
    public bool HasEmail => email.Length > 0;
    public string Role => role;
    public bool IsMe => isMe;
    public PresenceState Shown => shown;
    public string ShownText => PresenceRules.Word(shown);
    public bool CanRemove => canRemove;

    [RelayCommand]
    private void Remove() => page.AskRemove(email);
}

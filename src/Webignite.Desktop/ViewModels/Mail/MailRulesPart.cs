using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>Mail rules: run on new inbox mail after each check, or once on the open folder.</summary>
public sealed partial class MailRulesPart(AppServices services, MailViewModel page) : ViewModelBase
{
    private MailRulesViewModel? rulesPage;

    /// <summary>The page for adding and changing rules, made when first opened.</summary>
    public MailRulesViewModel RulesPage => rulesPage ??= new MailRulesViewModel(page);

    [ObservableProperty]
    public partial bool ShowRules { get; set; }

    // Rules take the place of the mail itself on the page, so the page says when that changes.
    partial void OnShowRulesChanged(bool value) => page.RulesShownChanged();

    [RelayCommand]
    private void ManageRules()
    {
        RulesPage.Load();
        ShowRules = true;
    }

    internal void CloseRules() => ShowRules = false;

    /// <summary>What "Run the rules now" works on: the open folder, or every inbox from All inboxes. Null for other lists.</summary>
    internal string? RulesTarget => page.CurrentCategory is not null || page.Search.Trim().Length > 0 ? null : page.CurrentFolder switch
    {
        { Kind: FolderKind.Folder } f => f.Name,
        { Kind: FolderKind.AllInboxes } => "all inboxes",
        _ => null,
    };

    /// <summary>Runs every rule once on the mail in the open folder, or in every inbox. Returns how many mails changed.</summary>
    internal async Task<int> RunRulesNow()
    {
        IEnumerable<MailSummary> mail = page.CurrentFolder switch
        {
            { Kind: FolderKind.Folder } f => services.Store.ListMailSummaries(f.Mailbox.Id, f.Path, 5000),
            { Kind: FolderKind.AllInboxes } => page.Accounts.SelectMany(a => services.Store.ListMailFolders(a.Id).Where(f => f.Role == FolderRole.Inbox)
                .SelectMany(f => services.Store.ListMailSummaries(a.Id, f.Path, 5000))),
            _ => [],
        };
        var outcomes = MailRules.Apply(services.Settings.MailRules.Rules, mail);
        if (outcomes.Count > 0)
            await services.MailCheck.CarryAsync(outcomes, e => page.SetStatus("A mail rule couldn't finish: " + e, true));
        return outcomes.Count;
    }
}

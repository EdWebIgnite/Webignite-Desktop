using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>Mail rules: run on new inbox mail after each check, or once on the open folder.</summary>
public partial class MailViewModel
{
    private MailRulesViewModel? rulesPage;

    /// <summary>The page for adding and changing rules, made when first opened.</summary>
    public MailRulesViewModel RulesPage => rulesPage ??= new MailRulesViewModel(this);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMail))]
    public partial bool ShowRules { get; set; }

    [RelayCommand]
    private void ManageRules()
    {
        RulesPage.Load();
        ShowRules = true;
    }

    internal void CloseRules() => ShowRules = false;

    /// <summary>What "Run the rules now" works on: the open folder, or every inbox from All inboxes. Null for other lists.</summary>
    internal string? RulesTarget => CurrentCategory is not null || Search.Trim().Length > 0 ? null : CurrentFolder switch
    {
        { Kind: FolderKind.Folder } f => f.Name,
        { Kind: FolderKind.AllInboxes } => "all inboxes",
        _ => null,
    };

    /// <summary>Runs the rules on new inbox mail in every mailbox. The first look at a mailbox only notes where its inbox is up to.</summary>
    private async Task RunRulesOnArrival()
    {
        var settings = services.Settings.MailRules;
        var marks = settings.RanUpTo.ToDictionary();
        var outcomes = new List<RuleOutcome>();
        foreach (var account in accounts)
        {
            if (services.Store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == FolderRole.Inbox) is not { } inbox)
                continue;
            var (fresh, ranUpTo) = MailRules.Arrived(services.Store.ListMailSummaries(account.Id, inbox.Path, 500),
                settings.RanUpTo.TryGetValue(account.Id, out var mark) ? mark : null);
            marks[account.Id] = ranUpTo;
            outcomes.AddRange(MailRules.Apply(settings.Rules, fresh));
        }
        services.Settings.MailRules = services.Settings.MailRules with { RanUpTo = marks };
        if (outcomes.Count > 0)
            await Carry(outcomes);
    }

    /// <summary>Runs every rule once on the mail in the open folder, or in every inbox. Returns how many mails changed.</summary>
    internal async Task<int> RunRulesNow()
    {
        IEnumerable<MailSummary> mail = CurrentFolder switch
        {
            { Kind: FolderKind.Folder } f => services.Store.ListMailSummaries(f.Mailbox.Id, f.Path, 5000),
            { Kind: FolderKind.AllInboxes } => accounts.SelectMany(a => services.Store.ListMailFolders(a.Id).Where(f => f.Role == FolderRole.Inbox)
                .SelectMany(f => services.Store.ListMailSummaries(a.Id, f.Path, 5000))),
            _ => [],
        };
        var outcomes = MailRules.Apply(services.Settings.MailRules.Rules, mail);
        if (outcomes.Count > 0)
            await Carry(outcomes);
        return outcomes.Count;
    }

    /// <summary>Does what the rules decided, one mailbox at a time: marks and categories first, then the moves.</summary>
    private async Task Carry(IReadOnlyList<RuleOutcome> outcomes)
    {
        foreach (var group in outcomes.GroupBy(o => o.Mail.AccountId))
        {
            if (accounts.FirstOrDefault(a => a.Id == group.Key) is not { } account)
                continue;
            var list = group.ToList();
            await Run(async () =>
            {
                if (list.Where(o => o.MarkRead).Select(o => o.Mail).ToList() is { Count: > 0 } read)
                    await services.Mail.MarkAsync(account, read, MailMark.Read, true);
                if (list.Where(o => o.MarkImportant).Select(o => o.Mail).ToList() is { Count: > 0 } important)
                    await services.Mail.MarkAsync(account, important, MailMark.Important, true);
                foreach (var keyword in list.SelectMany(o => o.Categories).Distinct(StringComparer.OrdinalIgnoreCase))
                    await services.Mail.SetCategoryAsync(account, list.Where(o => o.Categories.Contains(keyword, StringComparer.OrdinalIgnoreCase)).Select(o => o.Mail).ToList(), keyword, true);
                foreach (var move in list.Where(o => o.MoveTo is not null).GroupBy(o => o.MoveTo!))
                    await services.Mail.MoveAsync(account, move.Select(o => o.Mail).ToList(), move.Key);
            }, quiet: true, onError: e => SetStatus("A mail rule couldn't finish: " + e, true));
        }
    }
}

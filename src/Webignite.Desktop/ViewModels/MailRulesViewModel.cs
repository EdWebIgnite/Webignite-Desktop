using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Rules page on Mail: the rules in plain sentences, each one on or off, and a form to add one.
/// Rules are kept on this PC; the rule itself lives in Core.Mail.MailRules.
/// </summary>
public partial class MailRulesViewModel(MailViewModel owner) : ViewModelBase
{
    private AppServices Services => owner.Services;

    public ObservableCollection<MailRuleRowViewModel> Rules { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];
    public bool HasRules => Rules.Count > 0;
    public bool HasProblems => Problems.Count > 0;

    public static IReadOnlyList<Choice<RuleMatch>> MatchChoices { get; } =
    [
        new("Mail from the domain", RuleMatch.Domain),
        new("Sender's address has", RuleMatch.Sender),
        new("Subject has", RuleMatch.Subject),
        new("Sent to an address with", RuleMatch.To),
    ];

    public IReadOnlyList<Choice<string?>> MailboxChoices { get; private set; } = [];
    public IReadOnlyList<Choice<string?>> FolderChoices { get; private set; } = [];
    public IReadOnlyList<Choice<string?>> CategoryChoices { get; private set; } = [];

    [ObservableProperty] public partial Choice<RuleMatch> Match { get; set; } = MatchChoices[0];
    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial Choice<string?>? Mailbox { get; set; }
    [ObservableProperty] public partial Choice<string?>? Folder { get; set; }
    [ObservableProperty] public partial Choice<string?>? Category { get; set; }
    [ObservableProperty] public partial bool MarkImportant { get; set; }
    [ObservableProperty] public partial bool MarkRead { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";

    public string Placeholder => Match.Value switch
    {
        RuleMatch.Domain => "e.g. fnb.co.za",
        RuleMatch.Sender => "e.g. accounts@",
        RuleMatch.Subject => "e.g. Proof of payment",
        _ => "e.g. accounts@webignite.co.za",
    };

    /// <summary>Folders belong to one mailbox, so moving is only offered once a mailbox is picked.</summary>
    public bool CanMove => Mailbox?.Value is not null;

    /// <summary>"Run the rules on Inbox now", for the folder open behind this page.</summary>
    public string RunLabel => owner.RulesPart.RulesTarget is { } target ? $"Run the rules on {target} now" : "";
    public bool CanRun => owner.RulesPart.RulesTarget is not null && Rules.Count > 0;

    partial void OnMatchChanged(Choice<RuleMatch> value) => OnPropertyChanged(nameof(Placeholder));

    partial void OnMailboxChanged(Choice<string?>? value)
    {
        FolderChoices = [new("Leave it where it is", null), .. FoldersOf(value?.Value)];
        Folder = FolderChoices[0];
        OnPropertyChanged(nameof(FolderChoices));
        OnPropertyChanged(nameof(CanMove));
    }

    private IEnumerable<Choice<string?>> FoldersOf(string? accountId) => accountId is null ? [] :
        Services.Store.ListMailFolders(accountId)
            .Where(f => f.Role is FolderRole.Other or FolderRole.Archive or FolderRole.Junk)
            .Select(f => new Choice<string?>(f.Name, f.Path));

    /// <summary>Reads the rules and the choices again, as mailboxes, folders and categories can change between visits.</summary>
    public void Load()
    {
        MailboxChoices = [new("Every mailbox", null), .. owner.Accounts.Select(a => new Choice<string?>(a.Address, a.Id))];
        CategoryChoices = [new("No category", null), .. owner.CategoryList.Select(c => new Choice<string?>(c.Name, c.Keyword))];
        OnPropertyChanged(nameof(MailboxChoices));
        OnPropertyChanged(nameof(CategoryChoices));
        Mailbox = MailboxChoices.Count == 2 ? MailboxChoices[1] : MailboxChoices[0];
        Category = CategoryChoices[0];
        Message = "";
        ShowRules();
    }

    private void ShowRules()
    {
        Rules.Clear();
        foreach (var rule in Services.Settings.MailRules.Rules)
            Rules.Add(new MailRuleRowViewModel(rule, Describe(rule), this));
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(RunLabel));
    }

    private string Describe(MailRule rule) => MailRules.Describe(rule,
        path => (rule.AccountId is null ? null : Services.Store.ListMailFolders(rule.AccountId).FirstOrDefault(f => f.Path == path)?.Name) ?? path,
        keyword => owner.CategoryList.FirstOrDefault(c => c.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))?.Name ?? keyword)
        + (rule.AccountId is { } id && owner.Accounts.Count > 1 ? $" ({owner.Accounts.FirstOrDefault(a => a.Id == id)?.Address})" : "");

    [RelayCommand]
    private void Add()
    {
        var rule = new MailRule
        {
            Id = MailRule.NewId(),
            AccountId = Mailbox?.Value,
            Match = Match.Value,
            Text = Text.Trim(),
            MoveTo = CanMove ? Folder?.Value : null,
            Category = Category?.Value,
            MarkImportant = MarkImportant,
            MarkRead = MarkRead,
        };
        Problems.Clear();
        foreach (var problem in MailRules.Check(rule))
            Problems.Add(problem);
        OnPropertyChanged(nameof(HasProblems));
        if (Problems.Count > 0)
            return;
        Save([.. Services.Settings.MailRules.Rules, rule]);
        Text = "";
        MarkImportant = MarkRead = false;
        Category = CategoryChoices[0];
        Folder = FolderChoices.FirstOrDefault();
        Message = "Added. It works on mail that arrives from now on.";
    }

    internal void Switch(MailRule rule, bool on) =>
        Save(Services.Settings.MailRules.Rules.Select(r => r.Id == rule.Id ? r with { On = on } : r).ToList());

    internal void Remove(MailRule rule)
    {
        var before = Services.Settings.MailRules.Rules;
        var at = before.ToList().FindIndex(r => r.Id == rule.Id);
        Save(before.Where(r => r.Id != rule.Id).ToList());
        Services.OfferUndo("Removed a mail rule", () =>
            Save(Webignite.Core.Layout.ListUndo.PutBack(Services.Settings.MailRules.Rules, rule, at, r => r.Id)));
    }

    private void Save(IReadOnlyList<MailRule> rules)
    {
        Services.Settings.MailRules = Services.Settings.MailRules with { Rules = rules };
        ShowRules();
    }

    [RelayCommand]
    private async Task RunNow()
    {
        if (owner.RulesPart.RulesTarget is not { } target)
            return;
        Message = $"Running the rules on {target}...";
        var changed = await owner.RulesPart.RunRulesNow();
        owner.Reload();
        Message = changed switch
        {
            0 => $"Nothing in {target} needed changing.",
            1 => "The rules changed 1 mail.",
            _ => $"The rules changed {changed} mails.",
        };
    }

    [RelayCommand]
    private void Close() => owner.RulesPart.CloseRules();
}

/// <summary>One rule in the list: its sentence, an on/off switch and Remove.</summary>
public partial class MailRuleRowViewModel(MailRule rule, string text, MailRulesViewModel owner) : ViewModelBase
{
    public string Text => text;

    public bool On
    {
        get => rule.On;
        set => owner.Switch(rule, value);
    }

    [RelayCommand]
    private void Remove() => owner.Remove(rule);
}

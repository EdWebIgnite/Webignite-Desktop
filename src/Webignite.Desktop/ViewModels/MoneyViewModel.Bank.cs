using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Data;

namespace Webignite.Desktop.ViewModels;

// Bringing in a bank statement: its new lines, each with what it most likely is, ticked when sure. Add records the
// ticked ones and learns from the kinds of expense picked; nothing is recorded before that.
public partial class MoneyViewModel
{
    public ObservableCollection<BankLineRowViewModel> BankLines { get; } = [];

    [ObservableProperty] public partial bool ShowingBank { get; set; }
    [ObservableProperty] public partial string BankMessage { get; set; } = "";
    [ObservableProperty] public partial bool BankMessageIsProblem { get; set; }

    private IReadOnlyList<BankLine> bankShown = [];

    /// <summary>Reads a statement the page picked (OFX or CSV from internet banking) and shows its new lines.</summary>
    public async Task StartBankAsync(string path)
    {
        BankLines.Clear();
        ShowingBank = true;
        try
        {
            var text = await File.ReadAllTextAsync(path);
            var statement = BankStatements.Read(Path.GetFileName(path), text);
            if (statement.Problem is { } problem)
            {
                SayBank(problem, true);
                return;
            }
            var prepared = services.Bank.Prepare(statement, services.Settings.Bank);
            bankShown = [.. prepared.Select(p => p.Line)];
            foreach (var (line, match) in prepared)
                BankLines.Add(new BankLineRowViewModel(line, match));
            SayBank(prepared.Count == 0 ? "Nothing new: every line in this statement was brought in before."
                : $"{prepared.Count} new {(prepared.Count == 1 ? "line" : "lines")}. The ticked ones are added when you click Add. Check the kind of each expense.", false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SayBank("Couldn't read the file: " + e.Message, true);
        }
    }

    [RelayCommand]
    private void AddBankLines()
    {
        var now = DateTimeOffset.Now;
        var settings = services.Settings.Bank;
        var added = 0;
        foreach (var row in BankLines.Where(r => r.Include))
        {
            var category = row.IsExpense ? row.Category : null;
            var party = row.IsNew ? row.Party : null;
            services.Bank.Apply(row.Line, row.Match, category, party, now);
            if (row.Match.Kind != BankMatchKind.AlreadyEntered)
                added++;
            if (row.IsExpense && category is not null)
                settings = settings with { Rules = BankMatching.Learn(settings.Rules, row.Line, category, party) };
        }
        // Lines left unticked are taken as not wanted, so the next statement doesn't offer them again.
        services.Settings.Bank = settings.WithSeen(bankShown.Select(l => l.Key));
        AppLog.Write($"Bank statement: {added} lines recorded");
        ShowingBank = false;
        BankLines.Clear();
        Refresh();
        services.ShowNote?.Invoke("Bank statement in", added == 1 ? "1 line recorded." : $"{added} lines recorded.", null);
    }

    [RelayCommand]
    private void CancelBank()
    {
        ShowingBank = false;
        BankLines.Clear();
    }

    private void SayBank(string text, bool problem)
    {
        BankMessage = text;
        BankMessageIsProblem = problem;
    }
}

/// <summary>One new statement line: what it most likely is, ticked when that's sure.</summary>
public sealed partial class BankLineRowViewModel : ObservableObject
{
    public BankLineRowViewModel(BankLine line, BankMatch match)
    {
        Line = line;
        Match = match;
        Include = match.Sure || match.Kind is BankMatchKind.NewIncome or BankMatchKind.NewExpense;
        Category = match.Category ?? (line.IsIn ? null : "Other");
        Party = match.Party ?? "";
    }

    public BankLine Line { get; }
    public BankMatch Match { get; }

    public string Date => Line.Date.ToString("d MMM");
    public string Description => Line.Description.Length > 0 ? Line.Description : "(no description)";
    public string Amount => (Line.IsIn ? "" : "-") + Money.Rand(Math.Abs(Line.Amount));
    public string What => Match.Kind switch
    {
        BankMatchKind.Invoice => (Match.Sure ? "" : "Maybe ") + Match.Label,
        BankMatchKind.Bill => (Match.Sure ? "" : "Maybe ") + Match.Label,
        BankMatchKind.AlreadyEntered => Match.Label,
        BankMatchKind.NewIncome => "New money in",
        _ => "New expense",
    };

    public bool IsExpense => Match.Kind == BankMatchKind.NewExpense;
    public bool IsNew => Match.Kind is BankMatchKind.NewIncome or BankMatchKind.NewExpense;
    public IReadOnlyList<string> Categories => ExpenseCategories.All;

    [ObservableProperty] public partial bool Include { get; set; }
    [ObservableProperty] public partial string? Category { get; set; }
    [ObservableProperty] public partial string Party { get; set; }
}

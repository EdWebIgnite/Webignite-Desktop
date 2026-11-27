using System.Text.RegularExpressions;
using Webignite.Core.Documents;

namespace Webignite.Core.Finance;

public enum BankMatchKind
{
    /// <summary>A client paying an invoice: its number is in the line, or it's the only one owing that amount.</summary>
    Invoice,

    /// <summary>A supplier's bill kept in Webignite being paid.</summary>
    Bill,

    /// <summary>Already on the money page (typed in, or a monthly expense): nothing to add.</summary>
    AlreadyEntered,

    /// <summary>Money in that isn't an invoice payment: added as money received.</summary>
    NewIncome,

    /// <summary>Money out: added as an expense, its kind guessed from what you said about the same words before.</summary>
    NewExpense,
}

/// <summary>What a statement line most likely is. <see cref="Sure"/> is false when only the amount matched.</summary>
public sealed record BankMatch(BankMatchKind Kind, string? Id, string Label, bool Sure, string? Category = null, string? Party = null);

/// <summary>
/// Something learnt from a correction: lines whose words (digits left out, as references change) are these get
/// this kind of expense and this name. Kept in the setting <c>bank-rules</c>.
/// </summary>
public sealed record BankRule(string Words, string Category, string? Party);

/// <summary>
/// Works out what each statement line is: an invoice being paid, a bill, something already on the money page, or new
/// money in or out. Only a suggestion: nothing is recorded until the person says yes.
/// </summary>
public static partial class BankMatching
{
    /// <summary>How far apart a statement line and an entry typed in by hand can be and still be the same money.</summary>
    public const int DaysApart = 4;

    public static BankMatch Suggest(BankLine line, IReadOnlyList<StoredDocument> documents, IReadOnlyList<SupplierBill> bills,
        IReadOnlyList<MoneyEntry> entries, IReadOnlyList<BankRule> rules)
    {
        var amount = Math.Abs(line.Amount);
        if (line.IsIn)
        {
            var open = documents.Where(d => Receivables.Outstanding(d) > 0).ToList();
            var squeezed = Squeeze(line.Description);
            if (open.FirstOrDefault(d => OwnInvoicing.NumberOf(d) is { Length: > 0 } n && squeezed.Contains(Squeeze(n), StringComparison.Ordinal)) is { } named)
                return new BankMatch(BankMatchKind.Invoice, named.Id, Label(named), Sure: true);
            if (Entered(line, entries, EntryKind.Received) is { } same)
                return new BankMatch(BankMatchKind.AlreadyEntered, same.Id, $"Already in: {same.Party ?? "money in"}", Sure: true);
            var owingThat = open.Where(d => Receivables.Outstanding(d) == amount).ToList();
            if (owingThat.Count == 1)
                return new BankMatch(BankMatchKind.Invoice, owingThat[0].Id, Label(owingThat[0]), Sure: false);
            return new BankMatch(BankMatchKind.NewIncome, null, "Money in", Sure: false, Party: Learnt(line, rules)?.Party);
        }

        var owed = bills.Where(b => SupplierBills.IsOwed(b) && b.Total == amount).ToList();
        var byName = owed.FirstOrDefault(b => Words(b.Supplier).Any(w => w.Length >= 4 && line.Description.Contains(w, StringComparison.OrdinalIgnoreCase)));
        if (byName is not null || owed.Count == 1)
        {
            var bill = byName ?? owed[0];
            return new BankMatch(BankMatchKind.Bill, bill.Id, $"Bill from {bill.Supplier}" + (bill.Number is { } n ? $", {n}" : ""), Sure: byName is not null);
        }
        if (Entered(line, entries, EntryKind.Expense) is { } spent)
            return new BankMatch(BankMatchKind.AlreadyEntered, spent.Id, $"Already in: {spent.Party ?? spent.Category ?? "expense"}", Sure: true);
        var rule = Learnt(line, rules);
        return new BankMatch(BankMatchKind.NewExpense, null, rule?.Party ?? "Expense", Sure: rule is not null,
            rule?.Category ?? Guess(line.Description), rule?.Party);
    }

    /// <summary>
    /// The rules with one more: next time a line with the same words comes, it gets this kind and name. A rule for
    /// the same words is replaced.
    /// </summary>
    public static IReadOnlyList<BankRule> Learn(IReadOnlyList<BankRule> rules, BankLine line, string category, string? party)
    {
        var words = Key(line.Description);
        if (words.Length == 0)
            return rules;
        return [.. rules.Where(r => r.Words != words), new BankRule(words, category, string.IsNullOrWhiteSpace(party) ? null : party.Trim())];
    }

    private static BankRule? Learnt(BankLine line, IReadOnlyList<BankRule> rules) =>
        Key(line.Description) is { Length: > 0 } words ? rules.LastOrDefault(r => r.Words == words) : null;

    // The same money already on the money page: same amount, the right way, within a few days.
    private static MoneyEntry? Entered(BankLine line, IReadOnlyList<MoneyEntry> entries, EntryKind kind) =>
        entries.Where(e => e.Kind == kind && e.Amount == Math.Abs(line.Amount) && Math.Abs(e.Date.DayNumber - line.Date.DayNumber) <= DaysApart)
            .OrderBy(e => Math.Abs(e.Date.DayNumber - line.Date.DayNumber)).FirstOrDefault();

    private static string Label(StoredDocument d) => $"{OwnInvoicing.NumberOf(d) ?? "Invoice"}, {d.Document.Customer.Name}";

    // Bank fees say so; anything else is left for the person (or a rule) to say.
    private static string? Guess(string description) =>
        FeeWords().IsMatch(description) ? "Bank fees" : null;

    /// <summary>A line's words for a rule: lower case, without digits and punctuation, so changing references match.</summary>
    public static string Key(string description) =>
        string.Join(' ', Words(new string([.. description.Select(c => char.IsLetter(c) ? char.ToLowerInvariant(c) : ' ')])).Where(w => w.Length > 1));

    private static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string Squeeze(string text) => new([.. text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    [GeneratedRegex(@"\b(fee|fees|charge|charges|admin|service fee|monthly acc)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FeeWords();
}

/// <summary>
/// What the bank statement import remembers: the lines already brought in (by their keys, so the next statement only
/// shows new ones) and what was learnt from corrections. Setting <c>bank</c>, on this PC.
/// </summary>
public sealed record BankSettings
{
    public IReadOnlyList<string> Seen { get; init; } = [];
    public IReadOnlyList<BankRule> Rules { get; init; } = [];

    /// <summary>Seen keys are kept for the last few thousand lines; older statements won't come in again anyway.</summary>
    public const int MostSeen = 5000;

    public BankSettings WithSeen(IEnumerable<string> keys) =>
        this with { Seen = [.. Seen.Concat(keys).Distinct(StringComparer.Ordinal).TakeLast(MostSeen)] };
}

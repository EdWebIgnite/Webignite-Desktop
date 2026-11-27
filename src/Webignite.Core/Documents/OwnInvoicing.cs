using System.Globalization;
using System.Text.RegularExpressions;

namespace Webignite.Core.Documents;

/// <summary>A quote or invoice made final in Webignite itself, not in Xero: its number, when, and what's been paid.</summary>
public sealed record OwnIssue
{
    public required string Number { get; init; }
    public DateTimeOffset Issued { get; init; }
    public IReadOnlyList<OwnPayment> Payments { get; init; } = [];

    /// <summary>Credit notes against it, made here: each takes its amount off what's owed.</summary>
    public IReadOnlyList<OwnCredit> Credits { get; init; } = [];

    /// <summary>When it was voided here.</summary>
    public DateTimeOffset? Voided { get; init; }
}

/// <summary>A credit note made in Webignite against an invoice made here: its own number, the day, how much and why.</summary>
public sealed record OwnCredit(string Number, DateOnly Date, decimal Amount, string? Reason = null, DateTimeOffset Recorded = default);

/// <summary>Money received against an invoice made in Webignite: on a day, how much, and a note (EFT, PayFast).</summary>
public sealed record OwnPayment(DateOnly Date, decimal Amount, string? Note = null, DateTimeOffset Recorded = default);

/// <summary>
/// Quotes and invoices made in Webignite instead of Xero, once Eddie switches that on: numbered carrying on from the
/// numbers he already has (INV-0107 is followed by INV-0108), made final when he approves or emails one, and paid off
/// by payments recorded here. Xero isn't written to for these.
/// </summary>
public static partial class OwnInvoicing
{
    [GeneratedRegex(@"^(?<prefix>.*?)(?<digits>\d+)$")]
    private static partial Regex Numbered();

    /// <summary>The number a quote or invoice shows: Xero's, else the one Webignite gave it.</summary>
    public static string? NumberOf(StoredDocument s) => s.Xero.Number ?? s.Own?.Number;

    /// <summary>
    /// The next number for a kind: the highest number used so far, in Xero or here, plus one, keeping its prefix and
    /// its leading zeros. INV-0001 or QU-0001 when there are none yet.
    /// </summary>
    public static string NextNumber(DocumentKind kind, IEnumerable<StoredDocument> all)
    {
        var fallback = kind == DocumentKind.Quote ? "QU-" : "INV-";
        var used = all.Where(s => s.Document.Kind == kind)
            .Select(NumberOf).OfType<string>()
            .Select(n => Numbered().Match(n.Trim()))
            .Where(m => m.Success)
            .Select(m => (Prefix: m.Groups["prefix"].Value, Digits: m.Groups["digits"].Value, Value: long.Parse(m.Groups["digits"].Value, CultureInfo.InvariantCulture)))
            .ToList();
        if (used.Count == 0)
            return fallback + "0001";
        // The prefix used most, so one odd number (a test, a typo) doesn't change them all.
        var prefix = used.GroupBy(u => u.Prefix).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(u => u.Value)).First().Key;
        var mine = used.Where(u => u.Prefix == prefix).ToList();
        var top = mine.MaxBy(u => u.Value);
        var width = Math.Max(top.Digits.Length, mine.Max(u => u.Digits.Length));
        return prefix + (top.Value + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    /// <summary>Whether a draft (written here or by Claude or ChatGPT) can be made final here.</summary>
    public static bool CanIssue(StoredDocument s) =>
        s.Status is DocumentStatus.Draft or DocumentStatus.AwaitingApproval && s.Xero.Id is null && s.Own is null;

    /// <summary>
    /// Makes a draft final here: it gets the next number (or the one typed on it, if no other document has that), and
    /// can no longer be changed. Returns null when it can't be issued.
    /// </summary>
    public static StoredDocument? Issue(StoredDocument s, IReadOnlyCollection<StoredDocument> all, DateTimeOffset now)
    {
        if (!CanIssue(s))
            return null;
        var typed = s.Document.Number?.Trim();
        var taken = all.Where(o => o.Id != s.Id && o.Document.Kind == s.Document.Kind).Select(NumberOf).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = !string.IsNullOrEmpty(typed) && !taken.Contains(typed) ? typed : NextNumber(s.Document.Kind, all.Where(o => o.Id != s.Id));
        return s with
        {
            Status = DocumentStatus.Issued,
            Document = s.Document with { Number = number },
            Own = new OwnIssue { Number = number, Issued = now },
            OffApprovals = null,
        };
    }

    public static decimal Paid(StoredDocument s) => s.Own?.Payments.Sum(p => p.Amount) ?? 0;

    public static decimal Credited(StoredDocument s) => s.Own?.Credits.Sum(c => c.Amount) ?? 0;

    /// <summary>What's still owed on an invoice made here: the total less what's been paid and credited.</summary>
    public static decimal Due(StoredDocument s) => Math.Max(0, Money.Cents((s.Document.Total ?? 0) - Paid(s) - Credited(s)));

    /// <summary>An invoice made here, not settled, with something still owed: a payment can be recorded.</summary>
    public static bool CanRecord(StoredDocument s) =>
        s.Status == DocumentStatus.Issued && s.Document.Kind == DocumentKind.Invoice && s.Settled is null && Due(s) > 0;

    /// <summary>Why a payment can't be recorded as typed, or null when it can.</summary>
    public static string? Check(StoredDocument s, decimal? amount, DateOnly? date, DateOnly today) =>
        amount is null ? "Type the amount paid, like 500 or 1 250.50."
        : amount <= 0 ? "Type an amount above R 0."
        : amount > Due(s) ? $"That's more than the {Money.Rand(Due(s))} still owed."
        : date is null ? "Pick the day it was paid."
        : date > today ? "The day it was paid can't be in the future."
        : null;

    /// <summary>
    /// Records a payment. Once nothing is owed the invoice is settled as paid, on the day of the last payment.
    /// Returns null when it can't be recorded.
    /// </summary>
    public static StoredDocument? Record(StoredDocument s, decimal amount, DateOnly date, string? note, DateTimeOffset now)
    {
        if (!CanRecord(s) || Check(s, amount, date, DateOnly.FromDateTime(now.LocalDateTime)) is not null)
            return null;
        var payments = s.Own!.Payments.Append(new OwnPayment(date, Money.Cents(amount), string.IsNullOrWhiteSpace(note) ? null : note.Trim(), now)).ToList();
        var recorded = s with { Own = s.Own with { Payments = payments } };
        return Due(recorded) > 0 ? recorded : recorded with
        {
            Settled = new SettledState { On = now, As = Settlement.Paid },
            Xero = recorded.Xero with { PaidOn = payments.Max(p => p.Date) },
        };
    }

    /// <summary>The word on the row for one made here: Sent, Part paid, Part credited, or how it was settled.</summary>
    public static string Status(StoredDocument s) =>
        s.Settled?.As is { } settled ? Settlements.Label(settled)
        : s.Document.Kind == DocumentKind.Invoice && Paid(s) > 0 ? "Part paid"
        : s.Document.Kind == DocumentKind.Invoice && Credited(s) > 0 ? "Part credited"
        : "Sent";

    // Void and credit notes

    /// <summary>An invoice made here with nothing paid or credited yet can be voided: it stops being owed.</summary>
    public static bool CanVoid(StoredDocument s) =>
        s.Status == DocumentStatus.Issued && s.Document.Kind == DocumentKind.Invoice && s.Settled is null && Paid(s) == 0 && Credited(s) == 0;

    public static string VoidQuestion(StoredDocument s) =>
        $"Void {NumberOf(s)}? It stays in the list marked void, is no longer owed, and can't be undone. The client isn't told; let them know yourself if they had it.";

    public static StoredDocument? Void(StoredDocument s, DateTimeOffset now) => !CanVoid(s) ? null : s with
    {
        Own = s.Own! with { Voided = now },
        Settled = new SettledState { On = now, As = Settlement.Voided },
    };

    /// <summary>An invoice made here with something still owed can have a credit note made against it.</summary>
    public static bool CanCredit(StoredDocument s) =>
        s.Status == DocumentStatus.Issued && s.Document.Kind == DocumentKind.Invoice && s.Settled is null && Due(s) > 0;

    /// <summary>The next credit note number: CN-0001, then on from the highest made here or drafted in Xero.</summary>
    public static string NextCreditNumber(IEnumerable<StoredDocument> all)
    {
        var numbers = all.SelectMany(d => (d.Own?.Credits.Select(c => c.Number) ?? []).Concat(d.CreditNotes.Select(c => c.Number).OfType<string>()))
            .Select(n => Numbered().Match(n.Trim())).Where(m => m.Success)
            .Select(m => (Prefix: m.Groups["prefix"].Value, Digits: m.Groups["digits"].Value, Value: long.Parse(m.Groups["digits"].Value, CultureInfo.InvariantCulture)))
            .ToList();
        if (numbers.Count == 0)
            return "CN-0001";
        var top = numbers.MaxBy(n => n.Value);
        return top.Prefix + (top.Value + 1).ToString(CultureInfo.InvariantCulture).PadLeft(top.Digits.Length, '0');
    }

    /// <summary>Why a credit can't be made as typed, or null when it can. No amount means all that's still owed.</summary>
    public static string? CheckCredit(StoredDocument s, decimal? amount) =>
        amount is <= 0 ? "Type an amount above R 0, or leave it empty to credit all that's owed."
        : amount > Due(s) ? $"That's more than the {Money.Rand(Due(s))} still owed."
        : null;

    /// <summary>
    /// Makes a credit note against the invoice, for an amount or all that's owed. Credited in full with nothing paid,
    /// it's settled as credited; with payments too, as paid. Returns null when it can't be made.
    /// </summary>
    public static StoredDocument? Credit(StoredDocument s, decimal? amount, string? reason, DateOnly date, IEnumerable<StoredDocument> all, DateTimeOffset now)
    {
        if (!CanCredit(s) || CheckCredit(s, amount) is not null)
            return null;
        var credit = new OwnCredit(NextCreditNumber(all), date, Money.Cents(amount ?? Due(s)), string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), now);
        var credited = s with { Own = s.Own! with { Credits = [.. s.Own.Credits, credit] } };
        return Due(credited) > 0 ? credited : credited with
        {
            Settled = new SettledState { On = now, As = Paid(credited) > 0 ? Settlement.Paid : Settlement.Credited },
        };
    }

    // Quotes

    /// <summary>A quote made here with no answer yet: the client accepted or declined it.</summary>
    public static bool CanDecide(StoredDocument s) =>
        s.Status == DocumentStatus.Issued && s.Document.Kind == DocumentKind.Quote && s.Settled is null;
}

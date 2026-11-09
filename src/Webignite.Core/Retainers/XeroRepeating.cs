using System.Globalization;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Core.Retainers;

/// <summary>A repeating invoice set up in Xero, as far as turning it into a retainer needs. Read only.</summary>
public sealed record XeroRepeatingInvoice
{
    public required string Id { get; init; }
    public required string ContactName { get; init; }
    public string? Reference { get; init; }
    public IReadOnlyList<Line> Lines { get; init; } = [];
    public decimal Total { get; init; }

    /// <summary>Every <see cref="Period"/> <see cref="Unit"/>s: WEEKLY or MONTHLY.</summary>
    public int Period { get; init; } = 1;
    public string Unit { get; init; } = "MONTHLY";
    public DateOnly? NextDate { get; init; }
    public DateOnly? EndDate { get; init; }

    /// <summary>AUTHORISED (running), DRAFT or DELETED.</summary>
    public string Status { get; init; } = "";
}

/// <summary>
/// Bringing Xero's repeating invoices over as retainers, so Webignite drafts them each month instead. Each one is
/// made paused: Eddie stops the repeating invoice in Xero first, then resumes the retainer, so nobody is billed twice.
/// </summary>
public static class XeroRepeating
{
    /// <summary>Why it can't become a retainer, or null when it can.</summary>
    public static string? WhyNot(XeroRepeatingInvoice r, IEnumerable<Retainer> retainers) =>
        retainers.Any(x => x.FromXeroRepeating == r.Id) ? "Already a retainer."
        : r.Status != "AUTHORISED" ? "Not running in Xero."
        : r.Unit != "MONTHLY" || r.Period != 1 ? "Only monthly ones can become retainers. Set this one up by hand."
        : !r.Lines.Any(l => l.IsPriced) ? "It has no priced lines."
        : null;

    /// <summary>How often and when next, e.g. "Monthly on the 1st, next 1 Dec 2026".</summary>
    public static string Schedule(XeroRepeatingInvoice r)
    {
        var every = (r.Unit, r.Period) switch
        {
            ("MONTHLY", 1) => "Monthly",
            ("MONTHLY", var n) => $"Every {n} months",
            ("WEEKLY", 1) => "Weekly",
            ("WEEKLY", var n) => $"Every {n} weeks",
            _ => "Repeats",
        };
        return r.NextDate is { } next
            ? $"{every}, next {next.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}"
            : every;
    }

    /// <summary>The retainer it becomes: same client, lines and day, from the month Xero would bill next, paused.</summary>
    public static Retainer ToRetainer(XeroRepeatingInvoice r, DateOnly today, DateTimeOffset now)
    {
        var next = r.NextDate ?? today;
        var title = !string.IsNullOrWhiteSpace(r.Reference) ? r.Reference.Trim()
            : r.Lines.FirstOrDefault(l => l.IsPriced)?.Description.Split('\n')[0].Trim() is { Length: > 0 } d ? d
            : "Monthly invoice";
        return new Retainer
        {
            Id = Retainer.NewId(),
            ClientName = r.ContactName,
            Title = title.Length > 60 ? title[..60].TrimEnd() : title,
            Lines = r.Lines.Select(l => l with { SourceLineTotal = null }).ToList(),
            DayOfMonth = next.Day,
            Starts = new DateOnly(next.Year, next.Month, 1),
            Ends = r.EndDate,
            Paused = true,
            Created = now,
            FromXeroRepeating = r.Id,
        };
    }

    public static string Made(Retainer retainer) =>
        $"{retainer.Title} for {retainer.ClientName} is a retainer now, paused. Stop the repeating invoice in Xero, "
        + $"then Resume it here, so {retainer.ClientName} isn't billed twice. It's {Money.Rand(retainer.MonthlyAmount)} a month.";
}

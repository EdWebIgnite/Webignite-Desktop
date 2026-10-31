namespace Webignite.Core.Finance;

public enum EntryKind
{
    /// <summary>Money that came in, e.g. a client paying an invoice.</summary>
    Received,

    /// <summary>Money that went out.</summary>
    Expense,
}

/// <summary>One amount received or spent, typed in by hand or added by a monthly expense.</summary>
public sealed record MoneyEntry
{
    public required string Id { get; init; }
    public required EntryKind Kind { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>Always positive. Kind says which way it went.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Who paid (a client) or who was paid (a supplier).</summary>
    public string? Party { get; init; }

    public string? Category { get; init; }
    public string? Note { get; init; }

    /// <summary>Set when a monthly expense added this entry, e.g. "recurring:{id}:2026-10".</summary>
    public string? RecurringKey { get; init; }

    public DateTimeOffset Created { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>An expense that comes back every month, set up once.</summary>
public sealed record RecurringExpense
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Category { get; init; }
    public required decimal Amount { get; init; }

    /// <summary>Day of the month it goes off. 31 means the last day in shorter months.</summary>
    public int DayOfMonth { get; init; } = 1;

    /// <summary>First month it counts for. Only the year and month matter.</summary>
    public required DateOnly Starts { get; init; }

    /// <summary>Last month it counts for, or null while it carries on.</summary>
    public DateOnly? Ends { get; init; }

    public bool Paused { get; init; }
    public DateTimeOffset Created { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

public static class ExpenseCategories
{
    public static IReadOnlyList<string> All { get; } =
    [
        "Hosting and servers",
        "Domains",
        "Software and subscriptions",
        "Internet and phone",
        "Contractors",
        "Marketing",
        "Equipment",
        "Office",
        "Travel",
        "Bank fees",
        "Tax",
        "Other",
    ];
}

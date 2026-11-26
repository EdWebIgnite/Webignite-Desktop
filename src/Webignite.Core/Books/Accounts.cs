namespace Webignite.Core.Books;

/// <summary>The five kinds of account every set of books has. Assets and expenses grow with a debit; the rest with a credit.</summary>
public enum AccountType { Asset, Liability, Equity, Income, Expense }

/// <summary>
/// What an account is for, where the app needs to find one by its job rather than its code: the bank, money clients
/// owe, money owed to suppliers, and the owner's and earned equity. Most accounts are Other.
/// </summary>
public enum AccountRole { Other, Bank, Debtors, Creditors, OwnersCapital, Drawings, RetainedEarnings }

/// <summary>One account in the books. Eddie sees its name; the code is for the accountant and for sorting.</summary>
public sealed record Account(string Code, string Name, AccountType Type, AccountRole Role = AccountRole.Other)
{
    /// <summary>True when a debit makes it bigger (assets and expenses).</summary>
    public bool GrowsWithDebit => Type is AccountType.Asset or AccountType.Expense;
}

/// <summary>
/// The chart of accounts a new set of books starts with: plain names a small South African web business uses, and
/// no VAT accounts, as Webignite isn't VAT registered. The accountant can add and rename accounts; the ones with a
/// role are what the app posts to, so they stay.
/// </summary>
public static class ChartOfAccounts
{
    public static IReadOnlyList<Account> Default { get; } =
    [
        new("1000", "Business bank account", AccountType.Asset, AccountRole.Bank),
        new("1100", "Money clients owe (debtors)", AccountType.Asset, AccountRole.Debtors),
        new("1500", "Computer equipment", AccountType.Asset),

        new("2000", "Money owed to suppliers (creditors)", AccountType.Liability, AccountRole.Creditors),
        new("2100", "Credit card", AccountType.Liability),
        new("2200", "Loan from owner", AccountType.Liability),
        new("2300", "Income tax owed to SARS", AccountType.Liability),

        new("3000", "Owner's capital", AccountType.Equity, AccountRole.OwnersCapital),
        new("3100", "Drawings", AccountType.Equity, AccountRole.Drawings),
        new("3900", "Retained earnings", AccountType.Equity, AccountRole.RetainedEarnings),

        new("4000", "Website design and development", AccountType.Income),
        new("4100", "Hosting and domains", AccountType.Income),
        new("4200", "Retainers and maintenance", AccountType.Income),
        new("4900", "Other income", AccountType.Income),
        new("4950", "Interest received", AccountType.Income),

        new("5000", "Contractors", AccountType.Expense),
        new("6000", "Hosting and servers", AccountType.Expense),
        new("6010", "Domains", AccountType.Expense),
        new("6020", "Software and subscriptions", AccountType.Expense),
        new("6030", "Internet and phone", AccountType.Expense),
        new("6040", "Marketing", AccountType.Expense),
        new("6050", "Small equipment", AccountType.Expense),
        new("6060", "Office", AccountType.Expense),
        new("6070", "Travel", AccountType.Expense),
        new("6080", "Bank fees", AccountType.Expense),
        new("6090", "Accounting and legal", AccountType.Expense),
        new("6100", "Insurance", AccountType.Expense),
        new("6110", "Training", AccountType.Expense),
        new("6200", "Interest paid", AccountType.Expense),
        new("6900", "Other expenses", AccountType.Expense),
    ];

    /// <summary>The account with that role, or null when the chart has none (it always should).</summary>
    public static Account? WithRole(IEnumerable<Account> chart, AccountRole role) => chart.FirstOrDefault(a => a.Role == role);

    /// <summary>
    /// The expense account for one of the money page's expense categories (<c>ExpenseCategories</c>), so what Eddie
    /// typed in before the books existed lands in the right place. Tax paid is not an expense: it goes to what is owed to SARS.
    /// </summary>
    public static string ForExpenseCategory(string? category) => category switch
    {
        "Hosting and servers" => "6000",
        "Domains" => "6010",
        "Software and subscriptions" => "6020",
        "Internet and phone" => "6030",
        "Contractors" => "5000",
        "Marketing" => "6040",
        "Equipment" => "6050",
        "Office" => "6060",
        "Travel" => "6070",
        "Bank fees" => "6080",
        "Tax" => "2300",
        _ => "6900",
    };
}

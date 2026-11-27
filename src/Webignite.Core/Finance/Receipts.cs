using Webignite.Core.Projects;

namespace Webignite.Core.Finance;

/// <summary>
/// Receipts kept with expenses, so the books can show what each one was for. A receipt lives in the receipts
/// folder under its expense's id, so two receipts with the same name never clash.
/// </summary>
public static class Receipts
{
    /// <summary>Files a receipt can be: PDFs and photos.</summary>
    public static IReadOnlyList<string> Patterns { get; } = ["*.pdf", "*.jpg", "*.jpeg", "*.png", "*.heic", "*.webp"];

    /// <summary>Where an expense's receipt sits, inside the receipts folder.</summary>
    public static string RelativePath(string entryId, string fileName) => Path.Combine(entryId, Attachments.SafeName(fileName));

    /// <summary>
    /// An expense that should have a receipt and hasn't. Bank fees and tax show on the bank statement or SARS's
    /// papers, so they don't need one.
    /// </summary>
    public static bool IsMissing(MoneyEntry entry) =>
        entry.Kind == EntryKind.Expense && entry.Receipt is null && entry.Category is not ("Bank fees" or "Tax");

    /// <summary>"2 expenses have no receipt yet", or empty when none are missing.</summary>
    public static string MissingLine(IEnumerable<MoneyEntry> entries) => entries.Count(IsMissing) switch
    {
        0 => "",
        1 => "1 expense has no receipt yet.",
        var n => $"{n} expenses have no receipt yet.",
    };
}

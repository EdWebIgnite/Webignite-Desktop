using Webignite.Core.Finance;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Receipts kept with expenses, as copies in the receipts folder on this PC, so moving or deleting the original
/// doesn't lose them. A removed expense keeps its receipt, so Undo brings both back.
/// </summary>
public sealed class ReceiptFiles(string? folder = null)
{
    public string Folder { get; } = folder ?? Path.Combine(DataPaths.Root(), "receipts");

    /// <summary>The receipt's file, or null when the expense has none or it isn't on this PC.</summary>
    public string? PathOf(MoneyEntry entry) =>
        entry.Receipt is { } receipt && Path.Combine(Folder, Receipts.RelativePath(entry.Id, receipt.FileName)) is var path && File.Exists(path) ? path : null;

    /// <summary>Copies the file in as the expense's receipt, in place of any it had, and returns the expense to save.</summary>
    public MoneyEntry Attach(MoneyEntry entry, string source)
    {
        var name = Path.GetFileName(source);
        var target = Path.Combine(Folder, Receipts.RelativePath(entry.Id, name));
        var folder = Path.GetDirectoryName(target)!;
        if (Directory.Exists(folder))
            foreach (var old in Directory.GetFiles(folder).Where(f => !string.Equals(f, target, StringComparison.OrdinalIgnoreCase)))
                File.Delete(old);
        Directory.CreateDirectory(folder);
        File.Copy(source, target, overwrite: true);
        return entry with { Receipt = new EntryReceipt(name, new FileInfo(target).Length) };
    }
}

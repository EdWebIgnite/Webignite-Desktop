using Webignite.Core.Finance;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Receipts kept with expenses and supplier's bills, as copies in the receipts folder on this PC, so moving or
/// deleting the original doesn't lose them. A removed expense keeps its receipt, so Undo brings both back.
/// </summary>
public sealed class ReceiptFiles(string? folder = null)
{
    public string Folder { get; } = folder ?? Path.Combine(DataPaths.Root(), "receipts");

    /// <summary>The receipt's file, or null when the expense has none or it isn't on this PC. A bill's payment finds the bill's.</summary>
    public string? PathOf(MoneyEntry entry) => PathOf(entry.Id, entry.Receipt) ?? (entry.BillId is { } bill ? PathOf(bill, entry.Receipt) : null);

    /// <summary>The bill's own file (the supplier's PDF), or null.</summary>
    public string? PathOf(SupplierBill bill) => PathOf(bill.Id, bill.Receipt);

    private string? PathOf(string id, EntryReceipt? receipt) =>
        receipt is not null && Path.Combine(Folder, Receipts.RelativePath(id, receipt.FileName)) is var path && File.Exists(path) ? path : null;

    /// <summary>Copies the file in as the expense's receipt, in place of any it had, and returns the expense to save.</summary>
    public MoneyEntry Attach(MoneyEntry entry, string source) => entry with { Receipt = Keep(entry.Id, Path.GetFileName(source), File.ReadAllBytes(source)) };

    /// <summary>Keeps a file under an id (an expense's or a bill's), in place of any kept there before.</summary>
    public EntryReceipt Keep(string id, string fileName, byte[] data)
    {
        var target = Path.Combine(Folder, Receipts.RelativePath(id, fileName));
        var folder = Path.GetDirectoryName(target)!;
        if (Directory.Exists(folder))
            foreach (var old in Directory.GetFiles(folder).Where(f => !string.Equals(f, target, StringComparison.OrdinalIgnoreCase)))
                File.Delete(old);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(target, data);
        return new EntryReceipt(Path.GetFileName(fileName), data.LongLength);
    }
}

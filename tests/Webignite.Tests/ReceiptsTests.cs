using Webignite.Core.Finance;

namespace Webignite.Tests;

public sealed class ReceiptsTests
{
    private static MoneyEntry Out(string category, EntryReceipt? receipt = null) => new()
    {
        Id = MoneyEntry.NewId(), Kind = EntryKind.Expense, Date = new DateOnly(2026, 11, 3), Amount = 349.99m, Category = category, Receipt = receipt,
    };

    [Fact]
    public void Expenses_without_a_receipt_are_counted_except_bank_fees_and_tax()
    {
        var entries = new[]
        {
            Out("Software and subscriptions"),
            Out("Hosting and servers", new EntryReceipt("xneelo.pdf", 1200)),
            Out("Bank fees"),
            Out("Tax"),
            Out("Travel"),
            new MoneyEntry { Id = "in", Kind = EntryKind.Received, Date = new DateOnly(2026, 11, 3), Amount = 100m },
        };

        Assert.Equal("2 expenses have no receipt yet.", Receipts.MissingLine(entries));
        Assert.Equal("1 expense has no receipt yet.", Receipts.MissingLine(entries.Take(2)));
        Assert.Equal("", Receipts.MissingLine(entries.Skip(1).Take(3)));
    }

    [Fact]
    public void A_receipt_is_kept_under_its_expense_with_a_safe_name()
    {
        var path = Receipts.RelativePath("abc", "Slip: 3 Nov.pdf");
        Assert.StartsWith("abc", path, StringComparison.Ordinal);
        Assert.DoesNotContain(':', Path.GetFileName(path));
    }
}

public sealed class ReceiptFilesTests
{
    [Fact]
    public void Attaching_a_receipt_keeps_a_copy_and_replaces_any_earlier_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "webignite-receipts-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var first = Path.Combine(root, "slip.jpg");
            var second = Path.Combine(root, "invoice.pdf");
            File.WriteAllText(first, "slip");
            File.WriteAllText(second, "invoice!");
            var files = new Webignite.Services.ReceiptFiles(Path.Combine(root, "receipts"));
            var entry = new MoneyEntry { Id = "e1", Kind = EntryKind.Expense, Date = new DateOnly(2026, 11, 3), Amount = 50m };

            Assert.Null(files.PathOf(entry));
            entry = files.Attach(entry, first);
            File.Delete(first);
            Assert.Equal(new EntryReceipt("slip.jpg", 4), entry.Receipt);
            Assert.Equal("slip", File.ReadAllText(files.PathOf(entry)!));

            entry = files.Attach(entry, second);
            Assert.Equal("invoice.pdf", entry.Receipt!.FileName);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(files.PathOf(entry)!)!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

namespace Webignite.Tests;

internal static class Samples
{
    public static readonly DateOnly Today = new(2026, 10, 31);
    public static readonly DocumentSettings Settings = new();

    public static Document NewInvoice(params Line[] lines) => new()
    {
        Kind = DocumentKind.Invoice,
        Origin = DocumentOrigin.New,
        Customer = new Customer { Name = "Karoo Coffee Roasters" },
        Lines = lines.Length > 0 ? lines : [Priced("Website hosting, November", 1, 450m)],
    };

    public static Line Priced(string description, decimal quantity, decimal price) =>
        new() { Description = description, Quantity = quantity, UnitPrice = price };

    public static Document Completed(Document doc) => DocumentDefaults.Complete(doc, Settings, Today);

    /// <summary>A document kept on this PC: the sample invoice unless another is given.</summary>
    public static StoredDocument Stored(Document? doc = null) =>
        new() { Id = StoredDocument.NewId(), Document = doc ?? Completed(NewInvoice()) };

    /// <summary>
    /// A quote or invoice that is in Xero: one line for <paramref name="total"/>, due (or expiring) on
    /// <paramref name="due"/>, numbered INV-id or QU-id. Tests change the rest with <c>with</c>.
    /// </summary>
    public static StoredDocument InXero(string id, decimal total = 450m, DateOnly? due = null, DocumentKind kind = DocumentKind.Invoice) => new()
    {
        Id = id,
        Document = Completed(NewInvoice(Priced(kind == DocumentKind.Quote ? "Site" : "Work", 1, total))) with { Kind = kind, DueOrExpiryDate = due ?? Today.AddDays(10) },
        Status = DocumentStatus.InXero,
        XeroId = "x-" + id,
        XeroNumber = (kind == DocumentKind.Quote ? "QU-" : "INV-") + id,
    };
}

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
}

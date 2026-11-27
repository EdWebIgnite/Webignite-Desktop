using Webignite.Core.Finance;

namespace Webignite.Data;

/// <summary>Supplier's bills kept in Webignite (when quotes and invoices are made here instead of Xero).</summary>
public sealed partial class LocalStore
{
    private const string BillContext = "bill:";

    public SupplierBill SaveSupplierBill(SupplierBill bill) => Put("supplier_bills", BillContext, bill.Id, bill);

    public SupplierBill? GetSupplierBill(string id) => Find<SupplierBill>("supplier_bills", BillContext, id);

    public void DeleteSupplierBill(string id) => Remove("supplier_bills", id);

    public IReadOnlyList<SupplierBill> ListSupplierBills() => ReadAll<SupplierBill>(BillContext, "SELECT id, payload FROM supplier_bills");

    /// <summary>The bill paid and its expense on the money page, saved together so one is never kept without the other.</summary>
    public SupplierBill SavePaidBill(SupplierBill bill, MoneyEntry expense) => InOneGo(() =>
    {
        SaveEntry(expense);
        return SaveSupplierBill(bill);
    });
}

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// One editable line: its item (the short name in Xero's Item column), description, quantity and price. Numbers are
/// typed as text and read leniently, e.g. "1 500,50" or "R1500.50". A sum such as "3.5 x 650" works too, and is
/// replaced by its answer when the box is left. What the editor doesn't show (a discount an assistant set, the
/// Xero item it's linked to) is kept from the line it was made from.
/// </summary>
public partial class LineRowViewModel : ViewModelBase
{
    private readonly Action changed;

    public LineRowViewModel(Action changed) => this.changed = changed;

    private Line? original;

    /// <summary>The item, as Xero's Item column shows it, e.g. WordPress setup. Optional.</summary>
    [ObservableProperty]
    public partial string Item { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial string Quantity { get; set; } = "1";

    [ObservableProperty]
    public partial string Price { get; set; } = "";

    public string AmountText => ToLine() is { } line ? Money.Rand(line.Amount()) : "";

    partial void OnDescriptionChanged(string value) => changed();

    partial void OnItemChanged(string value) => changed();

    partial void OnQuantityChanged(string value) => Refresh();

    partial void OnPriceChanged(string value) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(AmountText));
        changed();
    }

    /// <summary>Puts the answer in place of a sum, once the box is left.</summary>
    public void Settle()
    {
        if (Calculator.LooksLikeSum(Quantity) && Calculator.Evaluate(Quantity) is { Ok: true } q)
            Quantity = Calculator.Plain(q.Value!.Value);
        if (Calculator.LooksLikeSum(Price) && Calculator.Evaluate(Price) is { Ok: true } p)
            Price = Calculator.Plain(Money.Cents(p.Value!.Value));
    }

    public static LineRowViewModel From(Line line, Action changed) => new(changed)
    {
        original = line,
        Item = line.Product ?? "",
        Description = line.Description,
        Quantity = line.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "",
        Price = line.UnitPrice?.ToString(CultureInfo.InvariantCulture) ?? "",
    };

    public bool IsBlank => string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(Price) && string.IsNullOrWhiteSpace(Item);

    public Line? ToLine()
    {
        if (IsBlank)
            return null;
        var quantity = Calculator.Amount(Quantity);
        var price = Calculator.Amount(Price);
        var priced = quantity is not null || price is not null;
        var item = string.IsNullOrWhiteSpace(Item) ? null : Item.Trim();
        return (original ?? new Line { Description = "" }) with
        {
            Product = item,
            // A new item name is looked up in Xero again.
            ItemCode = item == original?.Product ? original?.ItemCode : null,
            Description = Description.Trim(),
            Quantity = priced ? quantity : null,
            UnitPrice = priced ? price : null,
        };
    }
}

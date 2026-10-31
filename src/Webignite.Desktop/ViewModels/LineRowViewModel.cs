using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

/// <summary>One editable line. Numbers are typed as text and read leniently, e.g. "1 500,50" or "R1500.50".</summary>
public partial class LineRowViewModel : ViewModelBase
{
    private readonly Action changed;

    public LineRowViewModel(Action changed) => this.changed = changed;

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial string Quantity { get; set; } = "1";

    [ObservableProperty]
    public partial string Price { get; set; } = "";

    public string AmountText => ToLine() is { } line ? Money.Rand(line.Amount()) : "";

    partial void OnDescriptionChanged(string value) => changed();

    partial void OnQuantityChanged(string value) => Refresh();

    partial void OnPriceChanged(string value) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(AmountText));
        changed();
    }

    public static LineRowViewModel From(Line line, Action changed) => new(changed)
    {
        Description = line.Description,
        Quantity = line.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "",
        Price = line.UnitPrice?.ToString(CultureInfo.InvariantCulture) ?? "",
    };

    public bool IsBlank => string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(Price);

    public Line? ToLine()
    {
        if (IsBlank)
            return null;
        var quantity = ParseNumber(Quantity);
        var price = ParseNumber(Price);
        var priced = quantity is not null || price is not null;
        return new Line
        {
            Description = Description.Trim(),
            Quantity = priced ? quantity : null,
            UnitPrice = priced ? price : null,
        };
    }

    internal static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var cleaned = text.Trim().TrimStart('R', 'r').Replace(" ", "").Replace(" ", "");
        // Accept a comma as the decimal mark when there is no dot, as many South Africans type it.
        if (!cleaned.Contains('.') && cleaned.Count(c => c == ',') == 1)
            cleaned = cleaned.Replace(',', '.');
        else
            cleaned = cleaned.Replace(",", "");
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}

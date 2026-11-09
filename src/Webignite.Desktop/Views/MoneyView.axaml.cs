using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MoneyView : UserControl
{
    public MoneyView() => InitializeComponent();

    /// <summary>Picks a supplier's PDF in the app's own explorer, then reads it into the bill boxes.</summary>
    private void PickBillPdf(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MoneyViewModel money)
            return;
        Pick(money).Forget("Drafting a bill from a PDF", tell: true);
    }

    private async Task Pick(MoneyViewModel money)
    {
        var picked = await Picker.Files(this, "Pick the supplier's bill", ["*.pdf"], kindName: "PDF bills");
        if (picked.Count > 0)
            await money.StartBillAsync(picked[0]);
    }
}

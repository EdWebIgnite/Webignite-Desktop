using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A calendar item's box, coloured by kind and struck through when done (styles in Views/AgendaChips.axaml).
/// Its DataContext is an <see cref="AgendaItemViewModel"/>; each page puts its own text inside.
/// </summary>
public sealed class AgendaChip : Border
{
    protected override Type StyleKeyOverride => typeof(Border);

    public AgendaChip() => Classes.Add("chip");

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var item = DataContext as AgendaItemViewModel;
        Classes.Set("task", item?.IsTask == true);
        Classes.Set("quote", item?.IsQuote == true);
        Classes.Set("invoice", item?.IsInvoice == true);
        Classes.Set("done", item?.IsDone == true);
    }
}

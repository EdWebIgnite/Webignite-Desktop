using Webignite.Core.Calendar;

namespace Webignite.Desktop.ViewModels;

/// <summary>An agenda item as shown in the calendar, planner and Today.</summary>
public sealed class AgendaItemViewModel(AgendaItem item, Action<AgendaItemViewModel>? delete = null)
{
    public AgendaItem Item { get; } = item;
    public string Title => Item.Title;
    public string Time => Item.Time?.ToString("HH:mm") ?? "";
    public bool HasTime => Item.Time is not null;
    public bool IsEvent => Item.Kind == AgendaKind.Event;
    public bool IsTask => Item.Kind == AgendaKind.Task;
    public bool IsQuote => Item.Kind == AgendaKind.QuoteExpires;
    public bool IsInvoice => Item.Kind == AgendaKind.InvoiceDue;
    public bool IsDone => Item.Done;
    public bool CanDelete => IsEvent && delete is not null;

    public string KindLabel => Item.Kind switch
    {
        AgendaKind.Event => "Event",
        AgendaKind.Task => "To-do",
        AgendaKind.QuoteExpires => "Quote",
        _ => "Invoice",
    };

    public void Delete() => delete?.Invoke(this);
}

using Webignite.Core.Calendar;

namespace Webignite.Desktop.ViewModels;

/// <summary>An agenda item as shown in the calendar, planner and Today.</summary>
public sealed class AgendaItemViewModel(AgendaItem item, Action<AgendaItemViewModel>? delete = null, string? joinLink = null)
{
    public AgendaItem Item { get; } = item;
    public string Title => Item.Title;
    public string Time => Item.Time?.ToString("HH:mm") ?? "";
    public bool HasTime => Item.Time is not null;
    public bool IsEvent => Item.Kind == AgendaKind.Event;
    public bool IsTask => Item.Kind is AgendaKind.Task or AgendaKind.ProjectTask;
    public bool IsQuote => Item.Kind == AgendaKind.QuoteExpires;
    public bool IsInvoice => Item.Kind == AgendaKind.InvoiceDue;
    public bool IsDone => Item.Done;
    public bool CanDelete => IsEvent && delete is not null;

    public string KindLabel => Item.Kind switch
    {
        AgendaKind.Event => "Event",
        AgendaKind.Task => "To-do",
        AgendaKind.ProjectTask => "Project task",
        AgendaKind.QuoteExpires => "Quote",
        AgendaKind.Renewal => "Renewal",
        AgendaKind.Retainer => "Retainer",
        _ => "Invoice",
    };

    /// <summary>Where to join the event when it is an online meeting.</summary>
    public string? JoinLink => joinLink;
    public bool CanJoin => joinLink is not null;

    public void Join()
    {
        if (joinLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var url))
            AppServices.OpenInBrowser(url).Forget("Opening a meeting's link", tell: true);
    }

    public void Delete() => delete?.Invoke(this);
}

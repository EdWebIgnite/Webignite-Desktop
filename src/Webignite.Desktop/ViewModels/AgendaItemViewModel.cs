using Webignite.Core.Calendar;

namespace Webignite.Desktop.ViewModels;

/// <summary>An agenda item as shown in the calendar, planner and Today.</summary>
public sealed class AgendaItemViewModel(AgendaItem item, Action<AgendaItemViewModel>? delete = null, string? joinLink = null, Action<AgendaItemViewModel>? clear = null)
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

    /// <summary>On Today's list: a to-do or task can be ticked off there, and anything else dismissed for the day.</summary>
    public bool CanClear => clear is not null;
    public string ClearLabel => IsTask ? "Done" : "Dismiss";
    public string ClearTip => IsTask ? "Tick it off" : "Take it off today's list. It stays in the calendar.";

    /// <summary>The label on Today's list: what it is and, when it can be cleared there, the word that does it.</summary>
    public string KindAndClear => CanClear ? $"{KindLabel}  ·  {ClearLabel}" : KindLabel;

    public void Clear() => clear?.Invoke(this);

    /// <summary>For a button; made when first asked for, as most lists of these never show one.</summary>
    public System.Windows.Input.ICommand ClearCommand => field ??= new CommunityToolkit.Mvvm.Input.RelayCommand(Clear);
}

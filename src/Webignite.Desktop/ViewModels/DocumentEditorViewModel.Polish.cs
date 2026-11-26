using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

// The editor's smaller comforts: the customer picked from the clients, the date and due date chosen on a calendar,
// what was typed saved by itself a moment after typing stops, and the example item on the first line only.
public partial class DocumentEditorViewModel
{
    /// <summary>Client names to pick the customer from; any other name can still be typed.</summary>
    public IReadOnlyList<string> ClientNames => clientNames ??= [.. services.Clients.List().Select(c => c.Name).Order(StringComparer.CurrentCultureIgnoreCase)];
    private IReadOnlyList<string>? clientNames;

    /// <summary>Who it goes to, under the customer: the client's address, or that they aren't a client yet.</summary>
    public string CustomerHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CustomerName))
                return "";
            if (services.Clients.Find(CustomerName) is not { } client)
                return "Not one of your clients yet.";
            return ClientMail.First(client.Email) is { Length: > 0 } email ? $"Goes to {email}" : "No email saved for this client yet.";
        }
    }

    public bool CanSaveAsClient => !string.IsNullOrWhiteSpace(CustomerName) && services.Clients.Find(CustomerName) is null;

    /// <summary>Saves the customer typed as a new client, so their details are kept for next time.</summary>
    [RelayCommand]
    private void SaveAsClient()
    {
        if (!CanSaveAsClient)
            return;
        var client = services.Clients.Add(CustomerName);
        clientNames = null;
        OnPropertyChanged(nameof(ClientNames));
        CustomerChanged();
        services.ShowNote?.Invoke("Saved as a client", $"{client.Name} is a client now. Add their email on their page.", () => services.Go.Client(client));
    }

    private void CustomerChanged()
    {
        OnPropertyChanged(nameof(CustomerHint));
        OnPropertyChanged(nameof(CanSaveAsClient));
    }

    // The date and the due (or expiry) date, as picked. The due date follows the date until it's picked itself.
    [ObservableProperty]
    public partial DateTime? DateValue { get; set; }

    [ObservableProperty]
    public partial DateTime? DueValue { get; set; }

    private bool dueFollows = true;

    public bool CanPickDates => CanType && (stored?.Document.Origin ?? DocumentOrigin.New) == DocumentOrigin.New;

    partial void OnDateValueChanged(DateTime? value)
    {
        if (loading || value is not { } date)
            return;
        if (dueFollows)
        {
            loading = true;
            DueValue = date.AddDays(Kind == DocumentKind.Quote ? documentSettings.QuoteExpiryDays : documentSettings.InvoiceDueDays);
            loading = false;
        }
        Recalculate();
    }

    partial void OnDueValueChanged(DateTime? value)
    {
        if (loading)
            return;
        dueFollows = false;
        Recalculate();
    }

    private void ShowDates(Document doc)
    {
        var was = loading;
        loading = true;
        DateValue = doc.Date?.ToDateTime(TimeOnly.MinValue);
        DueValue = doc.DueOrExpiryDate?.ToDateTime(TimeOnly.MinValue);
        loading = was;
    }

    private static DateOnly? Day(DateTime? value) => value is { } d ? DateOnly.FromDateTime(d) : null;

    // Saved by itself a moment after typing stops, as Notes does. Back, another document or closing save at once.
    private DispatcherTimer? saveTimer;

    private void SaveSoon()
    {
        if (loading || !IsEditable || !HasContent)
            return;
        saveTimer ??= NewSaveTimer();
        saveTimer.Stop();
        saveTimer.Start();
    }

    private DispatcherTimer NewSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (changed && !deleted && HasContent)
                Save();
        };
        return timer;
    }

    private void StopSaveTimer() => saveTimer?.Stop();

    // Only the first line shows an example item.
    private void MarkFirstLine()
    {
        for (var i = 0; i < Lines.Count; i++)
            Lines[i].ItemHint = i == 0 ? "e.g. WordPress setup" : "";
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Time;

namespace Webignite.Desktop.ViewModels;

/// <summary>A draft invoice from a client's logged time for a month, at their hourly rates. It opens for checking; nothing goes to Xero.</summary>
public partial class TimeViewModel
{
    /// <summary>Clients with an hourly rate on them or on one of their projects.</summary>
    public IReadOnlyList<Choice<Client>> InvoiceClients { get; private set; } = [];
    public bool CanInvoice => InvoiceClients.Count > 0;

    /// <summary>This month and last month, built again after midnight by <see cref="CatchUpDays"/>.</summary>
    public IReadOnlyList<Choice<DateOnly>> InvoiceMonths { get; private set; } = [];

    [ObservableProperty] public partial Choice<Client>? InvoiceClient { get; set; }
    [ObservableProperty] public partial Choice<DateOnly>? InvoiceMonth { get; set; }

    private void LoadInvoiceChoices(IReadOnlyList<Client> clients)
    {
        var keep = InvoiceClient?.Value.Id;
        InvoiceClients = TimeRules.Invoiceable(clients).Select(c => new Choice<Client>(c.Name, c)).ToList();
        OnPropertyChanged(nameof(InvoiceClients));
        OnPropertyChanged(nameof(CanInvoice));
        InvoiceClient = InvoiceClients.FirstOrDefault(c => c.Value.Id == keep) ?? InvoiceClients.FirstOrDefault();
        InvoiceMonth ??= InvoiceMonths[0];
    }

    [RelayCommand]
    private void DraftInvoice()
    {
        CatchUpDays();
        if (InvoiceClient?.Value is not { } client || InvoiceMonth?.Value is not { } month)
            return;
        var entries = services.Store.ListTime(month, month.AddMonths(1).AddDays(-1));
        var (_, unpriced) = TimeInvoice.Lines(entries, client, month);
        if (TimeInvoice.Draft(entries, client, month, services.Settings.Documents, Today) is not { } draft)
        {
            Say(unpriced > 0
                ? $"{client.Name}'s time in {InvoiceMonth.Label.ToLowerInvariant()} has no hourly rate, so there is nothing to price. Set one on their page."
                : $"No time for {client.Name} in {InvoiceMonth.Label.ToLowerInvariant()}.", problem: true);
            return;
        }
        var stored = services.Store.Save(new StoredDocument { Id = StoredDocument.NewId(), Document = draft });
        Say(unpriced > 0
            ? $"Draft invoice made. {TimeRules.Format(unpriced)} with no rate was left out."
            : "Draft invoice made. It is waiting under Invoices for you to check.");
        services.Go.Document(stored);
    }
}

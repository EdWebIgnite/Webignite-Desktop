using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// The top of a client's page as the hub: their money at a glance.
public partial class ClientDetailViewModel
{
    [ObservableProperty] public partial bool ShowMoney { get; set; }
    [ObservableProperty] public partial string OwesText { get; set; } = "";
    [ObservableProperty] public partial string OverdueText { get; set; } = "";
    [ObservableProperty] public partial bool HasOverdue { get; set; }
    [ObservableProperty] public partial string PaidThisYearText { get; set; } = "";
    [ObservableProperty] public partial string InvoicedThisYearText { get; set; } = "";
    [ObservableProperty] public partial string PaysInText { get; set; } = "";
    [ObservableProperty] public partial string TaxYearText { get; set; } = "";

    private void LoadMoney(IReadOnlyList<StoredDocument> theirs)
    {
        var money = ClientMoney.For(theirs, Today);
        OwesText = Money.Rand(money.Owed);
        HasOverdue = money.Overdue > 0;
        OverdueText = HasOverdue ? $"{Money.Rand(money.Overdue)} of it overdue" : money.Owed > 0 ? "None of it overdue" : "Nothing owed";
        PaidThisYearText = Money.Rand(money.PaidThisYear);
        InvoicedThisYearText = Money.Rand(money.InvoicedThisYear);
        PaysInText = money.DaysToPay is null ? "Nothing paid yet to go by" : ClientMoney.PaysIn(money.DaysToPay);
        TaxYearText = $"Since {Dates.TaxYear(Today).From.ToString("d MMM yyyy")}";
        ShowMoney = theirs.Any(d => d.Document.Kind == DocumentKind.Invoice);
    }
}

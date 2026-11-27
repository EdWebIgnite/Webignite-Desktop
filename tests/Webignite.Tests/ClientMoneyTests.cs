using Webignite.Core.Clients;

namespace Webignite.Tests;

public sealed class ClientMoneyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void A_clients_money_at_a_glance_counts_Xero_and_Webignite_invoices_alike()
    {
        var today = Samples.Today; // 31 Oct 2026, tax year from 1 Mar 2026
        var overdue = Samples.InXero("a", 2800m, today.AddDays(-9)).WithXero(x => x with { Status = "AUTHORISED" });
        var paidXero = Samples.InXero("b", 7250m, today.AddDays(-20)).WithXero(x => x with { Status = "PAID", PaidOn = today.AddDays(-10), AmountPaid = 7250m })
            with { Settled = new SettledState { On = Now, As = Settlement.Paid } };
        paidXero = paidXero with { Document = paidXero.Document with { Date = today.AddDays(-24) } };
        var own = OwnInvoicing.Issue(Samples.Stored(Samples.Completed(Samples.NewInvoice(Samples.Priced("Hosting", 1, 1000m)))), [], Now)!;
        own = OwnInvoicing.Record(own, 400m, today, "EFT", Now)!;

        var money = ClientMoney.For([overdue, paidXero, own], today);
        Assert.Equal(2800m + 600m, money.Owed);
        Assert.Equal(2800m, money.Overdue);
        Assert.Equal(7250m + 400m, money.PaidThisYear);
        Assert.Equal(14, money.DaysToPay);
        Assert.Equal("Usually pays in 14 days", ClientMoney.PaysIn(money.DaysToPay));
        Assert.Equal("", ClientMoney.PaysIn(null));
    }
}

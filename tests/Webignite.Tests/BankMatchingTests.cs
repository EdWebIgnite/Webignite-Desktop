using Webignite.Core.Finance;

namespace Webignite.Tests;

public sealed class BankMatchingTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);

    private static BankLine Line(decimal amount, string description, int days = 0) => new(Day.AddDays(days), amount, description, "k" + amount + description);

    private static readonly IReadOnlyList<Webignite.Core.Documents.StoredDocument> Invoices =
    [
        Samples.InXero("0104", 2800m).WithXero(x => x with { Status = "AUTHORISED" }),
        Samples.InXero("0107", 450m).WithXero(x => x with { Status = "AUTHORISED" }),
        Samples.InXero("0110", 450m).WithXero(x => x with { Status = "AUTHORISED" }),
    ];

    [Fact]
    public void Money_in_naming_an_invoice_number_pays_that_invoice_even_written_without_its_dash()
    {
        var match = BankMatching.Suggest(Line(450m, "FNB APP PAYMENT FROM KAROO COFFEE INV0110"), Invoices, [], [], []);
        Assert.Equal((BankMatchKind.Invoice, "0110", true), (match.Kind, match.Id, match.Sure));
    }

    [Fact]
    public void Money_in_matching_only_one_invoices_amount_is_offered_but_not_sure()
    {
        var match = BankMatching.Suggest(Line(2800m, "MAGTAPE CREDIT LERATO"), Invoices, [], [], []);
        Assert.Equal((BankMatchKind.Invoice, "0104", false), (match.Kind, match.Id, match.Sure));

        // Two invoices owe 450: the amount alone doesn't say which.
        Assert.Equal(BankMatchKind.NewIncome, BankMatching.Suggest(Line(450m, "CREDIT"), Invoices, [], [], []).Kind);
    }

    [Fact]
    public void Money_already_typed_in_within_a_few_days_isnt_added_again()
    {
        var typed = new MoneyEntry { Id = "e1", Kind = EntryKind.Expense, Date = Day.AddDays(2), Amount = 349.99m, Party = "Takealot" };
        Assert.Equal((BankMatchKind.AlreadyEntered, "e1"), (BankMatching.Suggest(Line(-349.99m, "POS PURCHASE TAKEALOT"), [], [], [typed], []) is var m ? (m.Kind, m.Id) : default));
        Assert.Equal(BankMatchKind.NewExpense, BankMatching.Suggest(Line(-349.99m, "POS PURCHASE TAKEALOT", days: 10), [], [], [typed], []).Kind);
    }

    [Fact]
    public void Money_out_matching_a_bill_owed_pays_the_bill()
    {
        var bill = new SupplierBill { Id = "b1", Supplier = "xneelo (Pty) Ltd", Date = Day.AddDays(-9), Total = 1899m, Number = "INV-7790" };
        var match = BankMatching.Suggest(Line(-1899m, "DEBIT ORDER XNEELO 7790"), [], [bill], [], []);
        Assert.Equal((BankMatchKind.Bill, "b1", true), (match.Kind, match.Id, match.Sure));
    }

    [Fact]
    public void A_correction_is_learnt_and_used_for_the_same_words_with_another_reference()
    {
        var first = Line(-1150m, "DEBIT ORDER ADOBE SYSTEMS 88231");
        Assert.Equal((BankMatchKind.NewExpense, null, false), (BankMatching.Suggest(first, [], [], [], []) is var m ? (m.Kind, m.Category, m.Sure) : default));

        var rules = BankMatching.Learn([], first, "Software and subscriptions", "Adobe");
        var next = BankMatching.Suggest(Line(-1150m, "DEBIT ORDER ADOBE SYSTEMS 90412", days: 30), [], [], [], rules);

        Assert.Equal(("Software and subscriptions", "Adobe", true), (next.Category, next.Party, next.Sure));
        Assert.Single(BankMatching.Learn(rules, first, "Software and subscriptions", "Adobe Inc"));
    }

    [Fact]
    public void Bank_fees_are_guessed_from_their_words()
    {
        Assert.Equal("Bank fees", BankMatching.Suggest(Line(-125m, "#MONTHLY ACCOUNT FEE"), [], [], [], []).Category);
    }
}

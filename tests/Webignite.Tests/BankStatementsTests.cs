using Webignite.Core.Finance;

namespace Webignite.Tests;

public sealed class BankStatementsTests
{
    private const string FnbCsv = """
        2,Name,WEBIGNITE (PTY) LTD
        3,Account,62000000000,Gold Business Account
        4,Balance,15234.56,15234.56,0.00
        5,Number,Date,Description1,Description2,Description3,Amount,Balance,Accrued Charges
        5,1,03 Nov 2026,FNB App Payment From,Fynbos Physio,INV-0104,2800.00,18034.56,0.00
        5,2,05 Nov 2026,POS Purchase,"Takealot, Cape Town",,-349.99,17684.57,0.00
        5,3,30 Nov 2026,#Monthly Account Fee,,,-125.00,17559.57,0.00
        """;

    [Fact]
    public void An_FNB_csv_is_read_from_its_heading_row_with_the_description_columns_joined()
    {
        var statement = BankStatements.Read("62000000000.csv", FnbCsv);

        Assert.Null(statement.Problem);
        Assert.Equal(3, statement.Lines.Count);
        Assert.Equal((new DateOnly(2026, 11, 3), 2800m, "FNB App Payment From Fynbos Physio INV-0104"), (statement.Lines[0].Date, statement.Lines[0].Amount, statement.Lines[0].Description));
        Assert.Equal("POS Purchase Takealot, Cape Town", statement.Lines[1].Description);
        Assert.Equal(-125m, statement.Lines[2].Amount);
    }

    [Fact]
    public void A_csv_with_money_in_and_out_columns_is_read_too()
    {
        const string csv = "Date,Description,Debit,Credit,Balance\n2026/11/03,Payment Karoo Coffee,,450.00,900.00\n2026/11/04,xneelo hosting,1899.00,,-999.00\n";
        var lines = BankStatements.Read("statement.csv", csv).Lines;
        Assert.Equal([450m, -1899m], lines.Select(l => l.Amount));
    }

    [Fact]
    public void An_ofx_file_is_read_with_the_banks_own_ids()
    {
        const string ofx = """
            OFXHEADER:100
            <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>
            <STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20261103120000<TRNAMT>2800.00<FITID>A1<NAME>Fynbos Physio<MEMO>INV-0104
            <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261105<TRNAMT>-349.99<FITID>A2<NAME>Takealot
            </BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
            """;
        var lines = BankStatements.Read("statement.ofx", ofx).Lines;

        Assert.Equal(2, lines.Count);
        Assert.Equal(("ofx:A1", 2800m, "Fynbos Physio INV-0104"), (lines[0].Key, lines[0].Amount, lines[0].Description));
        Assert.Equal(new DateOnly(2026, 11, 5), lines[1].Date);
    }

    [Fact]
    public void A_statement_brought_in_twice_adds_nothing_the_second_time()
    {
        var first = BankStatements.Read("a.csv", FnbCsv).Lines;
        var again = BankStatements.Read("a.csv", FnbCsv).Lines;

        Assert.Empty(BankStatements.New(again, first.Select(l => l.Key).ToHashSet()));
        Assert.Equal(3, first.Select(l => l.Key).Distinct().Count());
    }

    [Fact]
    public void A_file_that_isnt_a_statement_says_so()
    {
        Assert.NotNull(BankStatements.Read("notes.csv", "Name,Phone\nEddie,082").Problem);
        Assert.NotNull(BankStatements.Read("empty.ofx", "<OFX></OFX>").Problem);
    }
}

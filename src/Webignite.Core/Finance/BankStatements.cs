using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Webignite.Core.Documents;

namespace Webignite.Core.Finance;

/// <summary>
/// One line on a bank statement: money in is more than zero, money out less. <see cref="Key"/> is the same each time
/// the same line is brought in (the bank's own id when the file has one), so a statement brought in twice, or two
/// that overlap, don't double anything.
/// </summary>
public sealed record BankLine(DateOnly Date, decimal Amount, string Description, string Key)
{
    public bool IsIn => Amount > 0;
}

/// <summary>What was read from a statement file, and what couldn't be.</summary>
public sealed record BankStatement(IReadOnlyList<BankLine> Lines, string? Problem);

/// <summary>
/// Reads a bank statement downloaded from internet banking: OFX (Money and Quicken format, which FNB and the other
/// banks offer) or CSV. A CSV is read by its heading row, wherever it is (FNB puts account details above it and a
/// number at the start of each row), finding the date, amount and description columns by name. Dates and amounts
/// are read the way banks write them, not by this PC's region.
/// </summary>
public static partial class BankStatements
{
    public static BankStatement Read(string fileName, string text) =>
        fileName.EndsWith(".ofx", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".qfx", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<STMTTRN>", StringComparison.OrdinalIgnoreCase)
            ? ReadOfx(text)
            : ReadCsv(text);

    public static BankStatement ReadOfx(string text)
    {
        var lines = new List<BankLine>();
        foreach (Match block in OfxTransaction().Matches(text))
        {
            var body = block.Groups["body"].Value;
            var posted = OfxField(body, "DTPOSTED");
            var amount = OfxField(body, "TRNAMT");
            if (posted is not { Length: >= 8 } || !DateOnly.TryParseExact(posted[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || Money.Parse(amount) is not { } value)
                continue;
            var description = string.Join(" ", new[] { OfxField(body, "NAME"), OfxField(body, "MEMO") }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
            var id = OfxField(body, "FITID");
            lines.Add(new BankLine(date, value, Tidy(description), id is { Length: > 0 } ? "ofx:" + id : KeyOf(date, value, description, null)));
        }
        return lines.Count == 0 ? new BankStatement([], "No transactions in this file. Download it again as OFX or CSV.") : new BankStatement(lines, null);
    }

    public static BankStatement ReadCsv(string text)
    {
        var rows = text.Replace("\r", "").Split('\n').Select(Cells).ToList();
        var headAt = rows.FindIndex(r => r.Any(c => Name(c) == "date") && r.Any(c => Name(c) is "amount" or "debit" or "credit"));
        if (headAt < 0)
            return new BankStatement([], "This doesn't look like a bank statement: no Date and Amount columns.");
        var head = rows[headAt].Select(Name).ToList();
        var dateAt = head.IndexOf("date");
        var amountAt = head.IndexOf("amount");
        var debitAt = head.IndexOf("debit");
        var creditAt = head.IndexOf("credit");
        var balanceAt = head.IndexOf("balance");
        var descriptionAt = head.Select((h, i) => (h, i)).Where(x => x.h.StartsWith("description", StringComparison.Ordinal) || x.h is "reference" or "details" or "narrative")
            .Select(x => x.i).ToList();
        var lines = new List<BankLine>();
        // Rows before the heading's own record number (FNB starts each row with one) are other details.
        var recordType = rows[headAt].Count > 0 && rows[headAt][0].Length > 0 && rows[headAt][0].All(char.IsDigit) ? rows[headAt][0] : null;
        foreach (var row in rows.Skip(headAt + 1))
        {
            if (recordType is not null && (row.Count == 0 || row[0] != recordType))
                continue;
            if (Cell(row, dateAt) is not { } dateText || ParseDate(dateText) is not { } date)
                continue;
            decimal? amount = amountAt >= 0 ? Money.Parse(Cell(row, amountAt))
                : (Money.Parse(Cell(row, creditAt)) ?? 0) - Math.Abs(Money.Parse(Cell(row, debitAt)) ?? 0);
            if (amount is not { } value || value == 0)
                continue;
            var description = Tidy(string.Join(" ", descriptionAt.Select(i => Cell(row, i)).Where(s => !string.IsNullOrWhiteSpace(s))));
            lines.Add(new BankLine(date, value, description, KeyOf(date, value, description, Cell(row, balanceAt))));
        }
        return lines.Count == 0 ? new BankStatement([], "No transactions under the heading row.") : new BankStatement(lines, null);
    }

    /// <summary>Only the lines not brought in before, by their keys.</summary>
    public static IReadOnlyList<BankLine> New(IEnumerable<BankLine> lines, IReadOnlySet<string> known) =>
        [.. lines.Where(l => !known.Contains(l.Key)).DistinctBy(l => l.Key)];

    private static readonly string[] DateFormats =
        ["dd MMM yyyy", "d MMM yyyy", "yyyy/MM/dd", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyyMMdd", "dd MMMM yyyy", "dd-MM-yyyy"];

    private static DateOnly? ParseDate(string text) =>
        DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string Name(string cell)
    {
        var name = cell.Trim().ToLowerInvariant();
        return name switch
        {
            "transaction date" or "posting date" or "date" => "date",
            "amount" or "amount (zar)" or "value" => "amount",
            "debit" or "debit amount" or "money out" => "debit",
            "credit" or "credit amount" or "money in" => "credit",
            "balance" or "running balance" => "balance",
            _ => name,
        };
    }

    private static string? Cell(List<string> row, int at) => at >= 0 && at < row.Count && row[at].Trim().Length > 0 ? row[at].Trim() : null;

    // A CSV row's cells, with quoted cells holding commas.
    private static List<string> Cells(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                    quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
                cell.Append(ch);
        }
        cells.Add(cell.ToString());
        return cells;
    }

    private static string? OfxField(string body, string name) =>
        Regex.Match(body, $@"<{name}>([^<\r\n]*)", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    private static string Tidy(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // Same line, same key: its day, amount, words and (when the bank gives one) the balance after it, which tells
    // two equal payments on one day apart.
    private static string KeyOf(DateOnly date, decimal amount, string description, string? balance)
    {
        var text = string.Join("|", date.ToString("yyyyMMdd", CultureInfo.InvariantCulture), amount.ToString("0.00", CultureInfo.InvariantCulture),
            Tidy(description).ToLowerInvariant(), balance ?? "");
        return "line:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..24].ToLowerInvariant();
    }

    [GeneratedRegex(@"<STMTTRN>(?<body>.*?)(</STMTTRN>|(?=<STMTTRN>)|(?=</BANKTRANLIST>))", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OfxTransaction();
}

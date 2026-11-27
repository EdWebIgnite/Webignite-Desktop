using System.Globalization;
using System.Text;
using Webignite.Core.Files;

namespace Webignite.Core.Documents;

/// <summary>
/// A spreadsheet Claude or ChatGPT prepared to bring in, waiting for Eddie to look at and bring in or throw away. Kept
/// on this PC as the file's text, so what comes in is worked out again, against what is here, at the moment he says so.
/// </summary>
public sealed record PendingImport(string Csv, DocumentKind? Kind, string From, DateTimeOffset At)
{
    public const string SettingName = "pending-document-import";

    /// <summary>The biggest file kept: about ten thousand rows.</summary>
    public const int Largest = 2 * 1024 * 1024;
}

/// <summary>What a spreadsheet of old quotes and invoices would bring in, what is left out and why.</summary>
public sealed record DocumentImportPlan(IReadOnlyList<StoredDocument> Documents, IReadOnlyList<string> Skipped, IReadOnlyList<string> Problems)
{
    public int Quotes => Documents.Count(d => d.Document.Kind == DocumentKind.Quote);
    public int Invoices => Documents.Count(d => d.Document.Kind == DocumentKind.Invoice);
}

/// <summary>
/// Old quotes and invoices from a CSV file (a Xero or QuickBooks export, or a list of Eddie's own) brought in as they
/// were: their numbers, dates, client, lines or total, and whether they were sent, paid, accepted or cancelled. Rows
/// with the same number are lines of one document. Nothing is mailed and nothing goes to Xero; a number already here
/// is left out, so a file can be brought in twice. The headings are matched loosely, so most exports work as they are.
/// </summary>
public static class DocumentImport
{
    private static readonly Dictionary<string, string[]> Headings = new()
    {
        ["kind"] = ["type", "kind", "documenttype", "doctype"],
        ["number"] = ["number", "invoicenumber", "quotenumber", "estimatenumber", "invoiceno", "quoteno", "documentnumber", "no", "num"],
        ["customer"] = ["contactname", "customer", "customername", "client", "clientname", "contact", "billto", "name", "company"],
        ["email"] = ["emailaddress", "email", "customeremail"],
        ["date"] = ["date", "invoicedate", "quotedate", "estimatedate", "issuedate", "issued", "created"],
        ["due"] = ["duedate", "due", "expirydate", "expiry", "expires", "validuntil", "expirationdate"],
        ["reference"] = ["reference", "ref", "ordernumber", "ponumber", "po"],
        ["description"] = ["description", "itemdescription", "details", "productservice", "lineitem", "item"],
        ["quantity"] = ["quantity", "qty", "hours"],
        ["price"] = ["unitamount", "unitprice", "price", "rate"],
        ["amount"] = ["lineamount", "amount", "linetotal"],
        ["total"] = ["total", "invoicetotal", "quotetotal", "totalamount", "grandtotal"],
        ["paid"] = ["amountpaid", "invoiceamountpaid", "paid", "paidamount"],
        ["owing"] = ["amountdue", "invoiceamountdue", "balance", "balancedue", "openbalance", "outstanding"],
        ["paidon"] = ["paiddate", "datepaid", "paymentdate", "fullypaidondate", "paidon"],
        ["status"] = ["status", "state"],
    };

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "d MMM yyyy", "dd MMM yyyy",
        "d MMMM yyyy", "MMM d, yyyy", "MMMM d, yyyy", "d-MMM-yyyy", "d-MMM-yy", "d/M/yy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss",
    ];

    /// <summary>
    /// What the file would bring in. <paramref name="kind"/> is what the rows are when the file doesn't say (a Type
    /// column, or a Quote number or Invoice number heading); null when the file must say.
    /// </summary>
    public static DocumentImportPlan Plan(string csv, DocumentKind? kind, IEnumerable<StoredDocument> existing, DateTimeOffset now, string currency = "ZAR")
    {
        var rows = Csv.Read(csv);
        if (rows.Count < 2)
            return new([], [], ["The file has no rows under its headings."]);
        var columns = Columns(rows[0]);
        if (!columns.ContainsKey("number"))
            return new([], [], ["The file needs a column with the quote or invoice number, headed Number, Invoice number or Quote number."]);
        if (!columns.ContainsKey("customer"))
            return new([], [], ["The file needs a column with the client, headed Client, Customer or Contact name."]);
        var headingKind = rows[0].Select(Key).Any(h => h is "quotenumber" or "estimatenumber" or "quoteno") ? DocumentKind.Quote
            : rows[0].Select(Key).Any(h => h is "invoicenumber" or "invoiceno") ? DocumentKind.Invoice : kind;

        var used = existing.Where(d => OwnInvoicing.NumberOf(d) is not null || d.Document.Number is not null)
            .Select(d => (d.Document.Kind, Number: (OwnInvoicing.NumberOf(d) ?? d.Document.Number)!.Trim().ToUpperInvariant())).ToHashSet();
        var problems = new List<string>();
        var groups = new List<(DocumentKind Kind, string Number, List<(int Row, IReadOnlyList<string> Cells)> Rows)>();
        for (var r = 1; r < rows.Count; r++)
        {
            var cells = rows[r];
            var number = Cell(cells, columns, "number");
            if (number.Length == 0)
            {
                problems.Add($"Row {r + 1} has no number, so it was left out.");
                continue;
            }
            var rowKind = KindOf(Cell(cells, columns, "kind")) ?? headingKind ?? KindFromNumber(number);
            if (rowKind is not { } k)
            {
                problems.Add($"Row {r + 1} ({number}) doesn't say whether it's a quote or an invoice, so it was left out.");
                continue;
            }
            var group = groups.FindIndex(g => g.Kind == k && string.Equals(g.Number, number, StringComparison.OrdinalIgnoreCase));
            if (group < 0)
                groups.Add((k, number, [(r + 1, cells)]));
            else
                groups[group].Rows.Add((r + 1, cells));
        }

        var documents = new List<StoredDocument>();
        var skipped = new List<string>();
        foreach (var (k, number, groupRows) in groups)
        {
            if (used.Contains((k, number.ToUpperInvariant())))
            {
                skipped.Add($"{number} is already here.");
                continue;
            }
            var (document, problem) = Build(k, number, groupRows, columns, now, currency);
            if (problem is not null)
                problems.Add(problem);
            else
                documents.Add(document!);
        }
        return new(documents, skipped, problems);
    }

    /// <summary>"23 invoices and 9 quotes to bring in. 4 are here already. 1 row was left out."</summary>
    public static string Summary(DocumentImportPlan plan)
    {
        var parts = new List<string>();
        var what = new[] { Count(plan.Invoices, "invoice"), Count(plan.Quotes, "quote") }.Where(t => t.Length > 0).ToList();
        parts.Add(what.Count == 0 ? "Nothing new to bring in." : string.Join(" and ", what) + " to bring in.");
        if (plan.Skipped.Count > 0)
            parts.Add(plan.Skipped.Count == 1 ? "1 is here already." : $"{plan.Skipped.Count} are here already.");
        if (plan.Problems.Count > 0)
            parts.Add(plan.Problems.Count == 1 ? "1 problem:" : $"{plan.Problems.Count} problems:");
        return string.Join(" ", parts);
    }

    private static (StoredDocument? Document, string? Problem) Build(DocumentKind kind, string number, List<(int Row, IReadOnlyList<string> Cells)> rows,
        Dictionary<string, int> columns, DateTimeOffset now, string currency)
    {
        var first = rows[0].Cells;
        string Get(string column) => rows.Select(r => Cell(r.Cells, columns, column)).FirstOrDefault(v => v.Length > 0) ?? "";
        var customer = Get("customer");
        if (customer.Length == 0)
            return (null, $"{number} (row {rows[0].Row}) has no client, so it was left out.");
        var date = Date(Get("date"));
        if (Get("date").Length > 0 && date is null)
            return (null, $"{number} has a date that couldn't be read ({Get("date")}). Use a date like 2026-11-18 or 18/11/2026.");

        var lines = new List<Line>();
        foreach (var (_, cells) in rows)
        {
            var description = Cell(cells, columns, "description");
            var quantity = Number(Cell(cells, columns, "quantity"));
            var price = Money.Parse(Cell(cells, columns, "price"));
            var amount = Money.Parse(Cell(cells, columns, "amount"));
            if (description.Length == 0 && price is null && amount is null)
                continue;
            if (price is null && amount is not null)
                (quantity, price) = (quantity is > 0 ? quantity : 1, quantity is > 0 ? Money.Cents(amount.Value / quantity.Value) : amount);
            lines.Add(price is null
                ? new Line { Description = description }
                : new Line { Description = description.Length > 0 ? description : "As quoted", Quantity = quantity ?? 1, UnitPrice = price, SourceLineTotal = amount });
        }
        var total = Money.Parse(Get("total"));
        if (lines.Count == 0 && total is null)
            return (null, $"{number} has no lines and no total, so it was left out.");
        if (lines.Count == 0)
            lines.Add(new Line { Description = $"As per {(kind == DocumentKind.Quote ? "quote" : "invoice")} {number}", Quantity = 1, UnitPrice = total });
        var linesTotal = Money.Cents(lines.Sum(l => l.SourceLineTotal ?? l.Amount()));
        var finalTotal = total ?? linesTotal;

        var status = Get("status").Trim().ToLowerInvariant();
        var paid = Money.Parse(Get("paid"));
        var owing = Money.Parse(Get("owing"));
        var issued = new DateTimeOffset((date ?? DateOnly.FromDateTime(now.UtcDateTime)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var due = Date(Get("due"));

        var stored = new StoredDocument
        {
            Id = StoredDocument.NewId(),
            Status = status == "draft" ? DocumentStatus.Draft : DocumentStatus.Issued,
            Document = new Document
            {
                Kind = kind,
                Origin = DocumentOrigin.Import,
                Number = number.Trim(),
                Reference = Blank(Get("reference")),
                Customer = new Customer { Name = customer, Email = Blank(Get("email")) },
                Date = date,
                DueOrExpiryDate = due,
                Currency = currency,
                Lines = lines,
                Subtotal = linesTotal,
                TaxTotal = 0,
                Total = finalTotal,
                OriginalStatus = Blank(Get("status")),
            },
            Own = status == "draft" ? null : new OwnIssue { Number = number.Trim(), Issued = issued },
            Created = now,
            Updated = now,
        };
        if (status == "draft")
            return (stored, null);

        var settled = status switch
        {
            "void" or "voided" or "deleted" or "cancelled" or "canceled" => Settlement.Voided,
            "accepted" when kind == DocumentKind.Quote => Settlement.Accepted,
            "declined" or "rejected" when kind == DocumentKind.Quote => Settlement.Declined,
            "invoiced" or "closed" when kind == DocumentKind.Quote => Settlement.Invoiced,
            _ => (Settlement?)null,
        };
        if (settled is not null)
            return (stored with
            {
                Settled = new SettledState { On = issued, As = settled },
                Own = settled == Settlement.Voided ? stored.Own! with { Voided = issued } : stored.Own,
            }, null);
        if (kind == DocumentKind.Invoice)
        {
            var paidSoFar = status == "paid" ? finalTotal : paid ?? (owing is { } left ? Money.Cents(finalTotal - left) : 0);
            if (paidSoFar > 0)
            {
                var on = Date(Get("paidon")) ?? due ?? date ?? DateOnly.FromDateTime(now.UtcDateTime);
                stored = stored with { Own = stored.Own! with { Payments = [new OwnPayment(on, Math.Min(paidSoFar, finalTotal), "Brought in as paid", now)] } };
                if (OwnInvoicing.Due(stored) == 0)
                    stored = stored with { Settled = new SettledState { On = issued, As = Settlement.Paid }, Xero = stored.Xero with { PaidOn = on } };
            }
        }
        return (stored, null);
    }

    private static Dictionary<string, int> Columns(IReadOnlyList<string> header)
    {
        var keys = header.Select(Key).ToList();
        var columns = new Dictionary<string, int>();
        foreach (var (field, names) in Headings)
        {
            // The first name in the list wins over later ones, so "Total" isn't taken for "Line amount".
            foreach (var name in names)
            {
                var at = keys.IndexOf(name);
                if (at >= 0 && !columns.ContainsValue(at))
                {
                    columns[field] = at;
                    break;
                }
            }
        }
        return columns;
    }

    // "*InvoiceNumber" and "Invoice number" both read as invoicenumber.
    private static string Key(string heading)
    {
        var key = new StringBuilder();
        foreach (var c in heading.Trim().ToLowerInvariant())
            if (char.IsAsciiLetterOrDigit(c))
                key.Append(c);
        return key.ToString();
    }

    private static string Cell(IReadOnlyList<string> cells, Dictionary<string, int> columns, string field) =>
        columns.TryGetValue(field, out var at) && at < cells.Count ? cells[at].Trim() : "";

    private static DocumentKind? KindOf(string text) => text.Trim().ToLowerInvariant() switch
    {
        "quote" or "quotes" or "estimate" or "estimates" or "quotation" => DocumentKind.Quote,
        "invoice" or "invoices" or "tax invoice" or "sales invoice" => DocumentKind.Invoice,
        _ => null,
    };

    // QU-1024 and EST-1 read as quotes, INV-0112 as an invoice; anything else has to say.
    private static DocumentKind? KindFromNumber(string number)
    {
        var start = number.Trim().ToUpperInvariant();
        return start.StartsWith("QU", StringComparison.Ordinal) || start.StartsWith("EST", StringComparison.Ordinal) ? DocumentKind.Quote
            : start.StartsWith("INV", StringComparison.Ordinal) ? DocumentKind.Invoice
            : null;
    }

    private static DateOnly? Date(string text) =>
        text.Length > 0 && DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var when)
            ? DateOnly.FromDateTime(when) : null;

    private static decimal? Number(string text) =>
        decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string Count(int n, string noun) => n == 0 ? "" : n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string? Blank(string value) => value.Trim().Length == 0 ? null : value.Trim();
}

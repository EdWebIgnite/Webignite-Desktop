using System.Globalization;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;

namespace Webignite.Xero;

/// <summary>Reads where invoices and quotes stand in Xero: status, amount due and payments. Read only.</summary>
public sealed class XeroStandings(XeroApi api)
{
    /// <summary>Xero takes many invoice IDs in one request; this keeps each address a sensible length.</summary>
    public const int IdsPerRequest = 40;

    /// <summary>Invoices by Xero ID. An invoice Xero doesn't return is left out.</summary>
    public async Task<IReadOnlyDictionary<string, XeroStanding>> InvoicesAsync(IEnumerable<string> ids, CancellationToken cancel = default)
    {
        var found = new Dictionary<string, XeroStanding>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in ids.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(IdsPerRequest))
        {
            var rows = await api.AllAsync("Invoices", "&IDs=" + string.Join(",", chunk), cancel);
            foreach (var row in rows)
                found[XeroApi.Str(row["InvoiceID"])] = ToStanding(row);
        }
        return found;
    }

    /// <summary>Quotes by Xero ID, one at a time: Xero can't fetch quotes by a list of IDs.</summary>
    public async Task<IReadOnlyDictionary<string, XeroStanding>> QuotesAsync(IEnumerable<string> ids, CancellationToken cancel = default)
    {
        var found = new Dictionary<string, XeroStanding>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if ((await api.GetAsync($"Quotes/{id}", cancel))["Quotes"]?[0] is JsonObject row)
                found[id] = ToStanding(row);
        }
        return found;
    }

    public static XeroStanding ToStanding(JsonObject row) => new(
        XeroApi.Str(row["Status"]),
        Amount(row["AmountDue"]),
        Amount(row["AmountPaid"]),
        XeroPayload.NormalizeDate(XeroApi.Str(row["FullyPaidOnDate"])) is { } iso
            ? DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null);

    private static decimal? Amount(JsonNode? node) => node is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;
}

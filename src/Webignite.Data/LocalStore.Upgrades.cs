using System.Text.Json.Nodes;

namespace Webignite.Data;

/// <summary>The steps <see cref="LocalStore.Upgrade"/> takes, one per schema version.</summary>
public sealed partial class LocalStore
{
    // Version 2: a document's Xero, delivery and settled fields moved into groups of their own.
    private void GroupDocumentFields()
    {
        var rows = new List<(string Id, byte[] Payload)>();
        using (var command = Command("SELECT id, payload FROM documents"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                rows.Add((reader.GetString(0), (byte[])reader[1]));

        foreach (var (id, payload) in rows)
        {
            var context = DocumentContext + id;
            var record = JsonNode.Parse(vault.Decrypt(payload, context))!.AsObject();
            StoredDocumentUpgrade.Group(record);
            Run("UPDATE documents SET payload = $payload WHERE id = $id",
                ("$id", id), ("$payload", vault.Encrypt(System.Text.Encoding.UTF8.GetBytes(record.ToJsonString()), context)));
        }
    }
}

/// <summary>Moves a document saved before version 2 into the grouped shape. Safe to run on one already moved.</summary>
public static class StoredDocumentUpgrade
{
    private static readonly (string Old, string Group, string New)[] Moves =
    [
        ("xero_id", "xero", "id"),
        ("xero_number", "xero", "number"),
        ("xero_state", "xero", "state"),
        ("xero_contact_id", "xero", "contact_id"),
        ("xero_sent", "xero", "sent"),
        ("xero_problems", "xero", "problems"),
        ("xero_status", "xero", "status"),
        ("xero_amount_due", "xero", "amount_due"),
        ("xero_amount_paid", "xero", "amount_paid"),
        ("paid_on", "xero", "paid_on"),
        ("xero_checked", "xero", "checked"),
        ("approved", "delivery", "approved"),
        ("emailed", "delivery", "emailed"),
        ("emailed_to", "delivery", "emailed_to"),
        ("chased", "delivery", "chased"),
        ("chase_count", "delivery", "chase_count"),
    ];

    public static void Group(JsonObject record)
    {
        foreach (var (old, group, name) in Moves)
        {
            if (!record.Remove(old, out var value))
                continue;
            if (record[group] is not JsonObject target)
                record[group] = target = [];
            target[name] = value;
        }

        // "settled" was a time with the reason beside it in "settled_as"; now both sit in one object.
        record.Remove("settled_as", out var reason);
        var settled = record["settled"];
        if (settled is JsonObject)
            return;
        var on = settled?.DeepClone() ?? (reason is null ? null : record["updated"]?.DeepClone());
        record.Remove("settled");
        if (on is not null)
            record["settled"] = new JsonObject { ["on"] = on, ["as"] = reason };
    }
}

/// <summary>The data was saved by a newer Webignite than this one, which would misread it.</summary>
public sealed class NewerDataException() : InvalidOperationException(
    "Webignite's data on this PC was saved by a newer version of Webignite. Nothing was changed. " +
    "Update Webignite, then open it again.");

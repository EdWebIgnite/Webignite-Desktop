using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Webignite.Core.Documents;
using Webignite.Core.Xero;
using Webignite.Data;

namespace Webignite.Tests;

/// <summary>Quotes and invoices saved before their fields were grouped still read the same after the upgrade.</summary>
public sealed class DocumentUpgradeTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-upgrade-").FullName;
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private string DbPath => Path.Combine(folder, "webignite.db");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    // A paid invoice as version 1 saved it: every field flat on the record.
    private static JsonObject OldInvoice(string id)
    {
        var record = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(
            new StoredDocument { Id = id, Document = Samples.Completed(Samples.NewInvoice()), Status = DocumentStatus.InXero },
            StoredJson.Options))!.AsObject();
        record.Remove("xero");
        record.Remove("delivery");
        record["xero_id"] = "x-1";
        record["xero_number"] = "INV-0101";
        record["xero_state"] = "verified";
        record["xero_contact_id"] = "c-1";
        record["xero_sent"] = "{\"Type\":\"ACCREC\"}";
        record["xero_problems"] = new JsonArray("one");
        record["xero_status"] = "PAID";
        record["xero_amount_due"] = 0;
        record["xero_amount_paid"] = 2725.5;
        record["paid_on"] = "2026-10-28";
        record["xero_checked"] = "2026-10-29T08:00:00+02:00";
        record["approved"] = "2026-10-01T09:00:00+02:00";
        record["emailed"] = "2026-10-01T09:05:00+02:00";
        record["emailed_to"] = "Thandi Mokoena";
        record["chased"] = "2026-10-20T10:00:00+02:00";
        record["chase_count"] = 2;
        record["settled"] = "2026-10-29T08:00:00+02:00";
        record["settled_as"] = "paid";
        return record;
    }

    // Saves a version 1 database holding the given records, as the earlier app left it.
    private void SaveOld(params JsonObject[] records)
    {
        using (var store = new LocalStore(DbPath, new Vault(key)))
            store.Save(new StoredDocument { Id = "placeholder", Document = Samples.Completed(Samples.NewInvoice()) });
        var vault = new Vault(key);
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var clear = connection.CreateCommand();
        clear.CommandText = "DELETE FROM documents; PRAGMA user_version = 1;";
        clear.ExecuteNonQuery();
        foreach (var record in records)
        {
            var id = record["id"]!.GetValue<string>();
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO documents (id, kind, status, created, updated, payload) VALUES ($id, 'Invoice', 'InXero', $t, $t, $p)";
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$t", "2026-10-01T00:00:00.0000000+00:00");
            insert.Parameters.AddWithValue("$p", vault.Encrypt(Encoding.UTF8.GetBytes(record.ToJsonString()), "document:" + id));
            insert.ExecuteNonQuery();
        }
    }

    [Fact]
    public void Every_field_of_an_old_document_reads_the_same_after_the_upgrade()
    {
        SaveOld(OldInvoice("d1"));

        using var store = new LocalStore(DbPath, new Vault(key));
        var d = store.Get("d1")!;

        Assert.Equal(LocalStore.SchemaVersion, store.StoredSchemaVersion);
        Assert.Equal("x-1", d.Xero.Id);
        Assert.Equal("INV-0101", d.Xero.Number);
        Assert.Equal(XeroWriteState.Verified, d.Xero.State);
        Assert.Equal("c-1", d.Xero.ContactId);
        Assert.Equal("{\"Type\":\"ACCREC\"}", d.Xero.Sent);
        Assert.Equal(["one"], d.Xero.Problems);
        Assert.Equal("PAID", d.Xero.Status);
        Assert.Equal(0m, d.Xero.AmountDue);
        Assert.Equal(2725.5m, d.Xero.AmountPaid);
        Assert.Equal(new DateOnly(2026, 10, 28), d.Xero.PaidOn);
        Assert.Equal(DateTimeOffset.Parse("2026-10-29T08:00:00+02:00", System.Globalization.CultureInfo.InvariantCulture), d.Xero.Checked);
        Assert.NotNull(d.Delivery.Approved);
        Assert.NotNull(d.Delivery.Emailed);
        Assert.Equal("Thandi Mokoena", d.Delivery.EmailedTo);
        Assert.NotNull(d.Delivery.Chased);
        Assert.Equal(2, d.Delivery.ChaseCount);
        Assert.Equal(Settlement.Paid, d.Settled?.As);
        Assert.Equal(d.Xero.Checked, d.Settled?.On);
    }

    [Fact]
    public void A_document_never_sent_or_settled_stays_clear()
    {
        var draft = OldInvoice("d2");
        foreach (var name in new[] { "xero_id", "xero_number", "xero_state", "xero_contact_id", "xero_sent", "xero_problems", "xero_status",
                     "xero_amount_due", "xero_amount_paid", "paid_on", "xero_checked", "approved", "emailed", "emailed_to", "chased", "chase_count",
                     "settled", "settled_as" })
            draft.Remove(name);
        SaveOld(draft);

        using var store = new LocalStore(DbPath, new Vault(key));
        var d = store.Get("d2")!;

        Assert.Equal(new XeroLink(), d.Xero with { Problems = [] });
        Assert.Equal(new Delivery(), d.Delivery);
        Assert.Null(d.Settled);
    }

    [Fact]
    public void Settled_without_a_reason_is_still_settled()
    {
        // Saved before the reason was kept: it must not start being chased again.
        var old = OldInvoice("d3");
        old.Remove("settled_as");
        SaveOld(old);

        using var store = new LocalStore(DbPath, new Vault(key));
        var d = store.Get("d3")!;

        Assert.NotNull(d.Settled);
        Assert.Null(d.Settled.As);
        Assert.Empty(Chasers.Find([d], new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Running_the_upgrade_twice_changes_nothing()
    {
        var once = OldInvoice("d4");
        StoredDocumentUpgrade.Group(once);
        var twice = once.DeepClone().AsObject();
        StoredDocumentUpgrade.Group(twice);
        Assert.Equal(once.ToJsonString(), twice.ToJsonString());
    }

    [Fact]
    public void A_database_from_a_newer_version_is_left_alone()
    {
        using (var store = new LocalStore(DbPath, new Vault(key)))
            store.Save(new StoredDocument { Id = "d5", Document = Samples.Completed(Samples.NewInvoice()) });
        using (var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {LocalStore.SchemaVersion + 1}";
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<NewerDataException>(() => new LocalStore(DbPath, new Vault(key)));
        Assert.Contains("Update Webignite", error.Message);
    }
}

using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Webignite.Data;

/// <summary>
/// The few ways every table is read and written: an encrypted payload under an id, with a readable column or
/// two (a date, a kind) where lists need them. The area files build on these instead of repeating the SQL.
/// </summary>
public sealed partial class LocalStore
{
    private SqliteCommand Command(string sql, params (string Name, object? Value)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private void Run(string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(sql, values);
        command.ExecuteNonQuery();
    }

    /// <summary>Saves a record in a table that holds only an id and its payload.</summary>
    private T Put<T>(string table, string context, string id, T value)
    {
        Run($"INSERT INTO {table} (id, payload) VALUES ($id, $payload) ON CONFLICT (id) DO UPDATE SET payload = $payload",
            ("$id", id), ("$payload", Seal(context + id, value)));
        return value;
    }

    /// <summary>One record by id, or null.</summary>
    private T? Find<T>(string table, string context, string id) where T : class
    {
        using var command = Command($"SELECT payload FROM {table} WHERE id = $id", ("$id", id));
        return command.ExecuteScalar() is byte[] payload ? Open<T>(context + id, payload) : null;
    }

    private void Remove(string table, string id) => Run($"DELETE FROM {table} WHERE id = $id", ("$id", id));

    /// <summary>The records a query finds. The query selects the id, then the payload.</summary>
    private List<T> ReadAll<T>(string context, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(sql, values);
        using var reader = command.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
            list.Add(Open<T>(context + reader.GetString(0), (byte[])reader[1]));
        return list;
    }

    /// <summary>The text values a query finds in its first column, such as keys already used.</summary>
    private HashSet<string> ReadKeys(string sql)
    {
        using var command = Command(sql);
        using var reader = command.ExecuteReader();
        var keys = new HashSet<string>();
        while (reader.Read())
            keys.Add(reader.GetString(0));
        return keys;
    }

    /// <summary>
    /// Runs several writes as one: all of them land or none does. IMMEDIATE takes the write lock up front, so a
    /// read inside can't be overtaken by the connector writing in between.
    /// </summary>
    private T InOneGo<T>(Func<T> work)
    {
        Execute("BEGIN IMMEDIATE");
        inOneGo.Value++;
        try
        {
            var result = work();
            Execute("COMMIT");
            return result;
        }
        catch
        {
            Execute("ROLLBACK");
            throw;
        }
        finally
        {
            inOneGo.Value--;
        }
    }

    /// <summary>A date as it is kept in a readable column, the same on every PC.</summary>
    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

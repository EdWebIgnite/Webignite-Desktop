using System.Text.Json;
using Webignite.Core.Sync;

namespace Webignite.Data;

/// <summary>Settings, each one encrypted under its name.</summary>
public sealed partial class LocalStore
{
    public T? GetSetting<T>(string name)
    {
        using var command = Command("SELECT payload FROM settings WHERE name = $name", ("$name", name));
        return command.ExecuteScalar() is byte[] payload ? Open<T>("setting:" + name, payload) : default;
    }

    /// <summary>Saves a setting; one the business shares (<see cref="SharedSettings"/>) is noted as changed now, so it goes to the other PC.</summary>
    public void SetSetting<T>(string name, T value)
    {
        if (!SharedSettings.IsShared(name))
        {
            Write(name, value);
            return;
        }
        InOneGo(() =>
        {
            Write(name, value);
            Stamp(name, clock.GetUtcNow());
            return 0;
        });
    }

    /// <summary>The shared settings this PC has, as kept, with when each was last changed (the earliest time when never noted).</summary>
    public IReadOnlyList<SharedSetting> ListSharedSettings()
    {
        var stamps = SharedStamps();
        var all = new List<SharedSetting>();
        foreach (var name in SharedSettings.Names)
        {
            if (GetSetting<JsonElement?>(name) is { } value)
                all.Add(new SharedSetting(name, value, stamps.GetValueOrDefault(name, DateTimeOffset.MinValue)));
        }
        return all;
    }

    /// <summary>When a shared setting was last changed here, or null when it was never noted.</summary>
    public DateTimeOffset? SharedSettingChanged(string name) => SharedStamps().TryGetValue(name, out var at) ? at : null;

    /// <summary>A shared setting from the other PC, kept with the time it was changed there, so it doesn't go back.</summary>
    public void PutSettingAsSynced(string name, JsonElement value, DateTimeOffset updated) => InOneGo(() =>
    {
        if (!SharedSettings.IsShared(name))
            throw new ArgumentException("Not a shared setting", nameof(name));
        Write(name, value);
        Stamp(name, updated);
        return 0;
    });

    private Dictionary<string, DateTimeOffset> SharedStamps() => GetSetting<Dictionary<string, DateTimeOffset>>(SharedSettings.StampsName) ?? [];

    private void Stamp(string name, DateTimeOffset at)
    {
        var stamps = SharedStamps();
        stamps[name] = at;
        Write(SharedSettings.StampsName, stamps);
    }

    private void Write<T>(string name, T value) =>
        Run("INSERT INTO settings (name, payload) VALUES ($name, $payload) ON CONFLICT (name) DO UPDATE SET payload = $payload",
            ("$name", name), ("$payload", Seal("setting:" + name, value)));
}

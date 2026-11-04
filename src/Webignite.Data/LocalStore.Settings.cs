namespace Webignite.Data;

/// <summary>Settings, each one encrypted under its name.</summary>
public sealed partial class LocalStore
{
    public T? GetSetting<T>(string name)
    {
        using var command = Command("SELECT payload FROM settings WHERE name = $name", ("$name", name));
        return command.ExecuteScalar() is byte[] payload ? Open<T>("setting:" + name, payload) : default;
    }

    public void SetSetting<T>(string name, T value) =>
        Run("INSERT INTO settings (name, payload) VALUES ($name, $payload) ON CONFLICT (name) DO UPDATE SET payload = $payload",
            ("$name", name), ("$payload", Seal("setting:" + name, value)));
}

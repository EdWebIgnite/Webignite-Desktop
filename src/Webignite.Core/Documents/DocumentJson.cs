using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webignite.Core.Documents;

/// <summary>
/// The JSON shape assistants send, in snake_case to match the schema the Python version used.
/// Unknown fields are rejected so a typo cannot silently drop data.
/// </summary>
public static class DocumentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        WriteIndented = true,
    };

    public static Document Parse(string json) =>
        JsonSerializer.Deserialize<Document>(json, Options) ?? throw new JsonException("Empty document.");

    public static string Write(Document document) => JsonSerializer.Serialize(document, Options);
}

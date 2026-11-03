using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webignite.Data;

/// <summary>
/// How the app's own records are written to and read from the database. The same snake_case shape as
/// <see cref="Webignite.Core.Documents.DocumentJson"/>, with one difference: a field this version of the app
/// doesn't know is skipped, not refused. That is what lets a later version rename or drop a property without
/// the app refusing to open the data it saved earlier. Assistant input keeps the strict options.
/// </summary>
public static class StoredJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };
}

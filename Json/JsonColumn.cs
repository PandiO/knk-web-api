using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Json;

/// <summary>
/// (De)serialises the JSON columns (<c>*Json</c> string properties) of entities that keep a
/// list or object inline. PascalCase property names, nulls omitted, a bad or empty column reads as
/// default - the options GameSettings has always used (extracted from Services/GameSettingsJson,
/// road navigation plan R29).
/// </summary>
public static class JsonColumn
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T? value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return default;
        }
    }

    public static List<T> DeserializeList<T>(string? json)
    {
        return Deserialize<List<T>>(json) ?? new List<T>();
    }
}

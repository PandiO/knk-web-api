using System.Collections.Generic;
using knkwebapi_v2.Json;

namespace knkwebapi_v2.Services;

/// <summary>GameSettings' JSON columns; the options now live in <see cref="JsonColumn"/>
/// (road navigation plan R29) so every JSON column in the schema is read and written the same way.</summary>
internal static class GameSettingsJson
{
    public static string Serialize<T>(T? value) => JsonColumn.Serialize(value);

    public static T? Deserialize<T>(string? json) => JsonColumn.Deserialize<T>(json);

    public static List<T> DeserializeList<T>(string? json) => JsonColumn.DeserializeList<T>(json);
}

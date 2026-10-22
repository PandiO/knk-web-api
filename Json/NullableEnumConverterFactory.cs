using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Json;

/// <summary>
/// KNG-195: reads an empty/whitespace JSON string as null for every nullable enum, the same tolerance
/// <see cref="NullableDoubleConverter"/> gives numbers. The form wizard sends "" when the empty option of
/// an Enum field is picked again (e.g. a User's Gender); JsonStringEnumConverter throws on it, binding
/// fails and the whole request is answered 400. Any other value is read and written by the converter
/// registered for the enum itself (JsonStringEnumConverter in Program.cs), so names and numbers still work.
/// Registered globally before JsonStringEnumConverter.
/// </summary>
public class NullableEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        Nullable.GetUnderlyingType(typeToConvert)?.IsEnum == true;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(NullableEnumConverter<>).MakeGenericType(Nullable.GetUnderlyingType(typeToConvert)!))!;

    private sealed class NullableEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String && string.IsNullOrWhiteSpace(reader.GetString())) return null;
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value.HasValue) JsonSerializer.Serialize(writer, value.Value, options);
            else writer.WriteNullValue();
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocViewer.Domain;

/// <summary>Serialises a <see cref="Vin"/> as its plain string value, validating on the way in.</summary>
public sealed class VinJsonConverter : JsonConverter<Vin>
{
    public override Vin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Vin.TryParse(reader.GetString(), out var vin, out var error) ? vin : throw new JsonException(error);

    public override void Write(Utf8JsonWriter writer, Vin value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Value);
    }
}

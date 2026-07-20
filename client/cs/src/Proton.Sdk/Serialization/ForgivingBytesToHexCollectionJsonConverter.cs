using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proton.Sdk.Serialization;

// Applies ForgivingBytesToHexJsonConverter to each element, so a collection property of hash digests can be
// serialized as an array of hex strings without registering the element converter globally.
public sealed class ForgivingBytesToHexCollectionJsonConverter : JsonConverter<IReadOnlyList<ReadOnlyMemory<byte>>>
{
    private static readonly ForgivingBytesToHexJsonConverter ElementConverter = new();

    public override IReadOnlyList<ReadOnlyMemory<byte>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected an array of hex strings");
        }

        var result = new List<ReadOnlyMemory<byte>>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            result.Add(ElementConverter.Read(ref reader, typeof(ReadOnlyMemory<byte>), options));
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<ReadOnlyMemory<byte>> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
        {
            ElementConverter.Write(writer, item, options);
        }

        writer.WriteEndArray();
    }
}

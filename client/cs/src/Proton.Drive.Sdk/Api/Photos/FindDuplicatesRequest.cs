using System.Text.Json.Serialization;
using Proton.Sdk.Serialization;

namespace Proton.Drive.Sdk.Api.Photos;

internal sealed class FindDuplicatesRequest
{
    [JsonConverter(typeof(ForgivingBytesToHexCollectionJsonConverter))]
    public required IReadOnlyList<ReadOnlyMemory<byte>> NameHashes { get; init; }
}

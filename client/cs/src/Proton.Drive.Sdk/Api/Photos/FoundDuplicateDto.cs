using System.Text.Json.Serialization;
using Proton.Drive.Sdk.Api.Links;
using Proton.Sdk.Serialization;

namespace Proton.Drive.Sdk.Api.Photos;

internal sealed class FoundDuplicateDto
{
    [JsonConverter(typeof(ForgivingBytesToHexJsonConverter))]
    public ReadOnlyMemory<byte> Hash { get; init; }

    [JsonConverter(typeof(ForgivingBytesToHexJsonConverter))]
    public ReadOnlyMemory<byte> ContentHash { get; init; }

    public int? LinkState { get; init; }

    [JsonPropertyName("ClientUID")]
    public string? ClientUid { get; init; }

    [JsonPropertyName("LinkID")]
    public LinkId? LinkId { get; init; }
}

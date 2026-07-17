using System.Text.Json.Serialization;
using Proton.Drive.Sdk.Api.Links;
using Proton.Sdk.Serialization;

namespace Proton.Drive.Sdk.Api.Photos;

internal sealed class PhotoAlbumInclusionDto
{
    [JsonPropertyName("AlbumLinkID")]
    public required LinkId Id { get; init; }

    [JsonPropertyName("Hash")]
    [JsonConverter(typeof(ForgivingBytesToHexJsonConverter))]
    public required ReadOnlyMemory<byte> NameHash { get; init; }

    [JsonConverter(typeof(ForgivingBytesToHexJsonConverter))]
    public required ReadOnlyMemory<byte> ContentHash { get; init; }

    [JsonConverter(typeof(EpochSecondsJsonConverter))]
    [JsonPropertyName("AddedTime")]
    public required DateTime CreationTime { get; init; }
}

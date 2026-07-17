using System.Text.Json.Serialization;
using Proton.Drive.Sdk.Api.Links;
using Proton.Sdk.Serialization;

namespace Proton.Drive.Sdk.Api.Photos;

internal sealed class RelatedPhotoDto
{
    [JsonPropertyName("LinkID")]
    public required LinkId Id { get; init; }

    [JsonConverter(typeof(EpochSecondsJsonConverter))]
    public required DateTime CaptureTime { get; init; }

    [JsonPropertyName("Hash")]
    [JsonConverter(typeof(ForgivingBytesToHexJsonConverter))]
    public required ReadOnlyMemory<byte> NameHash { get; init; }

    [JsonConverter(typeof(ForgivingBytesToHexJsonConverter))]
    public ReadOnlyMemory<byte>? ContentHash { get; init; }
}

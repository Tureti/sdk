using System.Text.Json.Serialization;
using Proton.Drive.Sdk.Api.Links;
using Proton.Drive.Sdk.Volumes;

namespace Proton.Drive.Sdk.Api.Shares;

internal sealed class SharedWithMeLinkDto
{
    [JsonPropertyName("VolumeID")]
    public required VolumeId VolumeId { get; init; }

    [JsonPropertyName("ShareID")]
    public required ShareId ShareId { get; init; }

    [JsonPropertyName("LinkID")]
    public required LinkId LinkId { get; init; }

    public required ShareTargetType ShareTargetType { get; init; }
}

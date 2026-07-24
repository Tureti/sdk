using System.Text.Json.Serialization;
using Proton.Drive.Sdk.Api.Links;

namespace Proton.Drive.Sdk.Api.Shares;

internal sealed class SharedByMeLinkDto
{
    [JsonPropertyName("ShareID")]
    public required ShareId ShareId { get; init; }

    [JsonPropertyName("LinkID")]
    public required LinkId LinkId { get; init; }

    [JsonPropertyName("ContextShareID")]
    public required ShareId ContextShareId { get; init; }
}

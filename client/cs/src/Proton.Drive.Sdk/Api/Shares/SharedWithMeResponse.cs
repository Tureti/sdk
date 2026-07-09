using System.Text.Json.Serialization;
using Proton.Sdk.Api;

namespace Proton.Drive.Sdk.Api.Shares;

internal sealed class SharedWithMeResponse : ApiResponse
{
    public required IReadOnlyList<SharedWithMeLinkDto> Links { get; init; }

    [JsonPropertyName("AnchorID")]
    public string? AnchorId { get; init; }

    public bool More { get; init; }
}

using System.Text.Json.Serialization;
using Proton.Drive.Sdk.Api.Links;
using Proton.Sdk.Api;

namespace Proton.Drive.Sdk.Api.Files;

internal sealed class SmallUploadResponse : ApiResponse
{
    [JsonPropertyName("LinkID")]
    public required LinkId LinkId { get; init; }

    [JsonPropertyName("RevisionID")]
    public required RevisionId RevisionId { get; init; }
}

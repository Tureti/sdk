using System.Text.Json.Serialization;
using Proton.Sdk.Cryptography;

namespace Proton.Drive.Sdk.Api.Files;

internal sealed class SmallRevisionUploadMetadataRequest
{
    [JsonPropertyName("CurrentRevisionID")]
    public required RevisionId CurrentRevisionId { get; init; }

    public required PgpArmoredSignature ManifestSignature { get; init; }

    public required bool ChecksumVerified { get; init; }

    [JsonPropertyName("SignatureEmail")]
    public string? SignatureEmailAddress { get; init; }

    [JsonPropertyName("ContentBlockEncSignature")]
    public PgpArmoredMessage? ContentBlockEncSignature { get; init; }

    public ReadOnlyMemory<byte>? ContentBlockVerificationToken { get; init; }

    [JsonPropertyName("XAttr")]
    public PgpArmoredMessage? ExtendedAttributes { get; init; }
}

using System.Text.Json.Serialization;
using Proton.Drive.Sdk.Api.Links;
using Proton.Sdk.Cryptography;

namespace Proton.Drive.Sdk.Api.Files;

internal sealed class SmallFileUploadMetadataRequest
{
    public required PgpArmoredMessage Name { get; init; }

    [JsonPropertyName("NameHash")]
    public required string NameHash { get; init; }

    [JsonPropertyName("ParentLinkID")]
    public required LinkId ParentLinkId { get; init; }

    [JsonPropertyName("NodePassphrase")]
    public required PgpArmoredMessage NodePassphrase { get; init; }

    [JsonPropertyName("NodePassphraseSignature")]
    public required PgpArmoredSignature NodePassphraseSignature { get; init; }

    [JsonPropertyName("NodeKey")]
    public required PgpArmoredSecretKey NodeKey { get; init; }

    [JsonPropertyName("MIMEType")]
    public required string MediaType { get; init; }

    public required ReadOnlyMemory<byte> ContentKeyPacket { get; init; }

    [JsonPropertyName("ContentKeyPacketSignature")]
    public PgpArmoredSignature? ContentKeySignature { get; init; }

    public required PgpArmoredSignature ManifestSignature { get; init; }

    public required bool ChecksumVerified { get; init; }

    [JsonPropertyName("SignatureEmail")]
    public string? SignatureEmailAddress { get; init; }

    public ReadOnlyMemory<byte>? ContentBlockVerificationToken { get; init; }

    [JsonPropertyName("XAttr")]
    public PgpArmoredMessage? ExtendedAttributes { get; init; }

    // required by backend even for non-photo files; always null in Drive
    public object? Photo { get; init; }

    [JsonPropertyName("ContentBlockEncSignature")]
    public PgpArmoredMessage? ContentBlockEncSignature { get; init; }
}

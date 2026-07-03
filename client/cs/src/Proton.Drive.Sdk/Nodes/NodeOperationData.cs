using System.Text.Json.Serialization;
using Proton.Cryptography.Pgp;

namespace Proton.Drive.Sdk.Nodes;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FileOperationData), typeDiscriminator: "file")]
[JsonDerivedType(typeof(FolderOperationData), typeDiscriminator: "folder")]
internal class NodeOperationData
{
    public required NodeUid? ParentUid { get; init; }

    public required PgpPrivateKey? Key { get; init; }
    public required PgpSessionKey? PassphraseSessionKey { get; init; }
    public required PgpSessionKey? NameSessionKey { get; init; }

    [JsonPropertyName("passphrase")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReadOnlyMemory<byte>? PassphraseForAnonymousMove { get; init; }
}

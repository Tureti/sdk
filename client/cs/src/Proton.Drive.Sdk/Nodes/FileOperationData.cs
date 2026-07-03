using Proton.Cryptography.Pgp;

namespace Proton.Drive.Sdk.Nodes;

internal sealed class FileOperationData : NodeOperationData
{
    public required PgpSessionKey? ContentKey { get; init; }
}

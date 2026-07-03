using Proton.Drive.Sdk.Api.Shares;

namespace Proton.Drive.Sdk.Nodes;

internal readonly record struct FolderMetadata(
    FolderNode Node,
    FolderOperationData OperationData,
    ShareId? MembershipShareId,
    ReadOnlyMemory<byte> NameHashDigest);

using System.Diagnostics.CodeAnalysis;
using Proton.Drive.Sdk.Api.Shares;

namespace Proton.Drive.Sdk.Nodes;

internal readonly struct NodeMetadata
{
    private readonly (FileNode Node, FileOperationData Secrets)? _fileAndSecrets;
    private readonly (FolderNode Node, FolderOperationData Secrets)? _folderAndSecrets;

    public NodeMetadata(FileNode node, FileOperationData operationData, ShareId? membershipShareId, ReadOnlyMemory<byte> nameHashDigest)
    {
        _fileAndSecrets = (node, operationData);
        MembershipShareId = membershipShareId;
        NameHashDigest = nameHashDigest;
    }

    public NodeMetadata(FolderNode node, FolderOperationData operationData, ShareId? membershipShareId, ReadOnlyMemory<byte> nameHashDigest)
    {
        _folderAndSecrets = (node, operationData);
        MembershipShareId = membershipShareId;
        NameHashDigest = nameHashDigest;
    }

    public Node Node => _fileAndSecrets?.Node ?? (Node)_folderAndSecrets!.Value.Node;
    public NodeOperationData OperationData => _fileAndSecrets?.Secrets ?? (NodeOperationData)_folderAndSecrets!.Value.Secrets;
    public ShareId? MembershipShareId { get; }
    public ReadOnlyMemory<byte> NameHashDigest { get; }

    public static NodeMetadata FromFile(FileMetadata m) => new(m.Node, m.OperationData, m.MembershipShareId, m.NameHashDigest);
    public static NodeMetadata FromFolder(FolderMetadata m) => new(m.Node, m.OperationData, m.MembershipShareId, m.NameHashDigest);

    public bool TryGetFileElseFolder(
        [MaybeNullWhen(false)] out FileNode fileNode,
        [MaybeNullWhen(false)] out FileOperationData fileOperationData,
        [MaybeNullWhen(true)] out FolderNode folderNode,
        [MaybeNullWhen(true)] out FolderOperationData folderOperationData)
    {
        if (_fileAndSecrets is null)
        {
            (folderNode, folderOperationData) = _folderAndSecrets!.Value;
            fileNode = null;
            fileOperationData = null;
            return false;
        }

        (fileNode, fileOperationData) = _fileAndSecrets.Value;
        folderNode = null;
        folderOperationData = null;
        return true;
    }

    public void Deconstruct(out Node node, out NodeOperationData operationData, out ShareId? membershipShareId, out ReadOnlyMemory<byte> nameHashDigest)
    {
        node = Node;
        operationData = OperationData;
        membershipShareId = MembershipShareId;
        nameHashDigest = NameHashDigest;
    }
}

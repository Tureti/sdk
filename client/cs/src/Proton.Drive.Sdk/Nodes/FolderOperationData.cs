namespace Proton.Drive.Sdk.Nodes;

internal sealed class FolderOperationData : NodeOperationData
{
    public required ReadOnlyMemory<byte>? HashKey { get; init; }
}

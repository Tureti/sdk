namespace Proton.Drive.Sdk.Nodes;

public record FileNode : Node
{
    public required string MediaType { get; init; }

    public required Revision ActiveRevision { get; init; }

    public required long TotalStorageSize { get; init; }
}

using Proton.Drive.Sdk.Api.Links;
using Proton.Drive.Sdk.Volumes;

namespace Proton.Drive.Sdk.Nodes;

internal interface INodeProvider
{
    IAsyncEnumerable<NodeMetadata> EnumerateNodeMetadataAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        IEnumerable<LinkId> linkIds,
        ShareAndKey? knownShareAndKey,
        CancellationToken cancellationToken);
}

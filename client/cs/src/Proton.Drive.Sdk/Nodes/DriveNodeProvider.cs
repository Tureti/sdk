using System.Runtime.CompilerServices;
using Proton.Drive.Sdk.Api;
using Proton.Drive.Sdk.Api.Links;
using Proton.Drive.Sdk.Volumes;

namespace Proton.Drive.Sdk.Nodes;

internal class DriveNodeProvider(IDriveApiClients api) : INodeProvider
{
    protected IDriveApiClients Api { get; } = api;

    public async IAsyncEnumerable<NodeMetadata> EnumerateNodeMetadataAsync(
        ProtonDriveClient client,
        VolumeId volumeId,
        IEnumerable<LinkId> linkIds,
        ShareAndKey? knownShareAndKey,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var batchLoader = new NodeMetadataBatchLoader(
            client,
            volumeId,
            (linkIdsToLoad, ct) => GetLinkDetailsAsync(volumeId, linkIdsToLoad, ct),
            knownShareAndKey);

        foreach (var linkId in linkIds)
        {
            await foreach (var nodeMetadata in batchLoader.QueueAndTryLoadBatchAsync(linkId, cancellationToken).ConfigureAwait(false))
            {
                yield return nodeMetadata;
            }
        }

        await foreach (var nodeMetadata in batchLoader.LoadRemainingAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return nodeMetadata;
        }
    }

    protected virtual ValueTask<LinkDetailsResponse> GetLinkDetailsAsync(
        VolumeId volumeId,
        IEnumerable<LinkId> linkIds,
        CancellationToken cancellationToken)
    {
        return Api.Links.GetDetailsAsync(volumeId, linkIds, cancellationToken);
    }
}

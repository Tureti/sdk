using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Proton.Drive.Sdk.Api.Links;
using Proton.Drive.Sdk.Volumes;

namespace Proton.Drive.Sdk.Nodes;

internal sealed class NodeMetadataBatchLoader(
    ProtonDriveClient client,
    VolumeId volumeId,
    Func<IEnumerable<LinkId>, CancellationToken, ValueTask<LinkDetailsResponse>> getLinkDetailsAsync,
    ShareAndKey? knownShareAndKey) : BatchLoaderBase<LinkId, NodeMetadata>
{
    private readonly ProtonDriveClient _client = client;
    private readonly Func<IEnumerable<LinkId>, CancellationToken, ValueTask<LinkDetailsResponse>> _getLinkDetailsAsync = getLinkDetailsAsync;
    private readonly ShareAndKey? _knownShareAndKey = knownShareAndKey;

    protected override async IAsyncEnumerable<NodeMetadata> LoadBatchAsync(
        ReadOnlyMemory<LinkId> ids,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await _getLinkDetailsAsync(MemoryMarshal.ToEnumerable(ids), cancellationToken).ConfigureAwait(false);

        foreach (var linkDetails in response.Links)
        {
            yield return await DtoToMetadataConverter.ConvertDtoToNodeMetadataAsync(
                _client,
                volumeId,
                linkDetails,
                _knownShareAndKey,
                cancellationToken).ConfigureAwait(false);
        }
    }
}

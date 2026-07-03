using Proton.Cryptography.Pgp;
using Proton.Drive.Sdk.Api.Shares;
using Proton.Drive.Sdk.Nodes;
using Proton.Drive.Sdk.Volumes;

namespace Proton.Drive.Sdk.Caching;

internal interface IDriveCache
{
    ValueTask SetMainVolumeIdAsync(VolumeId? volumeId, CancellationToken cancellationToken);

    ValueTask<(bool Exists, VolumeId? VolumeId)> TryGetMainVolumeIdAsync(CancellationToken cancellationToken);

    ValueTask SetPhotosVolumeIdAsync(VolumeId? volumeId, CancellationToken cancellationToken);

    ValueTask<(bool Exists, VolumeId? VolumeId)> TryGetPhotosVolumeIdAsync(CancellationToken cancellationToken);

    ValueTask SetShareKeyAsync(ShareId shareId, PgpPrivateKey shareKey, CancellationToken cancellationToken);

    ValueTask<PgpPrivateKey?> TryGetShareKeyAsync(ShareId shareId, CancellationToken cancellationToken);

    ValueTask SetNodeOperationDataAsync(NodeUid nodeId, NodeOperationData operationData, CancellationToken cancellationToken);

    ValueTask<NodeOperationData?> TryGetNodeOperationDataAsync(NodeUid nodeId, CancellationToken cancellationToken);

    ValueTask ClearAsync();
}

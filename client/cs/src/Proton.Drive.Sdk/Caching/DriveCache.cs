using System.Text.Json;
using Proton.Cryptography.Pgp;
using Proton.Drive.Sdk.Api.Shares;
using Proton.Drive.Sdk.Nodes;
using Proton.Drive.Sdk.Serialization;
using Proton.Drive.Sdk.Volumes;
using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Caching;

internal sealed class DriveCache(ICacheRepository repository) : IDriveCache
{
    private const string MainVolumeIdCacheKey = "volume:main:id";
    private const string PhotosVolumeIdCacheKey = "volume:photos:id";

    private readonly ICacheRepository _repository = repository;

    public ValueTask SetMainVolumeIdAsync(VolumeId? volumeId, CancellationToken cancellationToken)
    {
        var serializedValue = JsonSerializer.Serialize(volumeId, DriveCacheSerializerContext.Default.NullableVolumeId);

        return _repository.SetAsync(MainVolumeIdCacheKey, serializedValue, cancellationToken);
    }

    public async ValueTask<(bool Exists, VolumeId? VolumeId)> TryGetMainVolumeIdAsync(CancellationToken cancellationToken)
    {
        return await _repository.TryGetDeserializedValueAsync(
            MainVolumeIdCacheKey,
            DriveCacheSerializerContext.Default.NullableVolumeId,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SetPhotosVolumeIdAsync(VolumeId? volumeId, CancellationToken cancellationToken)
    {
        var serializedValue = JsonSerializer.Serialize(volumeId, DriveCacheSerializerContext.Default.NullableVolumeId);

        return _repository.SetAsync(PhotosVolumeIdCacheKey, serializedValue, cancellationToken);
    }

    public async ValueTask<(bool Exists, VolumeId? VolumeId)> TryGetPhotosVolumeIdAsync(CancellationToken cancellationToken)
    {
        return await _repository.TryGetDeserializedValueAsync(
            PhotosVolumeIdCacheKey,
            DriveCacheSerializerContext.Default.NullableVolumeId,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SetShareKeyAsync(ShareId shareId, PgpPrivateKey shareKey, CancellationToken cancellationToken)
    {
        var serializedValue = JsonSerializer.Serialize(shareKey, DriveSecretsSerializerContext.Default.PgpPrivateKey);

        return _repository.SetAsync(GetShareKeyCacheKey(shareId), serializedValue, cancellationToken);
    }

    public async ValueTask<PgpPrivateKey?> TryGetShareKeyAsync(ShareId shareId, CancellationToken cancellationToken)
    {
        var (exists, shareKey) = await _repository.TryGetDeserializedValueAsync(
            GetShareKeyCacheKey(shareId),
            DriveSecretsSerializerContext.Default.PgpPrivateKey,
            cancellationToken).ConfigureAwait(false);

        return exists ? shareKey : null;
    }

    public ValueTask SetNodeOperationDataAsync(NodeUid nodeId, NodeOperationData operationData, CancellationToken cancellationToken)
    {
        var serializedValue = JsonSerializer.Serialize(operationData, DriveSecretsSerializerContext.Default.NodeOperationData);

        return _repository.SetAsync(GetNodeOperationDataCacheKey(nodeId), serializedValue, cancellationToken);
    }

    public async ValueTask<NodeOperationData?> TryGetNodeOperationDataAsync(NodeUid nodeId, CancellationToken cancellationToken)
    {
        var (exists, operationData) = await _repository.TryGetDeserializedValueAsync(
            GetNodeOperationDataCacheKey(nodeId),
            DriveSecretsSerializerContext.Default.NodeOperationData,
            cancellationToken).ConfigureAwait(false);

        return exists ? operationData : null;
    }

    public ValueTask ClearAsync()
    {
        return _repository.ClearAsync();
    }

    private static string GetShareKeyCacheKey(ShareId shareId)
    {
        return $"share:{shareId}:key";
    }

    private static string GetNodeOperationDataCacheKey(NodeUid nodeId)
    {
        return $"node:{nodeId}";
    }
}

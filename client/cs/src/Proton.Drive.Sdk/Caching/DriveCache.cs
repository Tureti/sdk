using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Proton.Cryptography.Pgp;
using Proton.Drive.Sdk.Api.Shares;
using Proton.Drive.Sdk.Nodes;
using Proton.Drive.Sdk.Serialization;
using Proton.Drive.Sdk.Volumes;
using Proton.Sdk;
using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Caching;

internal sealed class DriveCache(ICacheRepository? repository = null) : IDriveCache
{
    private const string MainVolumeIdCacheKey = "volume:main:id";
    private const string PhotosVolumeIdCacheKey = "volume:photos:id";

    private const int MaxMemoryEntries = 1000;
    private const double MemoryEvictionRatio = 0.25;

    private readonly ICacheRepository? _repository = repository;
    private readonly HybridMemoryCache _memoryCache = new(MaxMemoryEntries, MemoryEvictionRatio);

    public ValueTask<VolumeId?> GetOrCreateMainVolumeIdAsync(Func<CancellationToken, ValueTask<VolumeId?>> factory, CancellationToken cancellationToken)
    {
        return GetOrCreateNullableAsync(MainVolumeIdCacheKey, DriveCacheSerializerContext.Default.NullableVolumeId, factory, cancellationToken);
    }

    public ValueTask SetMainVolumeIdAsync(VolumeId? volumeId, CancellationToken cancellationToken)
    {
        return SetAsync(MainVolumeIdCacheKey, volumeId, DriveCacheSerializerContext.Default.NullableVolumeId, cancellationToken);
    }

    public ValueTask<VolumeId?> GetOrCreatePhotosVolumeIdAsync(Func<CancellationToken, ValueTask<VolumeId?>> factory, CancellationToken cancellationToken)
    {
        return GetOrCreateNullableAsync(PhotosVolumeIdCacheKey, DriveCacheSerializerContext.Default.NullableVolumeId, factory, cancellationToken);
    }

    public ValueTask SetPhotosVolumeIdAsync(VolumeId? volumeId, CancellationToken cancellationToken)
    {
        return SetAsync(PhotosVolumeIdCacheKey, volumeId, DriveCacheSerializerContext.Default.NullableVolumeId, cancellationToken);
    }

    public ValueTask<PgpPrivateKey> GetOrCreateShareKeyAsync(
        ShareId shareId,
        Func<CancellationToken, ValueTask<PgpPrivateKey>> factory,
        CancellationToken cancellationToken)
    {
        return GetOrCreateAsync(GetShareKeyCacheKey(shareId), DriveSecretsSerializerContext.Default.PgpPrivateKey, factory, cancellationToken);
    }

    public ValueTask SetShareKeyAsync(ShareId shareId, PgpPrivateKey shareKey, CancellationToken cancellationToken)
    {
        return SetAsync(GetShareKeyCacheKey(shareId), shareKey, DriveSecretsSerializerContext.Default.PgpPrivateKey, cancellationToken);
    }

    public ValueTask<DriveCacheAcquisition<NodeOperationData>> TryAcquireNodeOperationDataAsync(NodeUid nodeId, CancellationToken cancellationToken)
    {
        return TryAcquireAsync(
            GetNodeOperationDataCacheKey(nodeId),
            DriveSecretsSerializerContext.Default.NodeOperationData,
            Option<NodeOperationData>.FromNullable,
            cancellationToken);
    }

    public ValueTask<NodeOperationData> GetOrCreateNodeOperationDataAsync(
        NodeUid nodeId,
        Func<CancellationToken, ValueTask<NodeOperationData>> factory,
        CancellationToken cancellationToken)
    {
        return GetOrCreateAsync(GetNodeOperationDataCacheKey(nodeId), DriveSecretsSerializerContext.Default.NodeOperationData, factory, cancellationToken);
    }

    public ValueTask SetNodeOperationDataAsync(NodeUid nodeId, NodeOperationData operationData, CancellationToken cancellationToken)
    {
        return SetAsync(GetNodeOperationDataCacheKey(nodeId), operationData, DriveSecretsSerializerContext.Default.NodeOperationData, cancellationToken);
    }

    public ValueTask ClearAsync()
    {
        _memoryCache.Clear();

        return _repository?.ClearAsync() ?? ValueTask.CompletedTask;
    }

    private static string GetShareKeyCacheKey(ShareId shareId)
    {
        return $"share:{shareId}:key";
    }

    private static string GetNodeOperationDataCacheKey(NodeUid nodeId)
    {
        return $"node:{nodeId}";
    }

    private async ValueTask<DriveCacheAcquisition<T>> TryAcquireAsync<T>(
        string key,
        JsonTypeInfo<T> typeInfo,
        Func<T?, Option<T>> convertNullabilityToCacheHitOption,
        CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGet<T>(key, out var memoryCached))
        {
            return DriveCacheAcquisition<T>.ForValue(memoryCached);
        }

        if (_repository is not null)
        {
            var repositoryValueOrNone = await _repository.TryGetDeserializedValueAsync(
                key,
                typeInfo,
                convertNullabilityToCacheHitOption,
                cancellationToken).ConfigureAwait(false);

            if (repositoryValueOrNone.TryGetValue(out var repositoryValue))
            {
                _memoryCache.Set(key, repositoryValue);
                return DriveCacheAcquisition<T>.ForValue(repositoryValue);
            }
        }

        var acquisition = await _memoryCache.TryAcquireOrWaitAsync<T>(key, cancellationToken).ConfigureAwait(false);

        if (acquisition.TryGetValueElseClaim(out var value, out var innerClaim))
        {
            return DriveCacheAcquisition<T>.ForValue(value);
        }

        return DriveCacheAcquisition<T>.ForClaim(new DriveCacheEntryClaim<T>(innerClaim, (v, ct) => WriteToRepositoryAsync(key, v, typeInfo, ct)));
    }

    private ValueTask<T?> GetOrCreateNullableAsync<T>(
        string key,
        JsonTypeInfo<T?> typeInfo,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken)
    {
        return GetOrCreateAsync(key, typeInfo, factory, Option<T?>.Some, cancellationToken);
    }

    private ValueTask<T> GetOrCreateAsync<T>(
        string key,
        JsonTypeInfo<T> typeInfo,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken)
        where T : notnull
    {
        return GetOrCreateAsync(key, typeInfo, factory, Option<T>.FromNullable, cancellationToken);
    }

    private ValueTask<T> GetOrCreateAsync<T>(
        string key,
        JsonTypeInfo<T> typeInfo,
        Func<CancellationToken, ValueTask<T>> factory,
        Func<T?, Option<T>> convertToCacheHitOption,
        CancellationToken cancellationToken)
    {
        return _memoryCache.GetOrCreateAsync(
            key,
            async ct =>
            {
                if (_repository is not null)
                {
                    var repositoryValueOrNone = await _repository.TryGetDeserializedValueAsync(key, typeInfo, convertToCacheHitOption, ct)
                        .ConfigureAwait(false);

                    if (repositoryValueOrNone.TryGetValue(out var repositoryValue))
                    {
                        return repositoryValue;
                    }
                }

                var value = await factory.Invoke(ct).ConfigureAwait(false);

                await WriteToRepositoryAsync(key, value, typeInfo, ct).ConfigureAwait(false);

                return value;
            },
            cancellationToken);
    }

    private ValueTask SetAsync<T>(string key, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        _memoryCache.Set(key, value);

        return WriteToRepositoryAsync(key, value, typeInfo, cancellationToken);
    }

    private ValueTask WriteToRepositoryAsync<T>(string key, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        if (_repository is null)
        {
            return ValueTask.CompletedTask;
        }

        var serializedValue = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);

        return _repository.SetAsync(key, serializedValue, cancellationToken);
    }
}

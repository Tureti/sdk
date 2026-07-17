using System.Text.Json;
using Proton.Cryptography.Pgp;
using Proton.Drive.Sdk.Account.Addresses;
using Proton.Drive.Sdk.Account.Serialization;
using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Account.Caching;

internal sealed class AccountSecretCache(ICacheRepository repository) : IAccountSecretCache
{
    private const string UserKeysCacheKey = "user:current:keys";

    private readonly Lazy<Task<ICacheRepository>> _getCacheRepository = new(async () =>
    {
        // If this fails, the cache will remain unusable until the next app restart, which we can live with (for now?).
        await repository.EnsureValueFormatVersionAsync(AccountCacheValueFormat.Version, CancellationToken.None).ConfigureAwait(false);
        return repository;
    });

    public async ValueTask SetUserKeysAsync(IEnumerable<PgpPrivateKey> unlockedKeys, CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var serializedValue = JsonSerializer.SerializeToUtf8Bytes(unlockedKeys, SecretsSerializerContext.Default.IEnumerablePgpPrivateKey);

        await repo.SetAsync(UserKeysCacheKey, serializedValue, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<PgpPrivateKey>?> TryGetUserKeysAsync(CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var serializedValue = await repo.TryGetAsync(UserKeysCacheKey, cancellationToken).ConfigureAwait(false);

        return serializedValue is not null
            ? JsonSerializer.Deserialize(serializedValue, SecretsSerializerContext.Default.PgpPrivateKeyArray)
            : null;
    }

    public async ValueTask SetAddressKeysAsync(AddressId addressId, IEnumerable<PgpPrivateKey> unlockedKeys, CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var serializedValue = JsonSerializer.SerializeToUtf8Bytes(unlockedKeys, SecretsSerializerContext.Default.IEnumerablePgpPrivateKey);

        await repo.SetAsync(GetAddressKeysCacheKey(addressId), serializedValue, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<PgpPrivateKey>?> TryGetAddressKeysAsync(AddressId addressId, CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var serializedValue = await repo.TryGetAsync(GetAddressKeysCacheKey(addressId), cancellationToken).ConfigureAwait(false);

        return serializedValue is not null
            ? JsonSerializer.Deserialize(serializedValue, SecretsSerializerContext.Default.PgpPrivateKeyArray)
            : null;
    }

    private static string GetAddressKeysCacheKey(AddressId addressId)
    {
        return $"address:{addressId}:keys";
    }
}

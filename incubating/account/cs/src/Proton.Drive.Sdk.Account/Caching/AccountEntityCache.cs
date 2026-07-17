using System.Text.Json;
using Proton.Drive.Sdk.Account.Addresses;
using Proton.Drive.Sdk.Account.Serialization;
using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Account.Caching;

internal sealed class AccountEntityCache(ICacheRepository repository) : IAccountEntityCache
{
    private const string CurrentUserDefaultAddressIdCacheKey = "user:current:addresses:default:id";

    private readonly Lazy<Task<ICacheRepository>> _getCacheRepository = new(async () =>
    {
        // If this fails, the cache will remain unusable until the next app restart, which we can live with (for now?).
        await repository.EnsureValueFormatVersionAsync(AccountCacheValueFormat.Version, CancellationToken.None).ConfigureAwait(false);
        return repository;
    });

    public async ValueTask SetAddressAsync(Address address, CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var value = JsonSerializer.SerializeToUtf8Bytes(address, AccountEntitiesSerializerContext.Default.Address);

        await repo.SetAsync(GetAddressCacheKey(address.Id), value, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Address?> TryGetAddressAsync(AddressId addressId, CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var value = await repo.TryGetAsync(GetAddressCacheKey(addressId), cancellationToken).ConfigureAwait(false);

        return value is not null ? JsonSerializer.Deserialize(value, AccountEntitiesSerializerContext.Default.Address) : null;
    }

    public async ValueTask SetCurrentUserDefaultAddressIdAsync(AddressId addressId, CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        await repo.SetUtf8StringAsync(CurrentUserDefaultAddressIdCacheKey, addressId.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AddressId?> TryGetCurrentUserDefaultAddressIdAsync(CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var value = await repo.TryGetUtf8StringAsync(CurrentUserDefaultAddressIdCacheKey, cancellationToken).ConfigureAwait(false);

        return value is not null ? (AddressId)value : null;
    }

    private static string GetAddressCacheKey(AddressId addressId)
    {
        return $"address:{addressId}";
    }
}

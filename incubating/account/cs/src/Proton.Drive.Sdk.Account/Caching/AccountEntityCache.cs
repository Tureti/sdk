using System.Text.Json;
using Proton.Drive.Sdk.Account.Addresses;
using Proton.Drive.Sdk.Account.Serialization;
using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Account.Caching;

internal sealed class AccountEntityCache(ICacheRepository repository) : IAccountEntityCache
{
    private const string CurrentUserDefaultAddressIdCacheKey = "user:current:addresses:default:id";

    private readonly ICacheRepository _repository = repository;

    public ValueTask SetAddressAsync(Address address, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(address, AccountEntitiesSerializerContext.Default.Address);

        return _repository.SetAsync(GetAddressCacheKey(address.Id), value, cancellationToken);
    }

    public async ValueTask<Address?> TryGetAddressAsync(AddressId addressId, CancellationToken cancellationToken)
    {
        var value = await _repository.TryGetAsync(GetAddressCacheKey(addressId), cancellationToken).ConfigureAwait(false);

        return value is not null ? JsonSerializer.Deserialize(value, AccountEntitiesSerializerContext.Default.Address) : null;
    }

    public ValueTask SetCurrentUserDefaultAddressIdAsync(AddressId addressId, CancellationToken cancellationToken)
    {
        return _repository.SetAsync(CurrentUserDefaultAddressIdCacheKey, addressId.ToString(), cancellationToken);
    }

    public async ValueTask<AddressId?> TryGetCurrentUserDefaultAddressIdAsync(CancellationToken cancellationToken)
    {
        var value = await _repository.TryGetAsync(CurrentUserDefaultAddressIdCacheKey, cancellationToken).ConfigureAwait(false);

        return value is not null ? (AddressId)value : null;
    }

    private static string GetAddressCacheKey(AddressId addressId)
    {
        return $"address:{addressId}";
    }
}

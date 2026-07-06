using Proton.Drive.Sdk.Account.Addresses;

namespace Proton.Drive.Sdk.Account.Caching;

internal interface IAccountEntityCache
{
    ValueTask SetAddressAsync(Address address, CancellationToken cancellationToken);
    ValueTask<Address?> TryGetAddressAsync(AddressId addressId, CancellationToken cancellationToken);

    ValueTask SetCurrentUserDefaultAddressIdAsync(AddressId addressId, CancellationToken cancellationToken);
    ValueTask<AddressId?> TryGetCurrentUserDefaultAddressIdAsync(CancellationToken cancellationToken);
}

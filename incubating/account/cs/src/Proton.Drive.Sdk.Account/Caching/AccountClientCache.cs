using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Account.Caching;

internal sealed class AccountClientCache(ICacheRepository cacheRepository, ISessionSecretCache sessionSecretCache) : IAccountClientCache
{
    public IAccountEntityCache Entities { get; } = new AccountEntityCache(cacheRepository);
    public IAccountSecretCache Secrets { get; } = new AccountSecretCache(cacheRepository);
    public ISessionSecretCache SessionSecrets { get; } = sessionSecretCache;
    public IPublicKeyCache PublicKeys { get; } = new PublicKeyCache();
}

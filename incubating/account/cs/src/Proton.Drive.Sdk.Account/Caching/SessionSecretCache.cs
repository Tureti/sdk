using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Account.Caching;

internal sealed class SessionSecretCache(ICacheRepository repository) : ISessionSecretCache
{
    private readonly ICacheRepository _repository = repository;

    public ValueTask SetAccountKeyPassphraseAsync(string keyId, ReadOnlyMemory<byte> passphrase, CancellationToken cancellationToken)
    {
        var cacheKey = GetAccountPassphraseCacheKey(keyId);

        return _repository.SetAsync(cacheKey, passphrase, cancellationToken);
    }

    public async ValueTask<ReadOnlyMemory<byte>?> TryGetAccountKeyPassphraseAsync(string keyId, CancellationToken cancellationToken)
    {
        var cacheKey = GetAccountPassphraseCacheKey(keyId);

        return await _repository.TryGetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
    }

    private static string GetAccountPassphraseCacheKey(string keyId)
    {
        return $"account:passphrase:{keyId}";
    }
}

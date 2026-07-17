using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Account.Caching;

internal sealed class SessionSecretCache(ICacheRepository repository) : ISessionSecretCache
{
    private readonly Lazy<Task<ICacheRepository>> _getCacheRepository = new(async () =>
    {
        await repository.EnsureValueFormatVersionAsync(AccountCacheValueFormat.Version, CancellationToken.None).ConfigureAwait(false);
        return repository;
    });

    public async ValueTask SetAccountKeyPassphraseAsync(string keyId, ReadOnlyMemory<byte> passphrase, CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var cacheKey = GetAccountPassphraseCacheKey(keyId);

        await repo.SetAsync(cacheKey, passphrase, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ReadOnlyMemory<byte>?> TryGetAccountKeyPassphraseAsync(string keyId, CancellationToken cancellationToken)
    {
        var repo = await _getCacheRepository.Value.ConfigureAwait(false);

        var cacheKey = GetAccountPassphraseCacheKey(keyId);

        return await repo.TryGetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
    }

    private static string GetAccountPassphraseCacheKey(string keyId)
    {
        return $"account:passphrase:{keyId}";
    }
}

namespace Proton.Sdk.Caching;

public interface ICacheRepository : IAsyncDisposable
{
    ValueTask SetAsync(string key, string value, CancellationToken cancellationToken);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);

    ValueTask ClearAsync();

    ValueTask<string?> TryGetAsync(string key, CancellationToken cancellationToken);
}

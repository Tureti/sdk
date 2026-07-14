namespace Proton.Sdk.Caching;

public interface ICacheRepository : IAsyncDisposable
{
    ValueTask SetAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);

    ValueTask ClearAsync();

    ValueTask<byte[]?> TryGetAsync(string key, CancellationToken cancellationToken);
}

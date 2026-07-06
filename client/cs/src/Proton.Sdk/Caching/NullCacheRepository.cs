namespace Proton.Sdk.Caching;

internal sealed class NullCacheRepository : ICacheRepository
{
    public static readonly NullCacheRepository Instance = new();

    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(default(string?));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

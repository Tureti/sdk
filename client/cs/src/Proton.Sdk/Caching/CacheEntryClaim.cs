namespace Proton.Sdk.Caching;

/// <summary>
/// Grants exclusive responsibility for producing the value for the cache key that
/// <see cref="HybridMemoryCache.TryAcquireOrWaitAsync{T}"/> returned as a miss. Exactly one of <see cref="SetValue"/>,
/// <see cref="Fail"/>, or <see cref="Cancel"/> must eventually be called. Disposing without having done so calls
/// <see cref="Cancel"/>. All four members are idempotent: only the first call has an effect.
/// </summary>
public sealed class CacheEntryClaim<T> : ICacheEntryClaim, IDisposable
{
    private readonly HybridMemoryCache _cache;
    private readonly string _key;
    private readonly HybridMemoryCache.PendingLoad<T> _pendingLoad;
    private int _isResolved;

    internal CacheEntryClaim(HybridMemoryCache cache, string key, HybridMemoryCache.PendingLoad<T> pendingLoad)
    {
        _cache = cache;
        _key = key;
        _pendingLoad = pendingLoad;
    }

    /// <summary>
    /// Caches <paramref name="value"/> under the claimed key and completes every waiter with it.
    /// </summary>
    /// <remarks>
    /// Concurrent with an explicit <see cref="HybridMemoryCache.Set{T}"/> for the same key, completion follows
    /// last-write-wins semantics — see <see cref="HybridMemoryCache.Set{T}"/>.
    /// </remarks>
    public void SetValue(T value)
    {
        if (Interlocked.CompareExchange(ref _isResolved, 1, 0) != 0)
        {
            return;
        }

        _cache.CompletePendingLoad(_key, _pendingLoad, value);
    }

    /// <summary>
    /// Marks the load as failed and propagates <paramref name="exception"/> to every waiter.
    /// </summary>
    public void Fail(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _isResolved, 1, 0) != 0)
        {
            return;
        }

        _cache.FailPendingLoad(_key, _pendingLoad, exception);
    }

    /// <summary>
    /// Releases the claim without caching a value. Concurrent waiters retry coalescing instead of receiving an error.
    /// </summary>
    public void Cancel()
    {
        if (Interlocked.CompareExchange(ref _isResolved, 1, 0) != 0)
        {
            return;
        }

        _cache.CancelPendingLoad(_key, _pendingLoad);
    }

    /// <summary>
    /// Calls <see cref="Cancel"/> if neither <see cref="SetValue"/>, <see cref="Fail"/>, nor <see cref="Cancel"/> was
    /// already called.
    /// </summary>
    public void Dispose()
    {
        Cancel();
    }
}

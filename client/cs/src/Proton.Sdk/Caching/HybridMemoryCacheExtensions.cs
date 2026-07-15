namespace Proton.Sdk.Caching;

public static class HybridMemoryCacheExtensions
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or invokes <paramref name="factory"/> to produce and
    /// cache it on a miss. Concurrent misses share a single factory execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="factory"/> receives <paramref name="cancellationToken"/> and should honor it. When the
    /// claim holder cancels during factory execution, the claim is cancelled and concurrent waiters retry
    /// coalescing rather than receiving the cancellation.
    /// </para>
    /// <para>
    /// On genuine factory failure, every caller — including concurrent waiters — receives the same exception. A
    /// waiter that cancels while awaiting an in-flight load is unaffected on other callers: only its own wait ends.
    /// </para>
    /// </remarks>
    public static async ValueTask<T> GetOrCreateAsync<T>(
        this HybridMemoryCache cache,
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken)
    {
        var acquisition = await cache.TryAcquireOrWaitAsync<T>(key, cancellationToken).ConfigureAwait(false);

        if (acquisition.TryGetValueElseClaim(out var value, out var claim))
        {
            return value;
        }

        return await RunFactoryAsync(claim, factory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> RunFactoryAsync<T>(
        CacheEntryClaim<T> claim,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await factory(cancellationToken).ConfigureAwait(false);
            claim.SetValue(value);
            return value;
        }
        catch (Exception exception)
        {
            claim.CancelOrFail(exception, cancellationToken);
            throw;
        }
    }
}

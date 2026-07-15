using Proton.Sdk.Caching;

namespace Proton.Drive.Sdk.Caching;

/// <summary>
/// Grants exclusive responsibility for producing a Drive cache entry, completing the underlying L1 claim and
/// persisting to L2 when <see cref="CompleteAsync"/> is called.
/// </summary>
internal sealed class DriveCacheEntryClaim<T> : ICacheEntryClaim, IDisposable
{
    private readonly CacheEntryClaim<T> _inner;
    private readonly Func<T, CancellationToken, ValueTask> _persistAsync;
    private int _isResolved;

    internal DriveCacheEntryClaim(
        CacheEntryClaim<T> inner,
        Func<T, CancellationToken, ValueTask> persistAsync)
    {
        _inner = inner;
        _persistAsync = persistAsync;
    }

    /// <summary>
    /// Completes the L1 claim and persists <paramref name="value"/> to L2.
    /// </summary>
    public ValueTask CompleteAsync(T value, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _isResolved, 1, 0) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _inner.SetValue(value);

        return _persistAsync.Invoke(value, cancellationToken);
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

        _inner.Fail(exception);
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

        _inner.Cancel();
    }

    /// <summary>
    /// Calls <see cref="Cancel"/> if neither <see cref="CompleteAsync"/>, <see cref="Fail"/>, nor <see cref="Cancel"/>
    /// was already called.
    /// </summary>
    public void Dispose()
    {
        Cancel();
    }
}

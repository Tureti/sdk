namespace Proton.Sdk.Caching;

/// <summary>
/// A cache load claim that must eventually be resolved through exactly one of its completion methods.
/// </summary>
public interface ICacheEntryClaim
{
    /// <summary>
    /// Marks the load as failed and propagates <paramref name="exception"/> to every waiter.
    /// </summary>
    void Fail(Exception exception);

    /// <summary>
    /// Releases the claim without caching a value. Concurrent waiters retry coalescing instead of receiving an error.
    /// </summary>
    void Cancel();
}

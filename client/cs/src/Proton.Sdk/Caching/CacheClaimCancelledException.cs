namespace Proton.Sdk.Caching;

/// <summary>
/// Internal signal that a claim holder cancelled without completing the load. Waiters retry coalescing rather than
/// treating this as a load failure.
/// </summary>
public sealed class CacheClaimCancelledException : Exception
{
    public CacheClaimCancelledException()
        : base("The cache entry claim was cancelled.")
    {
    }

    public CacheClaimCancelledException(string? message)
        : base(message)
    {
    }

    public CacheClaimCancelledException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

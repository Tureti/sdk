using System.Diagnostics.CodeAnalysis;

namespace Proton.Sdk.Caching;

/// <summary>
/// The outcome of <see cref="HybridMemoryCache.TryAcquireOrWaitAsync{T}"/>: either the resolved value for the
/// requested key (a cache hit, or another caller's completed claim), or an exclusive
/// <see cref="CacheEntryClaim{T}"/> that the caller must resolve.
/// </summary>
public readonly struct CacheAcquisition<T>
{
    private readonly T? _value;
    private readonly CacheEntryClaim<T>? _claim;

    private CacheAcquisition(T? value, CacheEntryClaim<T>? claim)
    {
        _value = value;
        _claim = claim;
    }

    public static CacheAcquisition<T> ForValue(T value) => new(value, claim: null);

    public static CacheAcquisition<T> ForClaim(CacheEntryClaim<T> claim) => new(default, claim);

    /// <summary>
    /// Outputs the already-resolved value and returns <see langword="true"/> on a cache hit or completed wait;
    /// otherwise outputs the exclusive claim and returns <see langword="false"/>.
    /// </summary>
    public bool TryGetValueElseClaim([NotNullWhen(true)] out T value, [NotNullWhen(false)] out CacheEntryClaim<T>? claim)
    {
        claim = _claim;
        value = _value!;
        return claim is null;
    }
}

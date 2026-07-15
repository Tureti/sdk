using System.Diagnostics.CodeAnalysis;

namespace Proton.Drive.Sdk.Caching;

/// <summary>
/// The outcome of a Drive cache try-acquire: either the resolved value (L1/L2 hit or completed wait), or an
/// exclusive <see cref="DriveCacheEntryClaim{T}"/> that the caller must resolve.
/// </summary>
internal readonly struct DriveCacheAcquisition<T>
{
    private readonly T? _value;
    private readonly DriveCacheEntryClaim<T>? _claim;

    private DriveCacheAcquisition(T? value, DriveCacheEntryClaim<T>? claim)
    {
        _value = value;
        _claim = claim;
    }

    public static DriveCacheAcquisition<T> ForValue(T value) => new(value, claim: null);

    public static DriveCacheAcquisition<T> ForClaim(DriveCacheEntryClaim<T> claim) => new(default, claim);

    /// <summary>
    /// Outputs the already-resolved value and returns <see langword="true"/> on a cache hit or completed wait,
    /// otherwise outputs the exclusive claim and returns <see langword="false"/>.
    /// </summary>
    public bool TryGetValueElseClaim([NotNullWhen(true)] out T value, [NotNullWhen(false)] out DriveCacheEntryClaim<T>? claim)
    {
        claim = _claim;
        value = _value!;
        return claim is null;
    }
}

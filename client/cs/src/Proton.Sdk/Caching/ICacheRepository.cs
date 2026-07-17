namespace Proton.Sdk.Caching;

public interface ICacheRepository : IAsyncDisposable
{
    /// <summary>
    /// Ensures cached entry format compatibility. When the stored version differs, all entries are wiped.
    /// All consumers sharing one repository must pass the same <paramref name="valueFormatVersion"/> string.
    /// </summary>
    ValueTask EnsureValueFormatVersionAsync(string valueFormatVersion, CancellationToken cancellationToken);

    ValueTask SetAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);

    ValueTask ClearAsync();

    ValueTask<byte[]?> TryGetAsync(string key, CancellationToken cancellationToken);
}

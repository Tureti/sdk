using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Proton.Sdk.Caching;

public static class CacheRepositoryExtensions
{
    /// <summary>
    /// Deserializes a repository entry when present and maps it through <paramref name="convertToCacheHitOption"/>.
    /// Returns <see cref="Option{T}.None"/> when the key is missing, deserialization fails, or the converted value is not usable,
    /// otherwise returns <see cref="Option{T}.Some"/> with the usable value (including <see langword="null"/> when the converter allows it).
    /// </summary>
    public static async ValueTask<Option<T>> TryGetDeserializedValueAsync<T>(
        this ICacheRepository repository,
        string key,
        JsonTypeInfo<T> typeInfo,
        Func<T?, Option<T>> convertToCacheHitOption,
        CancellationToken cancellationToken)
    {
        var serializedValue = await repository.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (serializedValue is null)
        {
            return Option<T>.None;
        }

        try
        {
            var deserializedValue = JsonSerializer.Deserialize(serializedValue, typeInfo);
            return convertToCacheHitOption.Invoke(deserializedValue);
        }
        catch
        {
            await repository.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return Option<T>.None;
        }
    }

    public static ValueTask SetAsync(
        this ICacheRepository repository,
        string key,
        ReadOnlySpan<byte> value,
        CancellationToken cancellationToken)
    {
        return repository.SetAsync(key, value.ToArray(), cancellationToken);
    }

    public static ValueTask SetUtf8StringAsync(this ICacheRepository repository, string key, string value, CancellationToken cancellationToken)
    {
        return repository.SetAsync(key, Encoding.UTF8.GetBytes(value), cancellationToken);
    }

    public static async ValueTask<string?> TryGetUtf8StringAsync(this ICacheRepository repository, string key, CancellationToken cancellationToken)
    {
        var value = await repository.TryGetAsync(key, cancellationToken).ConfigureAwait(false);

        return value is not null ? Encoding.UTF8.GetString(value) : null;
    }
}

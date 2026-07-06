using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Proton.Sdk.Caching;

public static class CacheRepositoryExtensions
{
    public static async ValueTask<(bool Exists, T? Value)> TryGetDeserializedValueAsync<T>(
        this ICacheRepository repository,
        string key,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var serializedValue = await repository.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (serializedValue is null)
        {
            return default;
        }

        try
        {
            return (true, JsonSerializer.Deserialize(serializedValue, typeInfo));
        }
        catch
        {
            await repository.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return default;
        }
    }
}

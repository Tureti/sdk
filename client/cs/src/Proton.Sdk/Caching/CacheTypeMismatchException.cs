namespace Proton.Sdk.Caching;

/// <summary>
/// Thrown when a cache key is accessed with a different generic type argument than it was bound to.
/// </summary>
public sealed class CacheTypeMismatchException : InvalidOperationException
{
    public CacheTypeMismatchException()
    {
    }

    public CacheTypeMismatchException(string? message)
        : base(message)
    {
    }

    public CacheTypeMismatchException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public CacheTypeMismatchException(string key, Type requestedType, Type? actualType)
        : base(FormatMessage(key, requestedType, actualType))
    {
        Key = key;
        RequestedType = requestedType;
        ActualType = actualType;
    }

    public string Key { get; } = string.Empty;

    public Type RequestedType { get; } = typeof(object);

    /// <summary>
    /// The runtime type the key was bound to, or <see langword="null"/> when the stored value is <see langword="null"/>.
    /// </summary>
    public Type? ActualType { get; }

    private static string FormatMessage(string key, Type requestedType, Type? actualType) =>
        actualType is null
            ? $"Cache key '{key}' holds null, but {requestedType.Name} was requested."
            : $"Cache key '{key}' is bound to {actualType.Name}, but {requestedType.Name} was requested.";
}

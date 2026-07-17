namespace Proton.Drive.Sdk.Account.Caching;

/// <summary>
/// Shared value format version for all account cache classes backed by the same <see cref="Proton.Sdk.Caching.ICacheRepository"/>.
/// </summary>
internal static class AccountCacheValueFormat
{
    public static readonly string Version = $"obj:{ObjectSchemaVersion}.ser:{SerializationVersion}";

    private const int ObjectSchemaVersion = 1;
    private const int SerializationVersion = 1;
}

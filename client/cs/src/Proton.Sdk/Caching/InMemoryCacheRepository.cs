using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Proton.Sdk.Caching;

public sealed class InMemoryCacheRepository : ICacheRepository, IDisposable
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new();
    private readonly ReaderWriterLockSlim _lock = new();

    public ValueTask EnsureValueFormatVersionAsync(string valueFormatVersion, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    ValueTask ICacheRepository.SetAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        Set(key, value);

        return ValueTask.CompletedTask;
    }

    ValueTask<byte[]?> ICacheRepository.TryGetAsync(string key, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(TryGet(key, out var value) ? value : null);
    }

    ValueTask ICacheRepository.RemoveAsync(string key, CancellationToken cancellationToken)
    {
        Remove(key);

        return ValueTask.CompletedTask;
    }

    ValueTask ICacheRepository.ClearAsync()
    {
        Clear();

        return ValueTask.CompletedTask;
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Set(string key, ReadOnlyMemory<byte> value)
    {
        _lock.EnterWriteLock();
        try
        {
            _entries[key] = value.ToArray();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool TryGet(string key, [MaybeNullWhen(false)] out byte[] value)
    {
        return _entries.TryGetValue(key, out value);
    }

    public void Remove(string key)
    {
        _lock.EnterWriteLock();
        try
        {
            _entries.TryRemove(key, out _);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _entries.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}

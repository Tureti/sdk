using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Proton.Sdk.Caching;

/// <summary>
/// A bounded, thread-safe, in-memory cache that stores live object instances rather than serialized values.
/// When the entry limit is reached, a configurable fraction of the coldest entries is evicted based on a
/// score that blends access frequency (LFU) and recency (LRU). Concurrent misses for the same key are
/// coalesced ("single-flight"): only one caller executes the factory, the others await its result.
/// </summary>
/// <remarks>
/// Each string key is bound to a single CLR type for the lifetime of the cache. Using the same key with
/// different generic type arguments throws <see cref="CacheTypeMismatchException"/>.
/// </remarks>
public sealed class HybridMemoryCache
{
    private const int MaxFrequency = 20;
    private const double DefaultMemoryEvictionRatio = 0.25;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, object> _inFlightLoads = new();
    private readonly int _maxEntries;
    private readonly double _evictionRatio;

    private long _accessSequence;
    private int _isTrimming;

    public HybridMemoryCache(int maxEntries, double evictionRatio = DefaultMemoryEvictionRatio)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxEntries, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(evictionRatio, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(evictionRatio, 1.0);

        _maxEntries = maxEntries;
        _evictionRatio = evictionRatio;
    }

    internal int Count => _entries.Count;

    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.Value is T typed)
            {
                entry.RecordAccess(Interlocked.Increment(ref _accessSequence), MaxFrequency);
                value = typed;
                return true;
            }

            if (entry.Value is null && default(T) is null)
            {
                entry.RecordAccess(Interlocked.Increment(ref _accessSequence), MaxFrequency);
                value = default!;
                return true;
            }

            throw new CacheTypeMismatchException(key, typeof(T), entry.Value?.GetType());
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Writes <paramref name="value"/> into the cache, replacing any existing entry for <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Concurrent with an in-flight claim or factory for the same key, writes follow last-write-wins semantics:
    /// whichever call to <see cref="Set{T}"/> or claim completion runs last determines the stored value. There is
    /// no freshness ordering between explicit writes and in-flight loads.
    /// </para>
    /// <para>
    /// A future improvement may route all writes through claims (try-acquire, complete-or-wait) to eliminate this
    /// race. That would remove direct <see cref="Set{T}"/> in favour of a single mutation path.
    /// </para>
    /// </remarks>
    public void Set<T>(string key, T value)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Value is not null and not T)
        {
            throw new CacheTypeMismatchException(key, typeof(T), entry.Value.GetType());
        }

        _entries[key] = new Entry(value, Interlocked.Increment(ref _accessSequence));

        TrimIfNeeded();
    }

    public void Remove(string key)
    {
        _entries.TryRemove(key, out _);
    }

    public void Clear()
    {
        _entries.Clear();
    }

    /// <summary>
    /// Looks up <paramref name="key"/>, or on a miss hands the caller an exclusive claim or awaits another caller's
    /// in-flight load for the same key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the primitive <see cref="HybridMemoryCacheExtensions.GetOrCreateAsync"/> is built on. Use it when the
    /// value cannot be produced through a single factory call — for example when several related keys must be
    /// resolved as part of one unit of work.
    /// </para>
    /// <para>
    /// Whoever receives the claim must eventually call <see cref="CacheEntryClaim{T}.SetValue"/>,
    /// <see cref="CacheEntryClaim{T}.Fail"/>, or <see cref="CacheEntryClaim{T}.Cancel"/> (disposing without doing so
    /// calls <see cref="CacheEntryClaim{T}.Cancel"/>). Failing to resolve a claim leaves concurrent waiters stuck
    /// forever. <see cref="CacheEntryClaim{T}.Cancel"/> causes waiters to retry; <see cref="CacheEntryClaim{T}.Fail"/>
    /// propagates the failure to every waiter.
    /// </para>
    /// </remarks>
    public async ValueTask<CacheAcquisition<T>> TryAcquireOrWaitAsync<T>(string key, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryGet<T>(key, out var cachedValue))
            {
                return CacheAcquisition<T>.ForValue(cachedValue);
            }

            var inFlightLoad = _inFlightLoads.GetOrAdd(key, static _ => new PendingLoad<T>());

            if (inFlightLoad is not PendingLoad<T> pendingLoad)
            {
                throw new CacheTypeMismatchException(key, typeof(T), inFlightLoad.GetType());
            }

            if (pendingLoad.TryClaim())
            {
                return CacheAcquisition<T>.ForClaim(new CacheEntryClaim<T>(this, key, pendingLoad));
            }

            try
            {
                return await AwaitPendingLoadAsync(pendingLoad, cancellationToken).ConfigureAwait(false);
            }
            catch (CacheClaimCancelledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Claim holder cancelled; retry coalescing on a fresh pending load.
            }
        }
    }

    /// <summary>
    /// Completes the in-flight load for <paramref name="key"/> and unblocks every waiter.
    /// </summary>
    /// <remarks>
    /// Writes <paramref name="value"/> via <see cref="Set{T}"/> and therefore follows the same last-write-wins
    /// semantics documented there when concurrent with an explicit <see cref="Set{T}"/> for the same key.
    /// </remarks>
    internal void CompletePendingLoad<T>(string key, PendingLoad<T> pendingLoad, T value)
    {
        Set(key, value);
        RemovePendingLoad(key, pendingLoad);
        pendingLoad.TryComplete(value);
    }

    internal void FailPendingLoad<T>(string key, PendingLoad<T> pendingLoad, Exception exception)
    {
        RemovePendingLoad(key, pendingLoad);
        pendingLoad.TryFail(exception);
    }

    internal void CancelPendingLoad<T>(string key, PendingLoad<T> pendingLoad)
    {
        RemovePendingLoad(key, pendingLoad);
        pendingLoad.TryCancel();
    }

    private static async ValueTask<CacheAcquisition<T>> AwaitPendingLoadAsync<T>(PendingLoad<T> pendingLoad, CancellationToken cancellationToken)
    {
        var value = await pendingLoad.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        return CacheAcquisition<T>.ForValue(value);
    }

    private void RemovePendingLoad<T>(string key, PendingLoad<T> pendingLoad)
    {
        ((ICollection<KeyValuePair<string, object>>)_inFlightLoads).Remove(new KeyValuePair<string, object>(key, pendingLoad));
    }

    private void TrimIfNeeded()
    {
        if (_entries.Count <= _maxEntries || Interlocked.CompareExchange(ref _isTrimming, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var overflow = _entries.Count - _maxEntries;
            if (overflow <= 0)
            {
                return;
            }

            var evictionCount = Math.Max(overflow, (int)Math.Ceiling(_maxEntries * _evictionRatio));
            var currentSequence = Interlocked.Read(ref _accessSequence);

            var coldestKeys = _entries
                .Select(pair => (pair.Key, Score: pair.Value.ComputeDecayScore(currentSequence)))
                .OrderBy(pair => pair.Score)
                .Take(evictionCount)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var key in coldestKeys)
            {
                _entries.TryRemove(key, out _);
            }
        }
        finally
        {
            Volatile.Write(ref _isTrimming, 0);
        }
    }

    internal sealed class PendingLoad<T>
    {
        private readonly TaskCompletionSource<T> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _isClaimed;

        public Task<T> Task => _completionSource.Task;

        /// <summary>
        /// Returns <see langword="true"/> the first time it is called on a given instance, and <see langword="false"/>
        /// on every subsequent call (including concurrent ones). Since a single instance can be raced onto the
        /// dictionary by <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})"/>'s
        /// factory before only one survives, this - rather than reference-comparing against a locally created
        /// candidate - is what determines which single caller becomes the claim holder for it.
        /// </summary>
        public bool TryClaim() => Interlocked.CompareExchange(ref _isClaimed, 1, 0) == 0;

        public void TryComplete(T value) => _completionSource.TrySetResult(value);

        public void TryFail(Exception exception) => _completionSource.TrySetException(exception);

        public void TryCancel() => _completionSource.TrySetException(new CacheClaimCancelledException());
    }

    private sealed class Entry(object? value, long sequence)
    {
        private long _lastAccessSequence = sequence;
        private int _frequencyCount = 1;

        public object? Value { get; } = value;

        public void RecordAccess(long newAccessSequence, int maxFrequency)
        {
            Interlocked.Exchange(ref _lastAccessSequence, newAccessSequence);

            int currentFrequency;
            do
            {
                currentFrequency = Volatile.Read(ref _frequencyCount);
                if (currentFrequency >= maxFrequency)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _frequencyCount, currentFrequency + 1, currentFrequency) != currentFrequency);
        }

        public double ComputeDecayScore(long currentSequence)
        {
            var ticksSinceLastAccess = currentSequence - Interlocked.Read(ref _lastAccessSequence) + 1;

            return Volatile.Read(ref _frequencyCount) / (double)ticksSinceLastAccess;
        }
    }
}

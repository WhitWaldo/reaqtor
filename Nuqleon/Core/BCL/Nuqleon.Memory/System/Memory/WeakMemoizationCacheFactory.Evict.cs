﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information.

//
// Revision history:
//
//   BD - 07/29/2015 - Initial work on memoization support.
//

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Time;

#if DEBUG
using System.Diagnostics;
#endif

namespace System.Memory
{
    public partial class WeakMemoizationCacheFactory
    {
        /// <summary>
        /// Implementation of a factory for weak memoization caches with a ranking-based eviction strategy.
        /// </summary>
        /// <typeparam name="TMetric">Type of the metric to rank cache entries by.</typeparam>
        private sealed class EvictImpl<TMetric> : IWeakMemoizationCacheFactory
        {
            private readonly Func<IMemoizationCacheEntryMetrics, TMetric> _ranker;
            private readonly int _maxCapacity;
            private readonly bool _descending;
            private readonly double _ageThreshold;
            private readonly IStopwatchFactory _stopwatchFactory;

            /// <summary>
            /// Creates a memoization cache factory for weak memoization caches that use an eviction strategy based on a function to rank cache entries based on metrics.
            /// </summary>
            /// <param name="ranker">The ranker function used to obtain the metric for each entry upon evicting entries from the cache.</param>
            /// <param name="maxCapacity">The maximum capacity of memoization caches returned by the factory.</param>
            /// <param name="ageThreshold">The threshold used to decide whether an entry has aged sufficiently in order to be considered for eviction. E.g. a value of 0.9 means that the youngest 10% of entries cannot get evicted.</param>
            /// <param name="descending">Indicates whether the ranker should evict entries with the highest or lowest score.</param>
            /// <param name="stopwatchFactory">The stopwatch factory used to create stopwatches that measure access times and function invocation times. If omitted, the default stopwatch is used.</param>
            /// <returns>Memoization cache factory for memoization caches that use a ranking-based cache eviction strategy.</returns>
            public EvictImpl(Func<IMemoizationCacheEntryMetrics, TMetric> ranker, int maxCapacity, double ageThreshold, IStopwatchFactory stopwatchFactory, bool descending)
            {
                _ranker = ranker;
                _maxCapacity = maxCapacity;
                _descending = descending;
                _ageThreshold = ageThreshold;
                _stopwatchFactory = stopwatchFactory ?? StopwatchFactory.Diagnostics;
            }

            /// <summary>
            /// Creates a memoization cache for the specified <paramref name="function"/> that doesn't keep cache entry keys alive.
            /// </summary>
            /// <typeparam name="T">Type of the memoization cache entry keys. This type has to be a reference type.</typeparam>
            /// <typeparam name="R">Type of the memoization cache entry values.</typeparam>
            /// <param name="function">The function to memoize.</param>
            /// <param name="options">Flags to influence the memoization behavior.</param>
            /// <returns>An empty memoization cache instance.</returns>
            public IMemoizationCache<T, R> Create<T, R>(Func<T, R> function, MemoizationOptions options) where T : class
            {
                if (function == null)
                    throw new ArgumentNullException(nameof(function));

                var cacheError = (options & MemoizationOptions.CacheException) > MemoizationOptions.None;
                return new Cache<T, R>(function, _ranker, _maxCapacity, _descending, _ageThreshold, cacheError, _stopwatchFactory);
            }

            private sealed class Cache<T, R> : MemoizationCacheBase<T, R>, IServiceProvider
                where T : class
            {
                private readonly ConditionalWeakTable<T, IMetricsCacheEntry<WeakReference<T>, R>>.CreateValueCallback _function;
                private readonly IWeakCacheDictionary<T, IMetricsCacheEntry<WeakReference<T>, R>> _cache;
#pragma warning disable CA2213
                private readonly ReaderWriterLockSlim _lock;
#pragma warning restore CA2213
                private readonly HashSet<IMetricsCacheEntry<WeakReference<T>, R>> _entries;
                private readonly IStopwatch _stopwatch;
                private readonly IEnumerable<IMetricsCacheEntry<WeakReference<T>, R>> _ranker;
                private readonly int _maxCapacity;
                private readonly bool _cacheError;
#if DEBUG
                private int _invocationCount;
                private int _accessCount;
                private int _evictionCount;
                private int _trimCount;
                private TimeSpan _trimElapsed;
                private IMetricsCacheEntry<WeakReference<T>, R> _lastEvicted;
#endif
                public Cache(Func<T, R> function, Func<IMemoizationCacheEntryMetrics, TMetric> ranker, int maxCapacity, bool descending, double ageThreshold, bool cacheError, IStopwatchFactory stopwatchFactory)
                {
                    _function = args =>
                    {
                        var weakArgs = WeakReferenceExtensions.Create(args);

                        var invokeDuration = default(TimeSpan);
#if DEBUG
                        Interlocked.Increment(ref _invocationCount);
#endif
                        Trim();

                        var res = default(IMetricsCacheEntry<WeakReference<T>, R>);
                        try
                        {
                            var swInvokeStart = _stopwatch.ElapsedTicks;

                            var value = function(args);

                            invokeDuration = new TimeSpan(_stopwatch.ElapsedTicks - swInvokeStart);

                            res = new MetricsValueCacheEntry<WeakReference<T>, R>(weakArgs, value);
                        }
                        catch (Exception ex) when (_cacheError)
                        {
                            res = new MetricsErrorCacheEntry<WeakReference<T>, R>(weakArgs, ex);
                        }

                        res.CreationTime = new TimeSpan(_stopwatch.ElapsedTicks);
                        res.InvokeDuration = invokeDuration;

                        _lock.EnterWriteLock();
                        try
                        {
                            _entries.Add(res);
                        }
                        finally
                        {
                            _lock.ExitWriteLock();
                        }

                        return res;
                    };

                    _cache = new WeakCacheDictionary<T, IMetricsCacheEntry<WeakReference<T>, R>>();
                    _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
                    _entries = new HashSet<IMetricsCacheEntry<WeakReference<T>, R>>();
                    _stopwatch = stopwatchFactory.StartNew();

                    //
                    // Exclude newest items which have statically irrelevant data, so they get a chance to become relevant.
                    //
                    var candidates = _entries.OrderBy(e => _stopwatch.ElapsedTicks - e.CreationTime.Ticks).Take(Math.Max(1, (int)(maxCapacity * ageThreshold)));
                    _ranker = descending ? candidates.OrderByDescending(e => ranker(e)) : candidates.OrderBy(e => ranker(e));

                    _maxCapacity = maxCapacity;
                    _cacheError = cacheError;
                }

                protected override int CountCore => _entries.Count;

                [ExcludeFromCodeCoverage]
                protected override string DebugViewCore
                {
                    get
                    {
                        var sb = new StringBuilder();

                        sb.AppendLine("Entries");
                        sb.AppendLine("-------");
                        sb.AppendLine();

                        if (_entries.Count == 0)
                        {
                            sb.AppendLine("  (empty)");
                        }
                        else
                        {
                            foreach (var node in _entries)
                            {
                                if (node.Key.TryGetTarget(out T key))
                                {
                                    var value = node is ErrorCacheEntry<T, R> err ? err.Exception : (object)node.Value;
                                    sb.AppendFormat(CultureInfo.InvariantCulture, "  {0} -> {1}", key, value);
                                    sb.AppendLine();
#if DEBUG
                                    node.ToDebugView(sb, "    ");
                                    sb.AppendLine();
#endif
                                }
                                else
                                {
                                    sb.AppendLine("  (empty slot)");
                                }
                            }

#if DEBUG
                            //
                            // CONSIDER: add statistical information about invoke / hit / access / speedup
                            //
                            sb.AppendLine("Summary");
                            sb.AppendLine("-------");
                            sb.AppendLine();
                            sb.AppendLine("  Invocation count = " + _invocationCount);
                            sb.AppendLine("  Access count     = " + _accessCount);
                            sb.AppendLine("  Trim count       = " + _trimCount);
                            sb.AppendLine("  Trim elapsed     = " + _trimElapsed);
                            sb.AppendLine("  Eviction count   = " + _evictionCount);

                            if (_lastEvicted != null)
                            {
                                sb.AppendLine();
                                sb.AppendLine("  Last eviction");
                                _lastEvicted.ToDebugView(sb, "    ");
                            }

                            sb.AppendLine();
#endif
                        }

                        return sb.ToString();
                    }
                }

                protected override R GetOrAddCore(T args)
                {
                    var entry = default(IMetricsCacheEntry<WeakReference<T>, R>);

                    _lock.EnterUpgradeableReadLock();
                    try
                    {
#if DEBUG
                        Interlocked.Increment(ref _accessCount);
#endif
                        var swTotalStart = _stopwatch.ElapsedTicks;

                        //
                        // NB: CWT does not call the function under its internal lock.
                        //
                        entry = _cache.GetOrAdd(args, _function);
#if DEBUG
                        var keys = _cache.Keys;
                        Debug.Assert(_entries.Count == keys.Count);
#endif
                        //
                        // NB: Calculating stats outside the lock to keep lock duration as short as possible.
                        //
                        var duration = new TimeSpan(_stopwatch.ElapsedTicks - swTotalStart);
                        var accessTime = _stopwatch.Elapsed;

                        lock (entry) // TODO: review; if access per entry is high, we may be better off without the lock and only guarantee approximate values
                        {
                            entry.HitCount++;
                            entry.TotalDuration += duration;
                            entry.LastAccessTime = accessTime;
                        }
                    }
                    finally
                    {
                        _lock.ExitUpgradeableReadLock();
                    }

                    return entry.Value;
                }

                private void Trim()
                {
                    //
                    // NB: We can have temporary oversubscription during concurrent accesses because we avoid to enter the write lock
                    //     until absolutely necessary, so _entries.Count can be a dirty read.
                    //
                    if (_entries.Count >= _maxCapacity)
                    {
#if DEBUG
                        var trimStart = _stopwatch.ElapsedTicks;
#endif
                        _lock.EnterWriteLock();
                        try
                        {
                            using (var evictionOrder = _ranker.GetEnumerator())
                            {
                                while (_entries.Count >= _maxCapacity && evictionOrder.MoveNext())
                                {
                                    var entry = evictionOrder.Current;
#if DEBUG
                                    _lastEvicted = entry;
#endif
                                    if (entry.Key.TryGetTarget(out T key))
                                    {
                                        _cache.Remove(key);
                                    }

                                    _entries.Remove(entry);
#if DEBUG
                                    _evictionCount++;
#endif
                                }
                            }

                            _entries.RemoveWhere(e => !e.Key.TryGetTarget(out _));
                        }
                        finally
                        {
                            _lock.ExitWriteLock();
                        }
#if DEBUG
                        var trimElapsed = new TimeSpan(_stopwatch.ElapsedTicks - trimStart);
                        _trimCount++;
                        _trimElapsed += trimElapsed;
#endif
                    }
                }

                protected override void ClearCore(bool disposing)
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        foreach (var entry in _entries)
                        {
                            if (entry.Key.TryGetTarget(out T key))
                            {
                                _cache.Remove(key);
                            }
                        }

                        _entries.Clear();
#if DEBUG
                        _invocationCount = 0;
                        _accessCount = 0;
                        _evictionCount = 0;
                        _trimCount = 0;
                        _trimElapsed = TimeSpan.Zero;
                        _lastEvicted = null;
#endif
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }

                protected override void DisposeCore()
                {
                    //
                    // NB: This can fail if the cache is in use; notice that the base class does not set
                    //     the disposed flag until DisposeCore has successfully returned, so the caller
                    //     can retry the operation at a later time.
                    //
                    _lock.Dispose();
                }

                public object GetService(Type serviceType)
                {
                    var res = default(object);

                    if (serviceType == typeof(ITrimmable<KeyValuePair<T, R>>))
                    {
                        res = Trimmable.Create<KeyValuePair<T, R>>(shouldTrim => TrimBy(kv => kv.Value.Kind == ValueOrErrorKind.Value, kv => new KeyValuePair<T, R>(kv.Key, kv.Value.Value), shouldTrim));
                    }
                    else if (serviceType == typeof(ITrimmable<KeyValuePair<T, IValueOrError<R>>>) && _cacheError)
                    {
                        res = Trimmable.Create<KeyValuePair<T, IValueOrError<R>>>(shouldTrim => TrimBy(kv => true, kv => new KeyValuePair<T, IValueOrError<R>>(kv.Key, kv.Value), shouldTrim));
                    }
                    else if (serviceType == typeof(ITrimmable<IMemoizationCacheEntryMetrics>))
                    {
                        res = Trimmable.Create<IMemoizationCacheEntryMetrics>(shouldTrim => TrimBy(kv => true, kv => kv.Value, shouldTrim));
                    }

                    //
                    // NB: No trim by key or value; those types could unify to the same ITrimmable<>.
                    //     The drawback is that users of N-ary function need to use Args<> types.
                    //

                    return res;
                }

                private int TrimBy<K>(Func<KeyValuePair<T, IMetricsCacheEntry<WeakReference<T>, R>>, bool> filter, Func<KeyValuePair<T, IMetricsCacheEntry<WeakReference<T>, R>>, K> selector, Func<K, bool> shouldTrim)
                {
                    var keys = new HashSet<T>();
                    var entries = new HashSet<IMetricsCacheEntry<WeakReference<T>, R>>();

                    _lock.EnterWriteLock();
                    try
                    {
                        foreach (var entry in _entries)
                        {
                            if (entry.Key.TryGetTarget(out T key))
                            {
                                var kv = new KeyValuePair<T, IMetricsCacheEntry<WeakReference<T>, R>>(key, entry);

                                if (filter(kv) && shouldTrim(selector(kv)))
                                {
                                    keys.Add(key);
                                    entries.Add(entry);
                                }
                            }
                            else
                            {
                                entries.Add(entry);
                            }
                        }

                        _entries.ExceptWith(entries);

                        foreach (var key in keys)
                        {
                            _cache.Remove(key);
                        }
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }

                    return entries.Count;
                }
            }
        }
    }
}

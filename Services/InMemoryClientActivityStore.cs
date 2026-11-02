using System.Collections.Concurrent;
using knkwebapi_v2.Models.ClientActivity;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services;

/// <summary>
/// Thread-safe in-memory implementation of client activity tracking.
/// Maintains a rolling 60-minute window of request metrics per client.
/// </summary>
public class InMemoryClientActivityStore : IClientActivityStore
{
    private class ClientActivityEntry
    {
        /// <summary>The client's metadata.</summary>
        public ClientInfo Client { get; set; } = new();

        /// <summary>Last request information.</summary>
        public RequestInfo? LastRequest { get; set; }

        /// <summary>When this client was last seen.</summary>
        public DateTime LastSeenUtc { get; set; }

        /// <summary>Rolling 60-minute window buckets (one per minute).</summary>
        public RollingWindowBucket?[] Buckets { get; set; } = new RollingWindowBucket?[60];

        /// <summary>Lock for this entry (per-entry locking to avoid hotspot).</summary>
        public readonly object Lock = new();
    }

    /// <summary>Dictionary mapping clientKey -> ClientActivityEntry.</summary>
    private readonly ConcurrentDictionary<string, ClientActivityEntry> _clients = new();

    /// <summary>
    /// Maximum number of clients to track. Cleanup runs if exceeded.
    /// Can be set via constructor for testing/configuration.
    /// </summary>
    private readonly int _maxClients;

    /// <summary>
    /// Cleanup interval: how often to check and remove inactive clients.
    /// </summary>
    private readonly TimeSpan _cleanupInterval;

    /// <summary>Timestamp of last cleanup operation.</summary>
    private DateTime _lastCleanupUtc;

    private readonly TimeProvider _time;

    public InMemoryClientActivityStore(int maxClients = 1000, TimeSpan? cleanupInterval = null, TimeProvider? time = null)
    {
        _maxClients = maxClients;
        _cleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(10);
        _time = time ?? TimeProvider.System;
        _lastCleanupUtc = UtcNow;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public void RecordRequest(ClientInfo client, RequestInfo request)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        if (request == null) throw new ArgumentNullException(nameof(request));

        var key = client.GetKey();
        
        // Trigger cleanup if needed.
        TriggerCleanupIfNeeded();

        // One bucket update per request (PandiO/knk-workspace#99: the old AddOrUpdate path recorded every request
        // after a client's first twice). A slot is reused only within its own minute; an hour later the same slot
        // starts a new bucket instead of adding to the old one.
        var now = UtcNow;
        var entry = _clients.GetOrAdd(key, _ => new ClientActivityEntry { Client = client, LastSeenUtc = now });
        lock (entry.Lock)
        {
            entry.LastRequest = request;
            entry.LastSeenUtc = now;

            var minuteIndex = now.Minute;
            var minuteStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
            var bucket = entry.Buckets[minuteIndex];
            if (bucket == null || bucket.BucketStartUtc != minuteStart)
            {
                bucket = new RollingWindowBucket { MinuteIndex = minuteIndex, BucketStartUtc = minuteStart };
                entry.Buckets[minuteIndex] = bucket;
            }

            bucket.RecordRequest(request);
        }
    }

    public IReadOnlyList<ClientSnapshot> GetClients(TimeSpan activeWithin)
    {
        var threshold = UtcNow.Subtract(activeWithin);
        var results = new List<ClientSnapshot>();

        foreach (var kvp in _clients)
        {
            var entry = kvp.Value;
            lock (entry.Lock)
            {
                if (entry.LastSeenUtc >= threshold)
                {
                    results.Add(SnapshotFromEntry(entry));
                }
            }
        }

        // Return sorted by most recently seen first.
        return results.OrderByDescending(s => s.LastSeenUtc).ToList();
    }

    public ClientSnapshot? GetClient(string clientType, string clientId)
    {
        var key = $"{clientType}:{clientId}";
        if (_clients.TryGetValue(key, out var entry))
        {
            lock (entry.Lock)
            {
                return SnapshotFromEntry(entry);
            }
        }
        return null;
    }

    public IReadOnlyList<ClientSnapshot> GetAllClients()
    {
        var results = new List<ClientSnapshot>();
        foreach (var kvp in _clients)
        {
            var entry = kvp.Value;
            lock (entry.Lock)
            {
                results.Add(SnapshotFromEntry(entry));
            }
        }
        return results.OrderByDescending(s => s.LastSeenUtc).ToList();
    }

    public int Cleanup(TimeSpan inactiveOlderThan)
    {
        var threshold = UtcNow.Subtract(inactiveOlderThan);
        var keysToRemove = new List<string>();

        foreach (var kvp in _clients)
        {
            var entry = kvp.Value;
            lock (entry.Lock)
            {
                if (entry.LastSeenUtc < threshold)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            _clients.TryRemove(key, out _);
        }

        return keysToRemove.Count;
    }

    /// <summary>
    /// Triggers cleanup if the interval has elapsed and we're over max clients.
    /// </summary>
    private void TriggerCleanupIfNeeded()
    {
        var now = UtcNow;
        if (now - _lastCleanupUtc > _cleanupInterval && _clients.Count > _maxClients)
        {
            _lastCleanupUtc = now;
            // Remove clients inactive for more than 24 hours.
            Cleanup(TimeSpan.FromHours(24));
        }
    }

    /// <summary>
    /// Creates a snapshot from an entry. Assumes caller holds the lock. Buckets from before the last 60 minutes
    /// (a client that went quiet) are left out, so the Last60Min totals cover the last 60 minutes only.
    /// </summary>
    private ClientSnapshot SnapshotFromEntry(ClientActivityEntry entry)
    {
        var snapshot = new ClientSnapshot
        {
            Client = entry.Client,
            LastSeenUtc = entry.LastSeenUtc,
            LastRequest = entry.LastRequest,
            BucketsLast60Minutes = new RollingWindowBucket?[60],
        };

        var windowStart = UtcNow.AddMinutes(-60);
        for (var i = 0; i < 60; i++)
        {
            var bucket = entry.Buckets[i];
            if (bucket != null && bucket.BucketStartUtc > windowStart)
            {
                snapshot.BucketsLast60Minutes[i] = bucket;
            }
        }

        return snapshot;
    }
}

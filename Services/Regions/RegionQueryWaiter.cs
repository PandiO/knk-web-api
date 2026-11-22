using System.Collections.Concurrent;
using knkwebapi_v2.Realtime.Contracts;

namespace knkwebapi_v2.Services.Regions;

/// <summary>
/// PandiO/knk-workspace#988: the region queries waiting for their answer, as <see cref="Map.GateCommandWaiter"/> does for
/// gate commands. Singleton per answer type: the client registers a query id before it publishes the query, the game hub
/// completes it. The first answer for an id wins; an answer for an unknown or finished id is ignored (a late answer
/// after the timeout, or a second game server).
/// </summary>
public sealed class RegionQueryWaiter<TAnswer> where TAnswer : class, IRegionQueryAnswer
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TAnswer>> _pending = new(StringComparer.Ordinal);

    /// <summary>How many queries wait now (tests, logs).</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Starts waiting for <paramref name="queryId"/>; dispose the handle when done (removes the entry).</summary>
    public Pending Register(string queryId)
    {
        var source = new TaskCompletionSource<TAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(queryId, source))
        {
            throw new InvalidOperationException($"Region query {queryId} is already waiting.");
        }
        return new Pending(this, queryId, source.Task);
    }

    /// <summary>Hands an answer to its waiting query. False when nothing waits for that id.</summary>
    public bool Complete(TAnswer? answer)
    {
        if (answer == null || string.IsNullOrWhiteSpace(answer.QueryId)) return false;
        return _pending.TryRemove(answer.QueryId.Trim(), out var source) && source.TrySetResult(answer);
    }

    private void Forget(string queryId) => _pending.TryRemove(queryId, out _);

    public sealed class Pending : IDisposable
    {
        private readonly RegionQueryWaiter<TAnswer> _owner;

        internal Pending(RegionQueryWaiter<TAnswer> owner, string queryId, Task<TAnswer> result)
        {
            _owner = owner;
            QueryId = queryId;
            Result = result;
        }

        public string QueryId { get; }

        public Task<TAnswer> Result { get; }

        public void Dispose() => _owner.Forget(QueryId);
    }
}

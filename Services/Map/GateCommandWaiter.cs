using System.Collections.Concurrent;
using knkwebapi_v2.Realtime.Contracts;

namespace knkwebapi_v2.Services.Map;

/// <summary>
/// Overview map L7 (PandiO/knk-workspace#741, spec § 6.2.5): the gate commands waiting for their
/// <c>GateCommandResult</c>. Singleton: the endpoint registers a command id before it publishes <c>GateCommand</c>, the
/// game hub completes it. The first result for an id wins; a result for an unknown or finished id is ignored (a late
/// answer after the timeout, or a second game server).
/// </summary>
public sealed class GateCommandWaiter
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<GateCommandResultDto>> _pending =
        new(StringComparer.Ordinal);

    /// <summary>How many commands wait now (tests, logs).</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Starts waiting for <paramref name="commandId"/>; dispose the handle when done (removes the entry).</summary>
    public Pending Register(string commandId)
    {
        var source = new TaskCompletionSource<GateCommandResultDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(commandId, source))
        {
            throw new InvalidOperationException($"Gate command {commandId} is already waiting.");
        }
        return new Pending(this, commandId, source.Task);
    }

    /// <summary>Hands a result to its waiting command. False when nothing waits for that id.</summary>
    public bool Complete(GateCommandResultDto? result)
    {
        if (result == null || string.IsNullOrWhiteSpace(result.CommandId)) return false;
        return _pending.TryRemove(result.CommandId.Trim(), out var source) && source.TrySetResult(result);
    }

    private void Forget(string commandId) => _pending.TryRemove(commandId, out _);

    public sealed class Pending : IDisposable
    {
        private readonly GateCommandWaiter _owner;

        internal Pending(GateCommandWaiter owner, string commandId, Task<GateCommandResultDto> result)
        {
            _owner = owner;
            CommandId = commandId;
            Result = result;
        }

        public string CommandId { get; }

        public Task<GateCommandResultDto> Result { get; }

        public void Dispose() => _owner.Forget(CommandId);
    }
}

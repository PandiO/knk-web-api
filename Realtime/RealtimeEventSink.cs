using System.Runtime.CompilerServices;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// One event for the game servers, as a service produces it (DESIGN §4.3).
/// </summary>
/// <param name="Type">See <see cref="RealtimeEventTypes"/>.</param>
/// <param name="Payload">Small "this changed" data.</param>
/// <param name="ActorUserId">The user behind the change, when known; the sink adds the source.</param>
/// <param name="ServerId">One game server, or null for all of them.</param>
/// <param name="V">Contract version of <paramref name="Type"/>.</param>
public sealed record RealtimeEvent(string Type, object? Payload, int? ActorUserId = null, string? ServerId = null, int V = 1);

/// <summary>
/// Where services hand their realtime events (DESIGN §4.3, decision Q3). Option A: the event goes out after the
/// database work it describes has committed (P4); when a transaction is open on the request's <c>KnKDbContext</c>
/// it waits for that commit and is dropped on a rollback. The S3 entity interceptor feeds the same sink, and option C
/// (an outbox) replaces only this implementation. Never blocks and never throws into the caller (P5).
/// </summary>
public interface IRealtimeEventSink
{
    void Publish(RealtimeEvent realtimeEvent);
}

/// <summary>
/// <see cref="IRealtimeEventSink"/> for option A (scoped, one per request or background scope). Sends at once when
/// no transaction is open on the scope's <see cref="KnKDbContext"/>, else when it commits
/// (<see cref="RealtimeTransactionEvents"/>). The actor's source is the request's caller: the plugin key =
/// <c>plugin</c>, a logged-in user = <c>web</c>, no request (a background job) = <c>system</c>.
/// </summary>
public sealed class AfterCommitRealtimeEventSink : IRealtimeEventSink
{
    private readonly IRealtimePublisher _publisher;
    private readonly RealtimeTransactionEvents _transactions;
    private readonly DbContext? _db;
    private readonly IHttpContextAccessor? _http;
    private readonly ILogger<AfterCommitRealtimeEventSink>? _logger;
    private readonly Func<DbContext, bool> _inTransaction;

    /// <param name="db">The scope's <see cref="KnKDbContext"/>; null sends at once.</param>
    /// <param name="inTransaction">Whether a transaction is open on <paramref name="db"/> (tests; default:
    /// <c>Database.CurrentTransaction != null</c>).</param>
    public AfterCommitRealtimeEventSink(IRealtimePublisher publisher, RealtimeTransactionEvents transactions,
        DbContext? db = null, IHttpContextAccessor? http = null, ILogger<AfterCommitRealtimeEventSink>? logger = null,
        Func<DbContext, bool>? inTransaction = null)
    {
        _publisher = publisher;
        _transactions = transactions;
        _db = db;
        _http = http;
        _logger = logger;
        _inTransaction = inTransaction ?? (context => context.Database.CurrentTransaction != null);
    }

    /// <summary>The scope's sink: its <see cref="KnKDbContext"/> (when registered) and request.</summary>
    public static AfterCommitRealtimeEventSink Create(IServiceProvider services) =>
        new(services.GetRequiredService<IRealtimePublisher>(), services.GetRequiredService<RealtimeTransactionEvents>(),
            services.GetService<KnKDbContext>(), services.GetService<IHttpContextAccessor>(),
            services.GetService<ILogger<AfterCommitRealtimeEventSink>>());

    public void Publish(RealtimeEvent realtimeEvent)
    {
        try
        {
            var actor = new RealtimeActor(realtimeEvent.ActorUserId, Source());
            void Send() => _publisher.PublishToGameServers(realtimeEvent.Type, realtimeEvent.Payload,
                realtimeEvent.ServerId, actor, realtimeEvent.V);

            if (_db != null && _inTransaction(_db))
            {
                _transactions.Defer(_db, Send);
            }
            else
            {
                Send();
            }
        }
        catch (Exception ex)
        {
            // P5: the hub is a doorbell; the change itself already happened.
            _logger?.LogWarning(ex, "Realtime event {Type} could not be queued", realtimeEvent.Type);
        }
    }

    private string Source()
    {
        var context = _http?.HttpContext;
        if (context == null) return "system";
        var caller = context.GetKnkCaller();
        return caller.IsPluginService ? "plugin" : caller.IsWebUser ? "web" : "system";
    }
}

/// <summary>
/// Holds realtime events raised inside a database transaction until it commits (DESIGN §4.3 option A, P4). Registered
/// as an EF Core transaction interceptor on <see cref="KnKDbContext"/>: a commit sends the context's held events in
/// order, a rollback or failure drops them, and a new transaction drops anything left from one that was disposed
/// without either. Singleton; per context, so concurrent requests don't mix.
/// </summary>
public sealed class RealtimeTransactionEvents : DbTransactionInterceptor
{
    private sealed class Held
    {
        public List<Action> Sends { get; } = new();
    }

    private readonly ConditionalWeakTable<DbContext, Held> _held = new();
    private readonly ILogger<RealtimeTransactionEvents>? _logger;

    public RealtimeTransactionEvents(ILogger<RealtimeTransactionEvents>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Holds <paramref name="send"/> until <paramref name="context"/>'s transaction commits.</summary>
    public void Defer(DbContext context, Action send)
    {
        var held = _held.GetOrCreateValue(context);
        lock (held)
        {
            held.Sends.Add(send);
        }
    }

    /// <summary>How many events wait for <paramref name="context"/>'s commit.</summary>
    public int HeldCount(DbContext context)
    {
        if (!_held.TryGetValue(context, out var held)) return 0;
        lock (held)
        {
            return held.Sends.Count;
        }
    }

    /// <summary>The transaction committed: send what it held, in order.</summary>
    public void Committed(DbContext? context)
    {
        foreach (var send in Take(context))
        {
            try
            {
                send();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Realtime event held for a transaction could not be queued");
            }
        }
    }

    /// <summary>The transaction rolled back, failed or was replaced: drop what it held.</summary>
    public void Discarded(DbContext? context)
    {
        var dropped = Take(context);
        if (dropped.Count > 0)
        {
            _logger?.LogDebug("Dropped {Count} realtime event(s) of a transaction that did not commit", dropped.Count);
        }
    }

    private List<Action> Take(DbContext? context)
    {
        if (context == null || !_held.TryGetValue(context, out var held)) return new List<Action>();
        lock (held)
        {
            var sends = held.Sends.ToList();
            held.Sends.Clear();
            return sends;
        }
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Discarded(eventData.Context);
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result, CancellationToken cancellationToken = default)
    {
        Discarded(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Committed(eventData.Context);

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Committed(eventData.Context);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Discarded(eventData.Context);

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discarded(eventData.Context);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        Discarded(eventData.Context);

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discarded(eventData.Context);
        return Task.CompletedTask;
    }
}

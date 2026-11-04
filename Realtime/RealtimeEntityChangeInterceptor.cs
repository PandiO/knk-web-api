using System.Runtime.CompilerServices;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// S3 (PandiO/knk-workspace#182, DESIGN §4.3 option A, decision Q3): one EF Core save interceptor that sees every write
/// path (form wizard, dashboard, imports, seeders) without touching the services. Before a save it notes the watched
/// entities (<see cref="RealtimeEntityChangeMap.IsWatched"/>) and which properties changed; after a successful save it
/// reads their ids, maps them to events and hands them to the context's <see cref="IRealtimeEventSink"/>, which sends
/// them at once or, inside an explicit transaction, when it commits (<see cref="RealtimeTransactionEvents"/>). A failed
/// save sends nothing. Option C (an outbox) only swaps the sink. Singleton; the pending changes are kept per context.
/// <para>Writes the interceptor can't see (<c>ExecuteUpdate</c>/raw SQL) send their events explicitly: balances (S2,
/// <c>CurrencyService</c>); presence has no game-server event.</para>
/// </summary>
public sealed class RealtimeEntityChangeInterceptor : SaveChangesInterceptor
{
    private sealed class Pending
    {
        public List<(EntityEntry Entry, RealtimeEntityState State, IReadOnlySet<string> Modified)> Entries { get; } = new();
    }

    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();
    private readonly Func<DbContext, IRealtimeEventSink?> _sinkFor;
    private readonly IHttpContextAccessor? _http;
    private readonly ILogger<RealtimeEntityChangeInterceptor>? _logger;

    /// <param name="sinkFor">The sink for a context's events (default: an <see cref="AfterCommitRealtimeEventSink"/>
    /// over that context, built by <see cref="RealtimeSetup"/>).</param>
    /// <param name="http">For the actor's user id; null outside a request.</param>
    public RealtimeEntityChangeInterceptor(Func<DbContext, IRealtimeEventSink?> sinkFor, IHttpContextAccessor? http = null,
        ILogger<RealtimeEntityChangeInterceptor>? logger = null)
    {
        _sinkFor = sinkFor;
        _http = http;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Publish(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Publish(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Drop(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Drop(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => Drop(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Drop(eventData.Context);
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        if (context == null) return;
        try
        {
            if (context.ChangeTracker.AutoDetectChangesEnabled)
            {
                context.ChangeTracker.DetectChanges();
            }
            var pending = _pending.GetOrCreateValue(context);
            lock (pending)
            {
                // A save that threw before SavingChanges finished leaves nothing; anything older is stale.
                pending.Entries.Clear();
                foreach (var entry in context.ChangeTracker.Entries())
                {
                    var state = entry.State switch
                    {
                        EntityState.Added => RealtimeEntityState.Added,
                        EntityState.Modified => RealtimeEntityState.Modified,
                        EntityState.Deleted => RealtimeEntityState.Deleted,
                        _ => (RealtimeEntityState?)null,
                    };
                    if (state == null || !RealtimeEntityChangeMap.IsWatched(entry.Metadata.ClrType)) continue;
                    IReadOnlySet<string> modified = state == RealtimeEntityState.Modified
                        ? entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToHashSet(StringComparer.Ordinal)
                        : new HashSet<string>();
                    pending.Entries.Add((entry, state.Value, modified));
                }
            }
        }
        catch (Exception ex)
        {
            // P5: the save goes on; the game server catches up on its next re-sync.
            _logger?.LogWarning(ex, "Collecting realtime entity changes failed");
        }
    }

    private void Publish(DbContext? context)
    {
        if (context == null || !_pending.TryGetValue(context, out var pending)) return;
        List<RealtimeEntityChange> changes;
        lock (pending)
        {
            if (pending.Entries.Count == 0) return;
            changes = pending.Entries.Select(e => new RealtimeEntityChange(e.Entry.Metadata.ClrType, e.State, e.Modified,
                ReadValues(e.Entry))).ToList();
            pending.Entries.Clear();
        }
        try
        {
            var events = RealtimeEntityChangeMap.Map(changes, ActorUserId());
            if (events.Count == 0) return;
            var sink = _sinkFor(context);
            if (sink == null) return;
            foreach (var realtimeEvent in events)
            {
                sink.Publish(realtimeEvent);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Publishing realtime entity changes failed");
        }
    }

    private void Drop(DbContext? context)
    {
        if (context == null || !_pending.TryGetValue(context, out var pending)) return;
        lock (pending)
        {
            pending.Entries.Clear();
        }
    }

    /// <summary>After the save: the entity object holds its generated id; a deleted entity keeps its values.</summary>
    private static IReadOnlyDictionary<string, object?> ReadValues(EntityEntry entry)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in RealtimeEntityChangeMap.ValueNames)
        {
            var property = entry.Metadata.FindProperty(name);
            if (property?.PropertyInfo == null) continue;
            try
            {
                values[name] = property.PropertyInfo.GetValue(entry.Entity);
            }
            catch (Exception)
            {
                // A value the rules can't read is treated as missing.
            }
        }
        return values;
    }

    private int? ActorUserId()
    {
        var context = _http?.HttpContext;
        if (context == null) return null;
        try
        {
            var caller = context.GetKnkCaller();
            return caller.IsWebUser ? caller.WebUserId : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

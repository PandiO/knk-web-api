using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>What one sweep pass did (PandiO/knk-workspace#180).</summary>
    public readonly record struct WorldTaskSweepResult(int Expired, int Released, int SessionsRemoved, int OffersTimedOut = 0)
    {
        public bool Any => Expired > 0 || Released > 0 || SessionsRemoved > 0 || OffersTimedOut > 0;
    }

    /// <summary>
    /// PandiO/knk-workspace#180 (DESIGN §8.2, decisions Q10/Q18): one pass of the world-task sweep.
    /// <list type="bullet">
    /// <item>Pending past <c>ExpiresAt</c> → Expired (rows from before #180 without one: created/updated + the TTL).</item>
    /// <item>InProgress past <c>LeaseUntil</c> (no heartbeat) → released to Pending with a fresh expiry, code kept.</item>
    /// <item>Workflow sessions no draft points at and with no activity for <c>SessionRetentionDays</c> → removed with their
    /// tasks. Kept: sessions with a Completed task (its output can still be read: <c>DomainWorldResolver</c> finds a
    /// region's world through <c>tempregion_worldtask_&lt;id&gt;</c>) and catalogue-scan sessions (the scan row points at its task).</item>
    /// <item>#269: an expired region task a game server held, and the region tasks of a removed session, release their
    /// <c>tempregion_worldtask_&lt;id&gt;</c> regions (<see cref="TempRegionReleases"/>) in the same save.</item>
    /// <item>#181: an offer (Offered) with no answer after <c>OfferTimeoutSeconds</c> (2 min) → TimedOut (the web app shows the
    /// task code; the game server drops the Accept/Decline line).</item>
    /// </list>
    /// Every change is told to the hubs (<see cref="IWorldTaskRealtime"/>, WT2). Every row is saved on its own and is conditional on the status it was read with (concurrency token), so a claim,
    /// heartbeat or completion that lands in between wins and the row is left for the next pass.
    /// </summary>
    public class WorldTaskSweeper
    {
        public const string CatalogScanSessionType = "MinecraftCatalogScan";

        private readonly KnKDbContext _db;
        private readonly WorldTaskOptions _options;
        private readonly Func<DateTime> _utcNow;
        private readonly IWorldTaskRealtime _realtime;

        public WorldTaskSweeper(KnKDbContext db, IOptions<WorldTaskOptions>? options = null, Func<DateTime>? utcNow = null,
            IWorldTaskRealtime? realtime = null)
        {
            _db = db;
            _options = options?.Value ?? new WorldTaskOptions();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _realtime = realtime ?? NoWorldTaskRealtime.Instance;
        }

        public async Task<WorldTaskSweepResult> RunOnceAsync(CancellationToken ct = default)
        {
            var now = _utcNow();
            var expired = await ExpirePendingAsync(now, ct);
            var released = await ReleaseLapsedLeasesAsync(now, ct);
            var removed = await RemoveAbandonedSessionsAsync(now, ct);
            var timedOut = await TimeOutOffersAsync(now, ct);
            return new WorldTaskSweepResult(expired, released, removed, timedOut);
        }

        private async Task<int> TimeOutOffersAsync(DateTime now, CancellationToken ct)
        {
            var cutoff = now - _options.OfferTimeout;
            var due = await _db.WorldTasks
                .Include(t => t.TargetUser)
                .Include(t => t.WorkflowSession)
                .Where(t => t.Status == WorldTaskStatuses.Pending && t.OfferState == WorldTaskOfferStates.Offered
                    && t.OfferedAt != null && t.OfferedAt <= cutoff)
                .OrderBy(t => t.Id)
                .Take(_options.SweepBatchSize)
                .ToListAsync(ct);

            var count = 0;
            foreach (var task in due)
            {
                task.OfferState = WorldTaskOfferStates.TimedOut;
                task.UpdatedAt = now;
                if (!await TrySaveAsync(task, ct)) continue;
                count++;
                _realtime.Changed(task);
                if (NormalizeUuid(task.TargetUser?.Uuid) is { } uuid)
                {
                    _realtime.OfferWithdrawn(task, uuid, "Nobody answered the offer in time.");
                }
            }
            return count;
        }

        private static string? NormalizeUuid(string? uuid) =>
            string.IsNullOrWhiteSpace(uuid) ? null : uuid.Trim().ToLowerInvariant();

        private async Task<int> ExpirePendingAsync(DateTime now, CancellationToken ct)
        {
            var ttl = _options.PendingTtl;
            var legacyCutoff = now - ttl;
            var due = await _db.WorldTasks
                .Include(t => t.TargetUser)
                .Include(t => t.WorkflowSession)
                .Where(t => t.Status == WorldTaskStatuses.Pending
                    && (t.ExpiresAt != null ? t.ExpiresAt <= now : (t.UpdatedAt ?? t.CreatedAt) <= legacyCutoff))
                .OrderBy(t => t.Id)
                .Take(_options.SweepBatchSize)
                .ToListAsync(ct);

            var count = 0;
            foreach (var task in due)
            {
                task.Status = WorldTaskStatuses.Expired;
                task.ErrorMessage = $"Expired: nobody claimed it within {_options.PendingTtlMinutes} minutes.";
                task.LeaseUntil = null;
                task.UpdatedAt = now;
                var offeredTo = task.OfferState == WorldTaskOfferStates.Offered ? NormalizeUuid(task.TargetUser?.Uuid) : null;
                // #269: a region task a game server held (and lost, e.g. a crash after drawing) releases its temp region.
                var release = await TempRegionReleases.AddIfNewAsync(_db, task, TempRegionReleaseReasons.TaskExpired, now, ct);
                if (!await TrySaveAsync(task, ct, release)) continue;
                count++;
                _realtime.Changed(task);
                if (offeredTo != null)
                {
                    _realtime.Cancelled(task, offeredTo, task.ErrorMessage);
                }
            }
            return count;
        }

        private async Task<int> ReleaseLapsedLeasesAsync(DateTime now, CancellationToken ct)
        {
            var legacyCutoff = now - _options.Lease;
            var due = await _db.WorldTasks
                .Include(t => t.TargetUser)
                .Include(t => t.WorkflowSession)
                .Where(t => t.Status == WorldTaskStatuses.InProgress
                    && (t.LeaseUntil != null ? t.LeaseUntil <= now : (t.UpdatedAt ?? t.ClaimedAt ?? t.CreatedAt) <= legacyCutoff))
                .OrderBy(t => t.Id)
                .Take(_options.SweepBatchSize)
                .ToListAsync(ct);

            var count = 0;
            foreach (var task in due)
            {
                task.Status = WorldTaskStatuses.Pending;
                task.ClaimedAt = null;
                task.ClaimedByServerId = null;
                task.ClaimedByMinecraftUsername = null;
                task.ClaimedByUuid = null;
                task.ClaimedByUserId = null;
                task.LeaseUntil = null;
                task.ReleaseCount++;
                task.ExpiresAt = now + _options.PendingTtl;
                task.UpdatedAt = now;
                if (!await TrySaveAsync(task, ct)) continue;
                count++;
                _realtime.Changed(task);
                if (WorldTaskService.HeadlessTaskTypes.Contains(task.TaskType))
                {
                    _realtime.HeadlessPending(task);
                }
            }
            return count;
        }

        private async Task<int> RemoveAbandonedSessionsAsync(DateTime now, CancellationToken ct)
        {
            var cutoff = now - _options.SessionRetention;
            var sessions = await _db.WorkflowSessions
                .Where(s => (s.EntityTypeName == null || s.EntityTypeName != CatalogScanSessionType)
                    && (s.UpdatedAt ?? s.CreatedAt) <= cutoff
                    && !_db.FormSubmissionProgresses.Any(p => p.WorkflowSessionId == s.Id)
                    && !s.WorldTasks.Any(t => t.Status == WorldTaskStatuses.Completed
                        || (t.UpdatedAt ?? t.CreatedAt) > cutoff
                        || (t.LeaseUntil != null && t.LeaseUntil > now)))
                .OrderBy(s => s.Id)
                .Take(_options.SweepBatchSize)
                .Include(s => s.WorldTasks)
                .Include(s => s.Steps)
                .ToListAsync(ct);

            var count = 0;
            foreach (var session in sessions)
            {
                // #269: the session's region tasks (none Completed) release their temp regions in the same save; the
                // release rows outlive the tasks.
                foreach (var task in session.WorldTasks.OrderBy(t => t.Id))
                {
                    await TempRegionReleases.AddIfNewAsync(_db, task, TempRegionReleaseReasons.SessionRemoved, now, ct);
                }
                _db.WorkflowSessions.Remove(session);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    count++;
                }
                catch (DbUpdateException)
                {
                    // Changed or referenced meanwhile; leave it for the next pass.
                    _db.ChangeTracker.Clear();
                }
            }
            return count;
        }

        private async Task<bool> TrySaveAsync(WorldTask task, CancellationToken ct, TempRegionRelease? release = null)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A claim, heartbeat or completion won; the next pass sees the new state.
                _db.Entry(task).State = EntityState.Detached;
                if (release != null)
                {
                    _db.Entry(release).State = EntityState.Detached;
                }
                return false;
            }
        }
    }

    /// <summary>PandiO/knk-workspace#180: runs <see cref="WorldTaskSweeper"/> every <c>WorldTasks:SweepIntervalSeconds</c> (30 s).</summary>
    public class WorldTaskSweepService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WorldTaskSweepService> _logger;
        private readonly WorldTaskOptions _options;
        private readonly ConnectivityAwareFailureLogger _failures;

        public WorldTaskSweepService(IServiceProvider serviceProvider, ILogger<WorldTaskSweepService> logger,
            IOptions<WorldTaskOptions> options)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;
            _failures = new ConnectivityAwareFailureLogger(logger, "World task sweep");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.SweepEnabled)
            {
                _logger.LogInformation("World task sweep is off (WorldTasks:SweepEnabled=false)");
                return;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.SweepIntervalSeconds)));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var sweeper = scope.ServiceProvider.GetRequiredService<WorldTaskSweeper>();
                        var result = await sweeper.RunOnceAsync(stoppingToken);
                        if (result.Any)
                        {
                            _logger.LogInformation(
                                "World task sweep: {Expired} expired, {Released} released (no heartbeat), {Sessions} abandoned session(s) removed, {Offers} offer(s) timed out",
                                result.Expired, result.Released, result.SessionsRemoved, result.OffersTimedOut);
                        }
                        _failures.Succeeded();
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _failures.Failed(ex, "World task sweep failed");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}

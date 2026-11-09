using System.Text.Json;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// PandiO/knk-workspace#269: the temp regions (<c>tempregion_worldtask_&lt;id&gt;</c>) of world tasks nothing will use,
    /// queued for the game server to remove earlier than its 14-day cleanup. See <see cref="TempRegionRelease"/>.
    /// </summary>
    public interface ITempRegionReleaseService
    {
        /// <summary>
        /// Queues the region of <paramref name="task"/> (a closed task, or any task of a deleted draft) when it can have
        /// one. Never throws: a failure is logged and the 14-day cleanup stays the backstop.
        /// </summary>
        Task QueueAsync(WorldTask task, string reason, CancellationToken ct = default);

        /// <summary>
        /// Queues the regions of every task of draft <paramref name="draftId"/>, called right before the draft is deleted
        /// (the delete clears <c>world_tasks.FormSubmissionProgressId</c>). Never throws.
        /// </summary>
        Task QueueForDraftAsync(int draftId, CancellationToken ct = default);

        /// <summary>
        /// The releases game server <paramref name="serverId"/> may act on now: past the grace period, their task closed
        /// (or gone), not a Completed task of a draft that still exists, and no domain, domain sub-region, lootbox area
        /// (its region or exclusions), road edge or draft names the region. Drops releases older than the keep period.
        /// </summary>
        Task<List<TempRegionReleaseDto>> ListDueAsync(string? serverId, int max = 50, CancellationToken ct = default);

        /// <summary>The game server's answer; deletes the release. False when it is gone already.</summary>
        Task<bool> SettleAsync(int id, TempRegionReleaseSettleDto dto, CancellationToken ct = default);
    }

    /// <summary>#269: which world tasks can leave a temp region, and the release row for one.</summary>
    public static class TempRegionReleases
    {
        /// <summary>The plugin's region task handler (<c>WgRegionIdTaskHandler</c>) is found through this field name.</summary>
        public const string RegionTaskField = "WgRegionId";

        public static string RegionIdFor(int worldTaskId) => DomainRegionNameFinalizer.TempRegionPrefix + worldTaskId;

        /// <summary>A task the plugin's region handler runs: field <c>WgRegionId</c>, or a result naming its temp region.</summary>
        public static bool IsRegionTask(WorldTask task) =>
            string.Equals(task.FieldName?.Trim(), RegionTaskField, StringComparison.OrdinalIgnoreCase)
            || (task.OutputJson != null && task.OutputJson.Contains(RegionIdFor(task.Id), StringComparison.OrdinalIgnoreCase));

        /// <summary>A game server claimed it at some point (a release keeps <c>ReleaseCount</c>), so it may have drawn a region.</summary>
        public static bool WasEverClaimed(WorldTask task) =>
            task.ClaimedAt != null || task.ReleaseCount > 0 || !string.IsNullOrWhiteSpace(task.ClaimedByServerId);

        /// <summary>
        /// Whether <paramref name="task"/>'s temp region is released for <paramref name="reason"/>:
        /// <list type="bullet">
        /// <item>only region tasks;</item>
        /// <item>a deleted draft: its open tasks (listed once they close), its Completed tasks unless the result picked
        /// another region ("select", nothing drawn), and its other closed tasks a game server ever held;</item>
        /// <item>a task that closed without a result, or whose session the sweep removes: only when a game server ever held
        /// it (an unclaimed task drew nothing); never a Completed one.</item>
        /// </list>
        /// </summary>
        public static bool ShouldRelease(WorldTask task, string reason)
        {
            if (!IsRegionTask(task))
            {
                return false;
            }
            if (reason == TempRegionReleaseReasons.DraftDeleted)
            {
                if (WorldTaskStatuses.IsOpen(task.Status))
                {
                    return true;
                }
                if (task.Status == WorldTaskStatuses.Completed)
                {
                    string? picked = RegionOf(task.OutputJson);
                    return picked == null || picked.Equals(RegionIdFor(task.Id), StringComparison.OrdinalIgnoreCase);
                }
                return WasEverClaimed(task);
            }
            return task.Status != WorldTaskStatuses.Completed && WasEverClaimed(task);
        }

        /// <summary>
        /// Adds a release for <paramref name="task"/> to <paramref name="db"/> without saving, unless it doesn't qualify or
        /// one exists for the task already. Returns the new row, or null.
        /// </summary>
        public static async Task<TempRegionRelease?> AddIfNewAsync(KnKDbContext db, WorldTask task, string reason, DateTime now,
            CancellationToken ct = default)
        {
            if (task.Id <= 0 || !ShouldRelease(task, reason))
            {
                return null;
            }
            if (db.TempRegionReleases.Local.Any(r => r.WorldTaskId == task.Id)
                || await db.TempRegionReleases.AnyAsync(r => r.WorldTaskId == task.Id, ct))
            {
                return null;
            }
            var row = new TempRegionRelease
            {
                WorldTaskId = task.Id,
                RegionId = RegionIdFor(task.Id),
                World = Truncate(WorldOf(task.OutputJson), 100),
                ServerId = Truncate(string.IsNullOrWhiteSpace(task.ClaimedByServerId) ? null : task.ClaimedByServerId.Trim(), 100),
                Reason = reason,
                RequestedAt = now
            };
            db.TempRegionReleases.Add(row);
            return row;
        }

        /// <summary>The <c>worldName</c> of a region task's result, or null.</summary>
        public static string? WorldOf(string? outputJson) => StringProperty(outputJson, "worldName");

        /// <summary>The <c>regionId</c> of a region task's result, or null.</summary>
        public static string? RegionOf(string? outputJson) => StringProperty(outputJson, "regionId");

        private static string? StringProperty(string? json, string name)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        string? value = property.Value.GetString();
                        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    }
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Truncate(string? value, int max) => value == null || value.Length <= max ? value : value[..max];
    }

    /// <summary>#269: see <see cref="ITempRegionReleaseService"/>.</summary>
    public class TempRegionReleaseService : ITempRegionReleaseService
    {
        /// <summary>Rows looked at per list call (most are listed; the others wait or are kept by a use).</summary>
        private const int ScanLimit = 200;

        private readonly KnKDbContext _db;
        private readonly WorldTaskOptions _options;
        private readonly Func<DateTime> _utcNow;
        private readonly ILogger<TempRegionReleaseService> _logger;

        public TempRegionReleaseService(KnKDbContext db, IOptions<WorldTaskOptions>? options = null,
            ILogger<TempRegionReleaseService>? logger = null, Func<DateTime>? utcNow = null)
        {
            _db = db;
            _options = options?.Value ?? new WorldTaskOptions();
            _logger = logger ?? NullLogger<TempRegionReleaseService>.Instance;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public async Task QueueAsync(WorldTask task, string reason, CancellationToken ct = default)
        {
            TempRegionRelease? row = null;
            try
            {
                row = await TempRegionReleases.AddIfNewAsync(_db, task, reason, _utcNow(), ct);
                if (row == null)
                {
                    return;
                }
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Temp region {RegionId} released ({Reason}); the game server removes it", row.RegionId, reason);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (row != null)
                {
                    _db.Entry(row).State = EntityState.Detached;
                }
                _logger.LogWarning(ex, "Could not release the temp region of world task {TaskId}; the 14-day cleanup removes it", task.Id);
            }
        }

        public async Task QueueForDraftAsync(int draftId, CancellationToken ct = default)
        {
            var added = new List<TempRegionRelease>();
            try
            {
                var tasks = await _db.WorldTasks.AsNoTracking()
                    .Where(t => t.FormSubmissionProgressId == draftId)
                    .OrderBy(t => t.Id)
                    .ToListAsync(ct);
                var now = _utcNow();
                foreach (var task in tasks)
                {
                    if (await TempRegionReleases.AddIfNewAsync(_db, task, TempRegionReleaseReasons.DraftDeleted, now, ct) is { } row)
                    {
                        added.Add(row);
                    }
                }
                if (added.Count == 0)
                {
                    return;
                }
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Draft {DraftId} deleted: released temp region(s) {RegionIds}", draftId,
                    string.Join(", ", added.Select(r => r.RegionId)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var row in added)
                {
                    _db.Entry(row).State = EntityState.Detached;
                }
                _logger.LogWarning(ex, "Could not release the temp regions of draft {DraftId}; the 14-day cleanup removes them", draftId);
            }
        }

        public async Task<List<TempRegionReleaseDto>> ListDueAsync(string? serverId, int max = 50, CancellationToken ct = default)
        {
            var now = _utcNow();
            await DropStaleAsync(now - _options.TempRegionReleaseKeep, ct);

            string? server = string.IsNullOrWhiteSpace(serverId) ? null : serverId.Trim();
            var dueBy = now - _options.TempRegionReleaseGrace;
            var rows = await _db.TempRegionReleases.AsNoTracking()
                .Where(r => r.RequestedAt <= dueBy && (server == null || r.ServerId == null || r.ServerId == server))
                .OrderBy(r => r.Id)
                .Take(ScanLimit)
                .ToListAsync(ct);

            var due = new List<TempRegionReleaseDto>();
            foreach (var row in rows)
            {
                if (due.Count >= Math.Clamp(max, 1, 200))
                {
                    break;
                }
                string? wait = await WaitReasonAsync(row, dueBy, ct);
                if (wait != null)
                {
                    _logger.LogDebug("Temp region release {Id} ({RegionId}) waits: {Why}", row.Id, row.RegionId, wait);
                    continue;
                }
                due.Add(new TempRegionReleaseDto
                {
                    Id = row.Id,
                    WorldTaskId = row.WorldTaskId,
                    RegionId = row.RegionId,
                    World = row.World,
                    Reason = row.Reason,
                    RequestedAt = row.RequestedAt
                });
            }
            return due;
        }

        public async Task<bool> SettleAsync(int id, TempRegionReleaseSettleDto dto, CancellationToken ct = default)
        {
            var outcome = TempRegionReleaseOutcomes.Normalize(dto?.Outcome)
                ?? throw new ArgumentException($"Unknown outcome '{dto?.Outcome}'. Use one of: {string.Join(", ", TempRegionReleaseOutcomes.All)}.");
            var row = await _db.TempRegionReleases.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (row == null)
            {
                return false;
            }
            _db.TempRegionReleases.Remove(row);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Settled by another call meanwhile.
                _db.Entry(row).State = EntityState.Detached;
                return false;
            }
            string? detail = string.IsNullOrWhiteSpace(dto!.Detail) ? null : dto.Detail.Trim();
            if (detail != null && detail.Length > 200) detail = detail[..200];
            _logger.LogInformation("Temp region {RegionId} (world task {TaskId}, {Reason}): {Outcome} by game server {ServerId}{Detail}",
                row.RegionId, row.WorldTaskId, row.Reason, outcome, string.IsNullOrWhiteSpace(dto.ServerId) ? "?" : dto.ServerId.Trim(),
                detail == null ? string.Empty : " - " + detail);
            return true;
        }

        /// <summary>Why <paramref name="row"/> isn't offered yet (or ever), or null when the game server may remove its region.</summary>
        private async Task<string?> WaitReasonAsync(TempRegionRelease row, DateTime dueBy, CancellationToken ct)
        {
            var task = await _db.WorldTasks.AsNoTracking()
                .Where(t => t.Id == row.WorldTaskId)
                .Select(t => new { t.Status, t.FormSubmissionProgressId, t.UpdatedAt, t.CreatedAt })
                .FirstOrDefaultAsync(ct);
            if (task != null)
            {
                if (WorldTaskStatuses.IsOpen(task.Status))
                {
                    return $"task is {task.Status}";
                }
                if ((task.UpdatedAt ?? task.CreatedAt) > dueBy)
                {
                    return "task changed within the grace period";
                }
                if (task.Status == WorldTaskStatuses.Completed && task.FormSubmissionProgressId is int draftId
                    && await _db.FormSubmissionProgresses.AnyAsync(p => p.Id == draftId, ct))
                {
                    return $"its result belongs to draft {draftId}, which still exists";
                }
            }
            return await UsedByAsync(row.RegionId, ct);
        }

        /// <summary>
        /// What names <paramref name="regionId"/>, in any world (stricter than WorldGuard's per-world ids, on purpose), or
        /// null. Exclusions, road edges and drafts are matched as text, so a longer id that contains it also keeps it.
        /// </summary>
        private async Task<string?> UsedByAsync(string regionId, CancellationToken ct)
        {
            string lowered = regionId.Trim().ToLower();
            if (await _db.Domains.AsNoTracking().AnyAsync(d => d.WgRegionId != null && d.WgRegionId.ToLower() == lowered, ct))
                return "a domain uses it";
            if (await _db.DomainSubRegions.AsNoTracking().AnyAsync(s => s.WgRegionId != null && s.WgRegionId.ToLower() == lowered, ct))
                return "a domain sub-region uses it";
            if (await _db.LootboxSpawnAreas.AsNoTracking().AnyAsync(a => (a.WgRegionId != null && a.WgRegionId.ToLower() == lowered)
                    || (a.ExcludedRegionIds != null && a.ExcludedRegionIds.ToLower().Contains(lowered)), ct))
                return "a lootbox spawn area uses or excludes it";
            if (await _db.RoadEdges.AsNoTracking().AnyAsync(e => e.RegionIdsJson != null && e.RegionIdsJson.ToLower().Contains(lowered), ct))
                return "a road edge names it";
            if (await _db.FormSubmissionProgresses.AsNoTracking().AnyAsync(p =>
                    (p.AllStepsDataJson != null && p.AllStepsDataJson.ToLower().Contains(lowered))
                    || (p.CurrentStepDataJson != null && p.CurrentStepDataJson.ToLower().Contains(lowered)), ct))
                return "a draft names it";
            return null;
        }

        private async Task DropStaleAsync(DateTime before, CancellationToken ct)
        {
            var stale = await _db.TempRegionReleases.Where(r => r.RequestedAt <= before).Take(ScanLimit).ToListAsync(ct);
            if (stale.Count == 0)
            {
                return;
            }
            _db.TempRegionReleases.RemoveRange(stale);
            try
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Dropped {Count} temp region release(s) older than {Days} days; the 14-day cleanup decides on those regions",
                    stale.Count, _options.TempRegionReleaseKeepDays);
            }
            catch (DbUpdateException)
            {
                foreach (var row in stale)
                {
                    _db.Entry(row).State = EntityState.Detached;
                }
            }
        }
    }
}

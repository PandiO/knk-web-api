using System.Data;
using System.Diagnostics;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.LocationRetention;

/// <summary>
/// Location retention (KNG-80, knk-workspace docs/architecture/location-retention.md).
/// <para>
/// A Location is an orphan when its name is still the default (LocationReferenceGraph.DefaultName,
/// null or empty), no foreign key in the EF model references it, no JSON reference source names it,
/// and it is older than the grace period. A run reads the orphans in keyset batches (read only),
/// then compares them with the review items: new orphans become Open items, known ones are touched,
/// Kept ones are re-flagged after KeptRecheckMonths or when the Location changed, and Open items
/// that stopped being orphans are Resolved. A run that found something new queues one in-game
/// digest. Only a staff member's Delete removes a Location, after re-checking it in the same
/// transaction with the row locked.
/// </para>
/// </summary>
public class LocationRetentionService : ILocationRetentionService
{
    private const int NoteMaxLength = 500;

    private readonly KnKDbContext _context;
    private readonly IEnumerable<ILocationReferenceSource> _referenceSources;
    private readonly IPlayerNotificationQueue _notifications;
    private readonly LocationRetentionRunGate _gate;
    private readonly ILogger<LocationRetentionService> _logger;
    private readonly LocationRetentionOptions _options;

    public LocationRetentionService(
        KnKDbContext context,
        IEnumerable<ILocationReferenceSource> referenceSources,
        IPlayerNotificationQueue notifications,
        LocationRetentionRunGate gate,
        ILogger<LocationRetentionService> logger,
        IOptions<LocationRetentionOptions>? options = null)
    {
        _context = context;
        _referenceSources = referenceSources;
        _notifications = notifications;
        _gate = gate;
        _logger = logger;
        _options = options?.Value ?? new LocationRetentionOptions();
    }

    // ===== Run =====

    public async Task<LocationRetentionRunDto?> RunCheckAsync(string trigger, int? triggeredByUserId, DateTime? scheduledSlotUtc = null, CancellationToken ct = default)
    {
        if (!_gate.TryBegin()) return null;
        try
        {
            var settings = await GetOrCreateSettingsAsync(ct);
            var now = DateTime.UtcNow;
            var run = new LocationRetentionRun
            {
                Trigger = trigger,
                TriggeredByUserId = triggeredByUserId,
                ScheduledSlotUtc = scheduledSlotUtc,
                StartedAt = now
            };
            _context.LocationRetentionRuns.Add(run);
            await _context.SaveChangesAsync(ct);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await ScanAsync(run, settings, now, ct);
                run.Succeeded = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Location retention run {RunId} failed", run.Id);
                // Leave none of the half-done item changes behind; record the failure on the run.
                _context.ChangeTracker.Clear();
                _context.LocationRetentionRuns.Attach(run);
                run.Succeeded = false;
                run.Error = Truncate(ex.Message, 1000);
            }
            run.FinishedAt = DateTime.UtcNow;
            run.DurationMs = stopwatch.ElapsedMilliseconds;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Location retention run {RunId} ({Trigger}): scanned {Scanned}, orphans {Found} (new {New}, re-flagged {Reflagged}, known {Known}), resolved {Resolved}, {Duration} ms",
                run.Id, run.Trigger, run.CandidatesScanned, run.OrphansFound, run.NewOrphans, run.Reflagged, run.AlreadyKnown, run.ResolvedCount, run.DurationMs);
            return await ToRunDtoAsync(run, ct);
        }
        finally
        {
            _gate.End();
        }
    }

    private async Task ScanAsync(LocationRetentionRun run, LocationRetentionSettings settings, DateTime now, CancellationToken ct)
    {
        var createdBefore = now.AddDays(-Math.Max(0, settings.GracePeriodDays));
        var jsonReferences = await CollectJsonReferencesAsync(ct);

        var defaultName = LocationReferenceGraph.HasDefaultName();
        run.CandidatesScanned = await _context.Locations.AsNoTracking()
            .Where(defaultName)
            .Where(l => l.CreatedAt == null || l.CreatedAt < createdBefore)
            .CountAsync(ct);

        var found = new Dictionary<int, Location>();
        var orphan = LocationReferenceGraph.IsOrphan(_context, createdBefore);
        var batchSize = Math.Clamp(_options.BatchSize, 10, 5000);
        var afterId = 0;
        while (true)
        {
            var batch = await _context.Locations.AsNoTracking()
                .Where(orphan)
                .Where(l => l.Id > afterId)
                .OrderBy(l => l.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            foreach (var location in batch.Where(l => !jsonReferences.ContainsKey(l.Id)))
            {
                found[location.Id] = location;
            }
            if (batch.Count < batchSize) break;
            afterId = batch[^1].Id;
        }
        run.OrphansFound = found.Count;

        var active = await _context.LocationOrphans
            .Where(o => (o.Status == LocationOrphanStatus.Open || o.Status == LocationOrphanStatus.Kept) && o.SupersededByItemId == null)
            .ToListAsync(ct);
        var activeByLocation = active
            .GroupBy(o => o.LocationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.Id).First());

        var reflags = new List<(LocationOrphan Kept, LocationOrphan Reflag)>();
        foreach (var location in found.Values)
        {
            if (!activeByLocation.TryGetValue(location.Id, out var item))
            {
                _context.LocationOrphans.Add(NewItem(location, run, now, previous: null));
                run.NewOrphans++;
                continue;
            }

            if (item.Status == LocationOrphanStatus.Open)
            {
                item.LastSeenRunId = run.Id;
                item.LastSeenAt = now;
                run.AlreadyKnown++;
                continue;
            }

            // Kept: suppressed until the recheck period is over or the Location changed.
            var recheckDue = item.DecidedAt.HasValue && now >= item.DecidedAt.Value.AddMonths(Math.Max(1, settings.KeptRecheckMonths));
            if (recheckDue || ChangedSince(item, location))
            {
                var reflag = NewItem(location, run, now, previous: item);
                _context.LocationOrphans.Add(reflag);
                reflags.Add((item, reflag));
                run.NewOrphans++;
                run.Reflagged++;
            }
            else
            {
                run.AlreadyKnown++;
            }
        }

        // Open items whose Location is no longer an orphan resolve themselves.
        var noLongerOrphans = active.Where(o => o.Status == LocationOrphanStatus.Open && !found.ContainsKey(o.LocationId)).ToList();
        if (noLongerOrphans.Count > 0)
        {
            var ids = noLongerOrphans.Select(o => o.LocationId).Distinct().ToList();
            var current = await _context.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
            foreach (var item in noLongerOrphans)
            {
                item.Status = LocationOrphanStatus.Resolved;
                item.ResolvedReason = Truncate(ResolvedReasonFor(current.GetValueOrDefault(item.LocationId), jsonReferences), NoteMaxLength);
                item.DecidedAt = now;
                run.ResolvedCount++;
            }
        }

        await _context.SaveChangesAsync(ct);
        if (reflags.Count > 0)
        {
            foreach (var (kept, reflag) in reflags) kept.SupersededByItemId = reflag.Id;
            await _context.SaveChangesAsync(ct);
        }

        if (run.NewOrphans > 0)
        {
            // One digest per run, and only when something is new, so a growing backlog doesn't
            // re-ping staff every week (same idea as the currency alerts' dedup window).
            var openCount = await _context.LocationOrphans.CountAsync(o => o.Status == LocationOrphanStatus.Open, ct);
            try
            {
                _notifications.EnqueueLocationOrphanDigest(new LocationOrphanDigestNotificationDto
                {
                    RunId = run.Id,
                    NewCount = run.NewOrphans,
                    OpenCount = openCount
                });
                run.DigestQueuedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                // Best effort, like CurrencyAlertService.NotifyStaffAsync: the items are stored either way.
                _logger.LogWarning(ex, "Could not queue the Location orphan digest of run {RunId}", run.Id);
            }
        }
    }

    private static LocationOrphan NewItem(Location location, LocationRetentionRun run, DateTime now, LocationOrphan? previous) => new()
    {
        LocationId = location.Id,
        Status = LocationOrphanStatus.Open,
        FlaggedByRunId = run.Id,
        FlaggedAt = now,
        LastSeenRunId = run.Id,
        LastSeenAt = now,
        Name = location.Name,
        World = location.World,
        X = location.X,
        Y = location.Y,
        Z = location.Z,
        Yaw = location.Yaw,
        Pitch = location.Pitch,
        LocationCreatedAt = location.CreatedAt,
        PreviousItemId = previous?.Id
    };

    /// <summary>The Location differs from the item's snapshot (name, world or position).</summary>
    public static bool ChangedSince(LocationOrphan snapshot, Location location) =>
        !string.Equals(snapshot.Name ?? "", location.Name ?? "", StringComparison.Ordinal)
        || !string.Equals(snapshot.World ?? "", location.World ?? "", StringComparison.Ordinal)
        || Math.Abs(snapshot.X - location.X) > 1e-6
        || Math.Abs(snapshot.Y - location.Y) > 1e-6
        || Math.Abs(snapshot.Z - location.Z) > 1e-6
        || Math.Abs(snapshot.Yaw - location.Yaw) > 1e-4
        || Math.Abs(snapshot.Pitch - location.Pitch) > 1e-4;

    private static string ResolvedReasonFor(Location? location, IReadOnlyDictionary<int, string> jsonReferences)
    {
        if (location == null) return "The Location no longer exists (deleted outside this review).";
        if (!IsDefaultName(location.Name)) return $"It has a custom name now: \"{location.Name}\".";
        if (jsonReferences.TryGetValue(location.Id, out var where)) return $"It is referenced now: {where}.";
        return "Another record references it now.";
    }

    private static bool IsDefaultName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name == LocationReferenceGraph.DefaultName;

    private async Task<Dictionary<int, string>> CollectJsonReferencesAsync(CancellationToken ct)
    {
        var all = new Dictionary<int, string>();
        foreach (var source in _referenceSources)
        {
            foreach (var (id, where) in await source.FindReferencedLocationIdsAsync(ct))
            {
                all.TryAdd(id, where);
            }
        }
        return all;
    }

    // ===== Schedule =====

    public async Task<DateTime?> DueScheduledSlotAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        if (!settings.ScheduleEnabled) return null;

        var slot = LatestSlotUtc(settings, nowUtc, TimeZone());
        var lastSlot = await _context.LocationRetentionRuns.AsNoTracking()
            .Where(r => r.Trigger == "scheduled" && r.ScheduledSlotUtc != null)
            .MaxAsync(r => r.ScheduledSlotUtc, ct);
        return lastSlot.HasValue && lastSlot.Value >= slot ? null : slot;
    }

    /// <summary>The most recent schedule slot at or before <paramref name="nowUtc"/>, in UTC.</summary>
    public static DateTime LatestSlotUtc(LocationRetentionSettings settings, DateTime nowUtc, TimeZoneInfo zone)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var minutes = Math.Clamp(settings.RunAtMinuteOfDay, 0, 24 * 60 - 1);
        var weekly = settings.Frequency == LocationRetentionFrequency.Weekly;
        var daysBack = weekly ? ((int)localNow.DayOfWeek - (int)settings.RunDayOfWeek + 7) % 7 : 0;
        var candidate = DateTime.SpecifyKind(localNow.Date.AddDays(-daysBack).AddMinutes(minutes), DateTimeKind.Unspecified);
        if (candidate > localNow) candidate = candidate.AddDays(weekly ? -7 : -1);
        return ToUtc(candidate, zone);
    }

    public static DateTime NextSlotUtc(LocationRetentionSettings settings, DateTime nowUtc, TimeZoneInfo zone)
    {
        var latest = TimeZoneInfo.ConvertTimeFromUtc(LatestSlotUtc(settings, nowUtc, zone), zone);
        var next = DateTime.SpecifyKind(latest.AddDays(settings.Frequency == LocationRetentionFrequency.Weekly ? 7 : 1), DateTimeKind.Unspecified);
        return ToUtc(next, zone);
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        // A run time inside a DST gap (02:30 on the spring-forward night) moves an hour later.
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private TimeZoneInfo TimeZone()
    {
        if (!string.IsNullOrWhiteSpace(_options.TimeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                _logger.LogWarning("LocationRetention:TimeZoneId {Zone} is unknown; using the server's local time zone", _options.TimeZoneId);
            }
        }
        return TimeZoneInfo.Local;
    }

    // ===== Review =====

    public async Task<LocationOrphanPageDto> ListAsync(string status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.LocationOrphans.AsNoTracking();
        query = (status ?? "open").Trim().ToLowerInvariant() switch
        {
            "all" => query,
            "kept" => query.Where(o => o.Status == LocationOrphanStatus.Kept),
            "deleted" => query.Where(o => o.Status == LocationOrphanStatus.Deleted),
            "resolved" => query.Where(o => o.Status == LocationOrphanStatus.Resolved),
            _ => query.Where(o => o.Status == LocationOrphanStatus.Open)
        };

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(o => o.FlaggedAt).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new LocationOrphanPageDto
        {
            Items = await ToDtosAsync(items, ct),
            TotalCount = total,
            PageNumber = page,
            PageSize = pageSize,
            OpenCount = await _context.LocationOrphans.CountAsync(o => o.Status == LocationOrphanStatus.Open, ct),
            KeptCount = await _context.LocationOrphans.CountAsync(o => o.Status == LocationOrphanStatus.Kept && o.SupersededByItemId == null, ct)
        };
    }

    public async Task<LocationOrphanDto> KeepAsync(int itemId, int actorUserId, string? note, CancellationToken ct = default)
    {
        var item = await _context.LocationOrphans.FirstOrDefaultAsync(o => o.Id == itemId, ct)
            ?? throw new KeyNotFoundException($"Orphan review item {itemId} not found.");
        if (item.Status != LocationOrphanStatus.Open)
            throw new InvalidOperationException($"Only an open item can be kept; this one is {item.Status}.");

        item.Status = LocationOrphanStatus.Kept;
        item.DecidedByUserId = actorUserId;
        item.DecidedAt = DateTime.UtcNow;
        item.DecisionNote = CleanNote(note);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Orphaned Location {LocationId} kept by user {UserId} (item {ItemId})", item.LocationId, actorUserId, item.Id);
        return (await ToDtosAsync(new List<LocationOrphan> { item }, ct))[0];
    }

    public async Task<LocationOrphanDeleteResultDto> DeleteAsync(int itemId, int actorUserId, string? note, CancellationToken ct = default)
    {
        var item = await _context.LocationOrphans.FirstOrDefaultAsync(o => o.Id == itemId, ct)
            ?? throw new KeyNotFoundException($"Orphan review item {itemId} not found.");
        if (item.Status != LocationOrphanStatus.Open)
            throw new InvalidOperationException($"Only an open item can be deleted; this one is {item.Status}.");

        var relational = _context.Database.IsRelational();
        await using var transaction = relational && _context.Database.CurrentTransaction == null
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;

        string outcome;
        string message;
        try
        {
            if (relational)
            {
                // Lock the Location row first: an insert or update that would reference it now waits
                // for this transaction (InnoDB checks a new FK against the parent row), and the
                // re-check below sees everything committed before the lock.
#pragma warning disable EF1002 // an int and a table name from the model, nothing user-supplied
                var table = _context.Model.FindEntityType(typeof(Location))!.GetTableName();
                await _context.Database.ExecuteSqlRawAsync($"SELECT Id FROM `{table}` WHERE Id = {item.LocationId} FOR UPDATE", ct);
#pragma warning restore EF1002
            }

            var location = await _context.Locations.FirstOrDefaultAsync(l => l.Id == item.LocationId, ct);
            var jsonReferences = await CollectJsonReferencesAsync(ct);
            var stillOrphan = location != null
                && !jsonReferences.ContainsKey(location.Id)
                && await _context.Locations.Where(LocationReferenceGraph.IsOrphan(_context, createdBefore: null)).AnyAsync(l => l.Id == location.Id, ct);

            if (!stillOrphan)
            {
                item.Status = LocationOrphanStatus.Resolved;
                item.ResolvedReason = Truncate("Not deleted: " + ResolvedReasonFor(location, jsonReferences), NoteMaxLength);
                item.DecidedByUserId = actorUserId;
                item.DecidedAt = DateTime.UtcNow;
                outcome = "NoLongerOrphan";
                message = item.ResolvedReason!;
            }
            else
            {
                _context.Locations.Remove(location!);
                item.Status = LocationOrphanStatus.Deleted;
                item.DecidedByUserId = actorUserId;
                item.DecidedAt = DateTime.UtcNow;
                item.DecisionNote = CleanNote(note);
                outcome = "Deleted";
                message = $"Location {item.LocationId} deleted.";
            }

            await _context.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A restricting FK the re-check could not see (or a row added in between) refused the
            // delete: record that instead of failing.
            if (transaction != null) await transaction.RollbackAsync(ct);
            _context.ChangeTracker.Clear();
            item = await _context.LocationOrphans.FirstAsync(o => o.Id == itemId, ct);
            item.Status = LocationOrphanStatus.Resolved;
            item.ResolvedReason = "Not deleted: the database refused it because another record references the Location.";
            item.DecidedByUserId = actorUserId;
            item.DecidedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            _logger.LogWarning(ex, "Delete of orphaned Location {LocationId} refused by the database", item.LocationId);
            outcome = "NoLongerOrphan";
            message = item.ResolvedReason;
        }

        if (outcome == "Deleted")
        {
            // The item row (status Deleted, who, when, note, snapshot) is the audit record; the log
            // line is the second trail.
            _logger.LogWarning(
                "Orphaned Location {LocationId} deleted by user {UserId} (item {ItemId}; was {World} {X:0.##} {Y:0.##} {Z:0.##}, name {Name})",
                item.LocationId, actorUserId, item.Id, item.World, item.X, item.Y, item.Z, item.Name);
        }

        return new LocationOrphanDeleteResultDto
        {
            Outcome = outcome,
            Message = message,
            Item = (await ToDtosAsync(new List<LocationOrphan> { item }, ct))[0]
        };
    }

    // ===== Status and settings =====

    public async Task<LocationRetentionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        var lastRun = await _context.LocationRetentionRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);
        return new LocationRetentionStatusDto
        {
            Settings = await ToSettingsDtoAsync(settings, ct),
            LastRun = lastRun == null ? null : await ToRunDtoAsync(lastRun, ct),
            NextScheduledRunAt = settings.ScheduleEnabled ? NextSlotUtc(settings, DateTime.UtcNow, TimeZone()) : null,
            Running = _gate.Running,
            Relations = LocationReferenceGraph.Describe(_context.Model).Select(r => r.Describe()).ToList(),
            OtherReferenceSources = new List<string>
            {
                "Game settings (join spawn, default respawn, per-world spawn/respawn)",
                "Unfinished form drafts (InProgress/Paused)"
            }
        };
    }

    public async Task<LocationRetentionSettingsDto> UpdateSettingsAsync(LocationRetentionSettingsDto dto, int? actorUserId, CancellationToken ct = default)
    {
        if (dto == null) throw new ArgumentException("Settings are required.");
        if (!Enum.TryParse<LocationRetentionFrequency>(dto.Frequency?.Trim(), ignoreCase: true, out var frequency) || !Enum.IsDefined(frequency))
            throw new ArgumentException("frequency must be Daily or Weekly.");
        if (!Enum.TryParse<DayOfWeek>(dto.RunDayOfWeek?.Trim(), ignoreCase: true, out var day) || !Enum.IsDefined(day))
            throw new ArgumentException("runDayOfWeek must be a day name, e.g. Sunday.");
        if (!TimeOnly.TryParseExact(dto.RunAtTime?.Trim(), new[] { "HH:mm", "H:mm" }, out var time))
            throw new ArgumentException("runAtTime must be HH:mm, e.g. 04:00.");
        if (dto.GracePeriodDays is < 0 or > 365)
            throw new ArgumentException("gracePeriodDays must be 0–365.");
        if (dto.KeptRecheckMonths is < 1 or > 120)
            throw new ArgumentException("keptRecheckMonths must be 1–120.");

        var settings = await GetOrCreateSettingsAsync(ct);
        settings.ScheduleEnabled = dto.ScheduleEnabled;
        settings.Frequency = frequency;
        settings.RunDayOfWeek = day;
        settings.RunAtMinuteOfDay = time.Hour * 60 + time.Minute;
        settings.GracePeriodDays = dto.GracePeriodDays;
        settings.KeptRecheckMonths = dto.KeptRecheckMonths;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = actorUserId;
        await _context.SaveChangesAsync(ct);
        return await ToSettingsDtoAsync(settings, ct);
    }

    public async Task<LocationTeleportTargetDto?> GetTeleportTargetAsync(int locationId, CancellationToken ct = default)
    {
        var location = await _context.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
        return location == null ? null : new LocationTeleportTargetDto
        {
            Id = location.Id,
            Name = location.Name,
            World = location.World,
            X = location.X,
            Y = location.Y,
            Z = location.Z,
            Yaw = location.Yaw,
            Pitch = location.Pitch
        };
    }

    private async Task<LocationRetentionSettings> GetOrCreateSettingsAsync(CancellationToken ct)
    {
        var settings = await _context.LocationRetentionSettings.FirstOrDefaultAsync(s => s.Id == "global", ct);
        if (settings != null) return settings;
        settings = new LocationRetentionSettings();
        _context.LocationRetentionSettings.Add(settings);
        try
        {
            await _context.SaveChangesAsync(ct);
            return settings;
        }
        catch (DbUpdateException)
        {
            // Another request created it first.
            _context.Entry(settings).State = EntityState.Detached;
            return await _context.LocationRetentionSettings.FirstAsync(s => s.Id == "global", ct);
        }
    }

    // ===== Mapping =====

    private async Task<List<LocationOrphanDto>> ToDtosAsync(List<LocationOrphan> items, CancellationToken ct)
    {
        var previousIds = items.Where(i => i.PreviousItemId.HasValue).Select(i => i.PreviousItemId!.Value).Distinct().ToList();
        var previous = previousIds.Count == 0
            ? new Dictionary<int, LocationOrphan>()
            : await _context.LocationOrphans.AsNoTracking().Where(o => previousIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var userIds = items.Select(i => i.DecidedByUserId).Concat(previous.Values.Select(p => p.DecidedByUserId))
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var usernames = await UsernamesAsync(userIds, ct);
        var locationIds = items.Select(i => i.LocationId).Distinct().ToList();
        var existing = (await _context.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct)).ToHashSet();

        return items.Select(i => new LocationOrphanDto
        {
            Id = i.Id,
            LocationId = i.LocationId,
            Status = i.Status.ToString(),
            FlaggedAt = i.FlaggedAt,
            FlaggedByRunId = i.FlaggedByRunId,
            LastSeenAt = i.LastSeenAt,
            Name = i.Name,
            World = i.World,
            X = i.X,
            Y = i.Y,
            Z = i.Z,
            Yaw = i.Yaw,
            Pitch = i.Pitch,
            LocationCreatedAt = i.LocationCreatedAt,
            LocationExists = existing.Contains(i.LocationId),
            DecidedByUserId = i.DecidedByUserId,
            DecidedByUsername = i.DecidedByUserId.HasValue ? usernames.GetValueOrDefault(i.DecidedByUserId.Value) : null,
            DecidedAt = i.DecidedAt,
            DecisionNote = i.DecisionNote,
            ResolvedReason = i.ResolvedReason,
            PreviousDecision = i.PreviousItemId.HasValue && previous.TryGetValue(i.PreviousItemId.Value, out var p)
                ? new LocationOrphanPreviousDecisionDto
                {
                    ItemId = p.Id,
                    Status = p.Status.ToString(),
                    DecidedByUserId = p.DecidedByUserId,
                    DecidedByUsername = p.DecidedByUserId.HasValue ? usernames.GetValueOrDefault(p.DecidedByUserId.Value) : null,
                    DecidedAt = p.DecidedAt,
                    DecisionNote = p.DecisionNote
                }
                : null
        }).ToList();
    }

    private async Task<LocationRetentionRunDto> ToRunDtoAsync(LocationRetentionRun run, CancellationToken ct)
    {
        var usernames = run.TriggeredByUserId.HasValue
            ? await UsernamesAsync(new List<int> { run.TriggeredByUserId.Value }, ct)
            : new Dictionary<int, string>();
        return new LocationRetentionRunDto
        {
            Id = run.Id,
            Trigger = run.Trigger,
            TriggeredByUserId = run.TriggeredByUserId,
            TriggeredByUsername = run.TriggeredByUserId.HasValue ? usernames.GetValueOrDefault(run.TriggeredByUserId.Value) : null,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Succeeded = run.Succeeded,
            Error = run.Error,
            CandidatesScanned = run.CandidatesScanned,
            OrphansFound = run.OrphansFound,
            NewOrphans = run.NewOrphans,
            AlreadyKnown = run.AlreadyKnown,
            Reflagged = run.Reflagged,
            Resolved = run.ResolvedCount,
            DurationMs = run.DurationMs,
            DigestQueuedAt = run.DigestQueuedAt
        };
    }

    private async Task<LocationRetentionSettingsDto> ToSettingsDtoAsync(LocationRetentionSettings settings, CancellationToken ct)
    {
        var usernames = settings.UpdatedByUserId.HasValue
            ? await UsernamesAsync(new List<int> { settings.UpdatedByUserId.Value }, ct)
            : new Dictionary<int, string>();
        return new LocationRetentionSettingsDto
        {
            ScheduleEnabled = settings.ScheduleEnabled,
            Frequency = settings.Frequency.ToString(),
            RunDayOfWeek = settings.RunDayOfWeek.ToString(),
            RunAtTime = $"{settings.RunAtMinuteOfDay / 60:00}:{settings.RunAtMinuteOfDay % 60:00}",
            GracePeriodDays = settings.GracePeriodDays,
            KeptRecheckMonths = settings.KeptRecheckMonths,
            TimeZone = TimeZone().Id,
            UpdatedAt = settings.UpdatedAt,
            UpdatedByUsername = settings.UpdatedByUserId.HasValue ? usernames.GetValueOrDefault(settings.UpdatedByUserId.Value) : null
        };
    }

    private async Task<Dictionary<int, string>> UsernamesAsync(List<int> userIds, CancellationToken ct) =>
        userIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);

    private static string? CleanNote(string? note)
    {
        var trimmed = note?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : Truncate(trimmed, NoteMaxLength);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Spawn, claim, give and deliver (docs/specs/lootboxes/DESIGN.md §3.1, §3.3; IMPLEMENTATION_PLAN.md Phase 2).
    /// <para>
    /// A claim runs in one READ COMMITTED transaction: lock the claimer's user row (serializes one player's claims,
    /// so two parallel clicks can't both slip under the daily cap), replay a stored claim for the same idempotency
    /// key, check token and status, count today's claims, roll with the same input as the odds preview, flip the
    /// spawn to Claimed (its <c>[ConcurrencyCheck]</c> Status makes a racing second claimer fail), then insert the
    /// ItemInstance and the claim, and finally point the instance's OriginRef at the claim. The unique indexes on
    /// LootboxClaim (spawn, instance, idempotency key) back this up on MySQL.
    /// </para>
    /// <para>
    /// Times come from the injected <see cref="TimeProvider"/> and are cut to whole seconds, because the columns are
    /// MySQL <c>datetime</c> (which would round 23:59:59.7 into the next day). The daily cap counts the UTC calendar
    /// day [00:00, 24:00).
    /// </para>
    /// </summary>
    public class LootboxRuntimeService : ILootboxRuntimeService
    {
        private const string FallbackDisplayMaterial = "minecraft:chest";

        private readonly ILootboxRuntimeRepository _repo;
        private readonly ILootboxTypeService _types;
        private readonly IItemInstanceService _instances;
        private readonly IUserRepository _users;
        private readonly IAuditLogService _audit;
        private readonly LootboxRollEngine _engine;
        private readonly ILootRandom _random;
        private readonly TimeProvider _time;
        private readonly ILogger<LootboxRuntimeService> _logger;

        public LootboxRuntimeService(
            ILootboxRuntimeRepository repo,
            ILootboxTypeService types,
            IItemInstanceService instances,
            IUserRepository users,
            IAuditLogService audit,
            LootboxRollEngine engine,
            ILootRandom random,
            TimeProvider time,
            ILogger<LootboxRuntimeService> logger)
        {
            _repo = repo;
            _types = types;
            _instances = instances;
            _users = users;
            _audit = audit;
            _engine = engine;
            _random = random;
            _time = time;
            _logger = logger;
        }

        // ===== Reads =====

        public async Task<LootboxRuntimeConfigDto> GetRuntimeConfigAsync()
        {
            var now = Now();
            await _repo.ExpireDueAsync(now);

            var config = await _repo.GetConfigurationAsync() ?? new LootboxConfiguration();
            var types = await _repo.GetEnabledTypesAsync();
            var grades = await _repo.GetGradesAsync();
            var areas = await _repo.GetAreasAsync();
            var active = await _repo.CountActiveByAreaAsync();

            return new LootboxRuntimeConfigDto
            {
                Enabled = config.Enabled,
                GlobalMaxActive = config.GlobalMaxActive,
                MaxClaimsPerPlayerPerDay = config.MaxClaimsPerPlayerPerDay,
                AnnounceMinItemStars = config.AnnounceMinItemStars,
                AnnounceSpawnMinBoxStars = config.AnnounceSpawnMinBoxStars,
                DropAnnouncementTemplate = config.DropAnnouncementTemplate,
                SpawnAnnouncementTemplate = config.SpawnAnnouncementTemplate,
                ServerTimeUtc = now,
                Types = types.Select(t => new LootboxRuntimeTypeDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    CategoryId = t.CategoryId,
                    CategoryName = t.Category?.Name ?? string.Empty,
                    DisplayMaterialKey = t.DisplayMaterial?.NamespaceKey ?? t.Category?.IconMaterialRef?.NamespaceKey ?? FallbackDisplayMaterial,
                    SpawnWeight = t.SpawnWeight,
                    MinBoxStars = t.MinBoxStars,
                    MaxBoxStars = t.MaxBoxStars,
                    MaxClaimsPerPlayerPerDay = t.MaxClaimsPerPlayerPerDay,
                    AnnounceMinItemStars = t.AnnounceMinItemStars,
                }).ToList(),
                Grades = grades.Select(g => new LootboxRuntimeGradeDto { Id = g.Id, Name = g.Name, Stars = g.Stars }).ToList(),
                Areas = areas.Select(a => new LootboxRuntimeAreaDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    World = a.World,
                    WgRegionId = a.WgRegionId,
                    Enabled = a.Enabled,
                    MaxActive = a.MaxActive,
                    SpawnIntervalSeconds = a.SpawnIntervalSeconds,
                    SpawnChancePercent = a.SpawnChancePercent,
                    MinOnlinePlayers = a.MinOnlinePlayers,
                    MinDistanceFromPlayers = a.MinDistanceFromPlayers,
                    LifetimeMinutes = a.LifetimeMinutes,
                    ExcludedRegionIds = (a.ExcludedRegionIds ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                    AllowedTypeIds = a.AllowedTypes.Select(t => t.LootboxTypeId).OrderBy(id => id).ToList(),
                    ActiveCount = active.GetValueOrDefault(a.Id),
                    CreatedByUserId = a.CreatedByUserId,
                }).ToList(),
            };
        }

        public async Task<List<LootboxSpawnDto>> GetActiveAsync()
        {
            await _repo.ExpireDueAsync(Now());
            return (await _repo.GetActiveSpawnsAsync()).Select(ToSpawnDto).ToList();
        }

        public async Task<LootboxClaimResultDto?> GetClaimAsync(int claimId)
        {
            if (claimId <= 0) return null;
            return await ResultAsync(claimId, replay: false);
        }

        public async Task<List<LootboxClaimResultDto>> GetPendingAsync(int userId)
        {
            if (userId <= 0) throw new ArgumentException("userId is required.", nameof(userId));
            var cutoff = Now().AddSeconds(-LootboxRuntimeServiceConstants.PendingGraceSeconds);
            var claims = await _repo.GetPendingAsync(userId, cutoff);
            if (claims.Count == 0) return new List<LootboxClaimResultDto>();

            var config = await _repo.GetConfigurationAsync() ?? new LootboxConfiguration();
            return claims.Select(c => ToResult(c, config, replay: false)).ToList();
        }

        public async Task<PagedResultDto<LootboxClaimLogDto>> SearchClaimsAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var query = new PagedQuery
            {
                PageNumber = Math.Max(1, queryDto.PageNumber),
                PageSize = Math.Clamp(queryDto.PageSize, 1, 200),
                SearchTerm = queryDto.SearchTerm,
                SortBy = queryDto.SortBy,
                SortDescending = queryDto.SortDescending,
                Filters = queryDto.Filters,
            };
            var result = await _repo.SearchClaimsAsync(query);
            return new PagedResultDto<LootboxClaimLogDto>
            {
                Items = result.Items.Select(c => new LootboxClaimLogDto
                {
                    Id = c.Id,
                    LootboxSpawnId = c.LootboxSpawnId,
                    IsAdminGive = c.LootboxSpawnId == null,
                    UserId = c.UserId,
                    Username = c.User?.Username,
                    LootboxTypeId = c.LootboxTypeId,
                    LootboxTypeName = c.LootboxType?.Name,
                    BoxGradeId = c.BoxGradeId,
                    BoxStars = c.BoxGrade?.Stars,
                    ItemBlueprintId = c.ItemBlueprintId,
                    ItemName = ItemName(c.ItemBlueprint),
                    ItemGradeId = c.ItemGradeId,
                    ItemStars = c.ItemGrade?.Stars,
                    Quantity = c.Quantity,
                    IsSpecial = c.IsSpecial,
                    ItemInstanceId = c.ItemInstanceId,
                    ClaimedAt = Utc(c.ClaimedAt),
                    DeliveredAt = Utc(c.DeliveredAt),
                    DeliveryMethod = c.DeliveryMethod?.ToString(),
                    DeliveryNote = c.DeliveryNote,
                }).ToList(),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize,
            };
        }

        // ===== Spawns =====

        public async Task<LootboxSpawnDto> SpawnAsync(LootboxSpawnRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.AreaId <= 0) throw new ArgumentException("areaId is required.", nameof(request));
            ValidatePosition(request.World, request.X, request.Y, request.Z, request.ServerId);

            var now = Now();
            await _repo.ExpireDueAsync(now);

            var spawn = await _repo.InTransactionAsync(async () =>
            {
                // One spawn decision at a time, so two requests can't both take the last free slot.
                await _repo.LockConfigurationAsync();

                var config = await _repo.GetConfigurationAsync() ?? new LootboxConfiguration();
                if (!config.Enabled) throw Conflict("Disabled", "Lootboxes are disabled.");

                var area = await _repo.GetAreaAsync(request.AreaId)
                    ?? throw new KeyNotFoundException($"Lootbox spawn area {request.AreaId} not found.");
                if (!area.Enabled) throw Conflict("Disabled", $"Spawn area '{area.Name}' is disabled.");
                if (!string.Equals(area.World, request.World.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Spawn area '{area.Name}' is in world '{area.World}', not '{request.World}'.", nameof(request));

                if (await _repo.CountActiveAsync(area.Id) >= area.MaxActive)
                    throw Conflict("AreaFull", $"Spawn area '{area.Name}' already has {area.MaxActive} active boxes.");
                if (await _repo.CountActiveAsync() >= config.GlobalMaxActive)
                    throw Conflict("GlobalFull", $"There are already {config.GlobalMaxActive} active boxes.");

                var allowed = area.AllowedTypes.Select(t => t.LootboxTypeId).ToHashSet();
                var candidates = (await _repo.GetEnabledTypesAsync())
                    .Where(t => t.SpawnWeight > 0 && (allowed.Count == 0 || allowed.Contains(t.Id)))
                    .OrderBy(t => t.Id)
                    .ToList();
                if (candidates.Count == 0)
                    throw Conflict("NoEnabledType", $"No enabled lootbox type may spawn in '{area.Name}'.");

                var type = PickType(candidates);
                var boxGrade = await RollBoxGradeAsync(type)
                    ?? throw Conflict("NoBoxGrade", $"'{type.Name}' has no box grade with weight in ★{type.MinBoxStars}-{type.MaxBoxStars}.");

                var created = new LootboxSpawn
                {
                    Token = Guid.NewGuid(),
                    LootboxTypeId = type.Id,
                    BoxGradeId = boxGrade.Id,
                    SpawnAreaId = area.Id,
                    World = area.World,
                    X = request.X,
                    Y = request.Y,
                    Z = request.Z,
                    Status = LootboxSpawnStatus.Active,
                    SpawnedAt = now,
                    ExpiresAt = now.AddMinutes(Math.Max(1, area.LifetimeMinutes)),
                    ServerId = Blank(request.ServerId),
                };
                await _repo.AddSpawnAsync(created);
                return created;
            });

            var dto = await SpawnDtoAsync(spawn.Id);
            LootboxMetrics.Spawned(dto.LootboxTypeName, dto.BoxStars);
            _logger.LogInformation("Lootbox spawn {SpawnId}: {Type} ★{Stars} in area {AreaId} at {World} {X} {Y} {Z}",
                dto.Id, dto.LootboxTypeName, dto.BoxStars, dto.SpawnAreaId, dto.World, dto.X, dto.Y, dto.Z);
            return dto;
        }

        public async Task<LootboxSpawnDto> AdminSpawnAsync(LootboxAdminSpawnRequestDto request, int? actorUserId)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.TypeId <= 0) throw new ArgumentException("typeId is required.", nameof(request));
            ValidatePosition(request.World, request.X, request.Y, request.Z, request.ServerId);
            var lifetime = request.LifetimeMinutes ?? LootboxRuntimeServiceConstants.DefaultLifetimeMinutes;
            if (lifetime < 1 || lifetime > LootboxRuntimeServiceConstants.MaxLifetimeMinutes)
                throw new ArgumentException($"lifetimeMinutes must be 1-{LootboxRuntimeServiceConstants.MaxLifetimeMinutes}.", nameof(request));

            var now = Now();
            var actor = await ExistingUserIdAsync(actorUserId);
            var type = await _repo.GetTypeAsync(request.TypeId)
                ?? throw new KeyNotFoundException($"LootboxType {request.TypeId} not found.");
            var boxGrade = await BoxGradeAsync(type, request.BoxStars);
            await EnsureHasLootAsync(type, boxGrade.Stars);

            var spawn = new LootboxSpawn
            {
                Token = Guid.NewGuid(),
                LootboxTypeId = type.Id,
                BoxGradeId = boxGrade.Id,
                SpawnAreaId = null,
                World = request.World.Trim(),
                X = request.X,
                Y = request.Y,
                Z = request.Z,
                Status = LootboxSpawnStatus.Active,
                SpawnedAt = now,
                ExpiresAt = now.AddMinutes(lifetime),
                ServerId = Blank(request.ServerId),
                CreatedByUserId = actor,
            };
            await _repo.InTransactionAsync(async () =>
            {
                await _repo.AddSpawnAsync(spawn);
                await AuditAsync(actor, null, AuditAction.LootboxSpawnedByAdmin, new
                {
                    @event = "Spawned",
                    spawnId = spawn.Id,
                    lootboxTypeId = type.Id,
                    lootboxTypeName = type.Name,
                    boxStars = boxGrade.Stars,
                    world = spawn.World,
                    x = spawn.X,
                    y = spawn.Y,
                    z = spawn.Z,
                });
                return spawn.Id;
            });

            var dto = await SpawnDtoAsync(spawn.Id);
            LootboxMetrics.Spawned(dto.LootboxTypeName, dto.BoxStars);
            _logger.LogInformation("Lootbox admin spawn {SpawnId} by user {ActorUserId}: {Type} ★{Stars} at {World} {X} {Y} {Z}",
                dto.Id, actor, dto.LootboxTypeName, dto.BoxStars, dto.World, dto.X, dto.Y, dto.Z);
            return dto;
        }

        public async Task<LootboxSpawnDto> DespawnAsync(int spawnId, int? actorUserId)
        {
            if (spawnId <= 0) throw new ArgumentException("Invalid spawn id.", nameof(spawnId));
            await _repo.ExpireDueAsync(Now());

            var spawn = await _repo.GetSpawnAsync(spawnId)
                ?? throw new KeyNotFoundException($"Lootbox spawn {spawnId} not found.");
            if (spawn.Status == LootboxSpawnStatus.Active)
            {
                spawn.Status = LootboxSpawnStatus.Removed;
                try
                {
                    await _repo.SaveChangesAsync();
                    _logger.LogInformation("Lootbox spawn {SpawnId} removed by user {ActorUserId}", spawnId, actorUserId);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Claimed or expired in the meantime: report what it is now.
                    _repo.DiscardChanges();
                }
            }
            return await SpawnDtoAsync(spawnId);
        }

        // ===== Claims =====

        public async Task<LootboxClaimResultDto> ClaimAsync(int spawnId, LootboxClaimRequestDto request)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return await ClaimCoreAsync(spawnId, request);
            }
            catch (LootboxConflictException ex)
            {
                LootboxMetrics.Conflict(ex.Code);
                _logger.LogInformation("Lootbox claim of spawn {SpawnId} by user {UserId} refused: {Code}",
                    spawnId, request?.UserId, ex.Code);
                throw;
            }
            finally
            {
                LootboxMetrics.ClaimTook(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private sealed record ClaimOutcome(int ClaimId, bool Replay);

        private async Task<LootboxClaimResultDto> ClaimCoreAsync(int spawnId, LootboxClaimRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (spawnId <= 0) throw new ArgumentException("Invalid spawn id.", nameof(spawnId));
            if (request.UserId <= 0) throw new ArgumentException("userId is required.", nameof(request));
            var key = IdempotencyKey(request.IdempotencyKey)
                ?? throw new ArgumentException(
                    $"idempotencyKey is required (at most {LootboxRuntimeServiceConstants.MaxIdempotencyKeyLength} characters).", nameof(request));

            var now = Now();
            await _repo.ExpireDueAsync(now);

            // Fast path for a retry: no lock needed to hand back a stored result.
            var stored = await _repo.GetClaimByIdempotencyKeyAsync(key);
            if (stored != null) return await ReplayAsync(stored, request.UserId, spawnId, null);

            var user = await _users.GetByIdAsync(request.UserId)
                ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
            EnsureCanOpen(user);

            ClaimOutcome outcome;
            try
            {
                outcome = await _repo.InTransactionAsync(async () =>
                {
                    // Serializes this player's claims: the replay lookup and the daily count below see whatever a
                    // parallel claim of theirs committed.
                    await _users.LockUsersAsync(new[] { request.UserId });

                    var again = await _repo.GetClaimByIdempotencyKeyAsync(key);
                    if (again != null)
                    {
                        EnsureSameClaim(again, request.UserId, spawnId, null);
                        return new ClaimOutcome(again.Id, true);
                    }

                    var spawn = await _repo.GetSpawnAsync(spawnId)
                        ?? throw new KeyNotFoundException($"Lootbox spawn {spawnId} not found.");
                    // The token first: without it a caller learns nothing about the box.
                    if (spawn.Token != request.Token) throw Conflict("TokenMismatch", "The lootbox token doesn't match.");
                    EnsureClaimable(spawn.Status, spawn.ExpiresAt, now);

                    var config = await _repo.GetConfigurationAsync() ?? new LootboxConfiguration();
                    if (!config.Enabled) throw Conflict("Disabled", "Lootboxes are disabled.");
                    await EnforceDailyCapAsync(request.UserId, spawn.LootboxType, config, now);

                    var roll = await RollAsync(spawn.LootboxTypeId, spawn.BoxGrade.Stars);

                    // Flip the box first, on its own: a racing claimer's UPDATE … WHERE Status='Active' matches no
                    // row and fails here, before anything else is written.
                    spawn.Status = LootboxSpawnStatus.Claimed;
                    spawn.ClaimedAt = now;
                    spawn.ClaimedByUserId = request.UserId;
                    await _repo.SaveChangesAsync();

                    var claim = await MintAsync(roll, request.UserId, spawn.LootboxTypeId, spawn.BoxGradeId, spawn.Id, key, now);
                    return new ClaimOutcome(claim.Id, false);
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                _repo.DiscardChanges();
                var status = await _repo.GetSpawnStatusAsync(spawnId);
                throw status switch
                {
                    LootboxSpawnStatus.Expired => Conflict("Expired", "This lootbox has expired."),
                    LootboxSpawnStatus.Removed => Conflict("Removed", "This lootbox was removed."),
                    _ => Conflict("AlreadyClaimed", "Someone else got there first."),
                };
            }
            catch (DbUpdateException ex)
            {
                // A unique index caught what the checks above didn't (e.g. the same key in two parallel requests).
                _repo.DiscardChanges();
                var winner = await _repo.GetClaimByIdempotencyKeyAsync(key);
                if (winner != null) return await ReplayAsync(winner, request.UserId, spawnId, null);
                if (await _repo.SpawnHasClaimAsync(spawnId)) throw Conflict("AlreadyClaimed", "Someone else got there first.");
                _logger.LogError(ex, "Lootbox claim of spawn {SpawnId} by user {UserId} failed to save", spawnId, request.UserId);
                throw;
            }

            var result = await ResultAsync(outcome.ClaimId, outcome.Replay)
                ?? throw new InvalidOperationException($"Lootbox claim {outcome.ClaimId} vanished.");
            if (outcome.Replay)
            {
                _logger.LogInformation("Lootbox claim {ClaimId} replayed for user {UserId}", result.ClaimId, result.UserId);
            }
            else
            {
                LootboxMetrics.Claimed(TypeLabel(result), result.BoxStars, result.ItemGradeStars, result.IsSpecial);
                _logger.LogInformation(
                    "Lootbox claim {ClaimId}: user {UserId} opened spawn {SpawnId} (★{BoxStars}) and got blueprint {BlueprintId} ★{ItemStars} x{Quantity}, instance {InstanceId}, special {IsSpecial}",
                    result.ClaimId, result.UserId, spawnId, result.BoxStars, result.ItemBlueprintId, result.ItemGradeStars,
                    result.Quantity, result.ItemInstanceId, result.IsSpecial);
            }
            return result;
        }

        public async Task<LootboxClaimResultDto> AdminGiveAsync(LootboxAdminGiveRequestDto request, int? actorUserId)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.UserId <= 0) throw new ArgumentException("userId is required.", nameof(request));
            if (request.TypeId <= 0) throw new ArgumentException("typeId is required.", nameof(request));
            var givenKey = IdempotencyKey(request.IdempotencyKey);
            if (givenKey == null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
                throw new ArgumentException($"idempotencyKey can be at most {LootboxRuntimeServiceConstants.MaxIdempotencyKeyLength} characters.", nameof(request));

            if (givenKey != null)
            {
                var stored = await _repo.GetClaimByIdempotencyKeyAsync(givenKey);
                if (stored != null) return await ReplayAsync(stored, request.UserId, null, request.TypeId);
            }

            _ = await _users.GetByIdAsync(request.UserId) ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
            var actor = await ExistingUserIdAsync(actorUserId);
            var type = await _repo.GetTypeAsync(request.TypeId)
                ?? throw new KeyNotFoundException($"LootboxType {request.TypeId} not found.");
            var boxGrade = await BoxGradeAsync(type, request.BoxStars);
            var key = givenKey ?? $"admin-give:{Guid.NewGuid():N}";
            var now = Now();

            int claimId;
            try
            {
                claimId = await _repo.InTransactionAsync(async () =>
                {
                    var roll = await RollAsync(type.Id, boxGrade.Stars);
                    var claim = await MintAsync(roll, request.UserId, type.Id, boxGrade.Id, null, key, now);
                    await AuditAsync(actor, request.UserId, AuditAction.LootboxGranted, new
                    {
                        claimId = claim.Id,
                        lootboxTypeId = type.Id,
                        lootboxTypeName = type.Name,
                        boxStars = boxGrade.Stars,
                        itemBlueprintId = claim.ItemBlueprintId,
                        itemName = roll.Item.Name,
                        itemStars = roll.ItemGrade?.Stars,
                        isSpecial = claim.IsSpecial,
                        itemInstanceId = claim.ItemInstanceId,
                    });
                    return claim.Id;
                });
            }
            catch (DbUpdateException) when (givenKey != null)
            {
                _repo.DiscardChanges();
                var winner = await _repo.GetClaimByIdempotencyKeyAsync(givenKey);
                if (winner == null) throw;
                return await ReplayAsync(winner, request.UserId, null, request.TypeId);
            }

            var result = await ResultAsync(claimId, replay: false)
                ?? throw new InvalidOperationException($"Lootbox claim {claimId} vanished.");
            _logger.LogInformation("Lootbox admin give {ClaimId}: user {ActorUserId} gave user {UserId} a {Type} ★{BoxStars} → blueprint {BlueprintId}",
                claimId, actor, request.UserId, type.Name, boxGrade.Stars, result.ItemBlueprintId);
            return result;
        }

        public async Task<LootboxDeliveredResultDto> MarkDeliveredAsync(int claimId, LootboxDeliveredRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (claimId <= 0) throw new ArgumentException("Invalid claim id.", nameof(claimId));
            if (!Enum.TryParse<LootboxDeliveryMethod>(request.Method, true, out var method) || !Enum.IsDefined(method))
                throw new ArgumentException("method must be Inventory, DroppedOwned or Redelivered.", nameof(request));

            var claim = await _repo.GetClaimForUpdateAsync(claimId)
                ?? throw new KeyNotFoundException($"Lootbox claim {claimId} not found.");
            if (request.UserId is int userId && userId != claim.UserId)
                throw Conflict("WrongUser", $"Lootbox claim {claimId} belongs to another user.");

            if (claim.DeliveredAt is DateTime delivered)
            {
                return new LootboxDeliveredResultDto
                {
                    ClaimId = claim.Id,
                    DeliveredAt = Utc(delivered),
                    DeliveryMethod = (claim.DeliveryMethod ?? method).ToString(),
                    AlreadyDelivered = true,
                };
            }

            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (note is { Length: > LootboxRuntimeServiceConstants.MaxDeliveryNoteLength })
                note = note[..LootboxRuntimeServiceConstants.MaxDeliveryNoteLength];

            claim.DeliveredAt = Now();
            claim.DeliveryMethod = method;
            claim.DeliveryNote = note;
            await _repo.SaveChangesAsync();
            _logger.LogInformation("Lootbox claim {ClaimId} delivered ({Method})", claim.Id, method);

            return new LootboxDeliveredResultDto
            {
                ClaimId = claim.Id,
                DeliveredAt = Utc(claim.DeliveredAt.Value),
                DeliveryMethod = method.ToString(),
                AlreadyDelivered = false,
            };
        }

        // ===== Claim helpers =====

        private static void EnsureCanOpen(User user)
        {
            if (user.IsFrozen) throw Conflict("Frozen", "Frozen players can't open lootboxes.");
            if (!user.IsActive || user.DeletedAt != null) throw Conflict("UserInactive", "This account can't open lootboxes.");
        }

        private static void EnsureClaimable(LootboxSpawnStatus status, DateTime expiresAt, DateTime now)
        {
            switch (status)
            {
                case LootboxSpawnStatus.Claimed:
                    throw Conflict("AlreadyClaimed", "Someone else got there first.");
                case LootboxSpawnStatus.Removed:
                    throw Conflict("Removed", "This lootbox was removed.");
                case LootboxSpawnStatus.Expired:
                    throw Conflict("Expired", "This lootbox has expired.");
            }
            if (expiresAt <= now) throw Conflict("Expired", "This lootbox has expired.");
        }

        /// <summary>
        /// DESIGN.md §3.3 step 3: the UTC calendar day [00:00, 24:00), all types together first, then this type.
        /// Admin gives aren't counted (the repository counts world-box claims only).
        /// </summary>
        private async Task EnforceDailyCapAsync(int userId, LootboxType type, LootboxConfiguration config, DateTime now)
        {
            var (dayStart, dayEnd) = UtcDay(now);
            if (config.MaxClaimsPerPlayerPerDay is int globalLimit
                && await _repo.CountClaimsAsync(userId, dayStart, dayEnd) >= globalLimit)
            {
                throw new LootboxDailyLimitException(LootboxDailyLimitException.GlobalScope, globalLimit, dayEnd);
            }
            if (type.MaxClaimsPerPlayerPerDay is int typeLimit
                && await _repo.CountClaimsAsync(userId, dayStart, dayEnd, type.Id) >= typeLimit)
            {
                throw new LootboxDailyLimitException(LootboxDailyLimitException.TypeScope, typeLimit, dayEnd);
            }
        }

        public static (DateTime Start, DateTime End) UtcDay(DateTime utcNow)
        {
            var start = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);
            return (start, start.AddDays(1));
        }

        // The same input the odds preview uses (ILootboxTypeService.BuildRollInputAsync), so they can't drift.
        private async Task<LootRollResult> RollAsync(int typeId, int boxStars)
        {
            var input = await _types.BuildRollInputAsync(typeId)
                ?? throw new KeyNotFoundException($"LootboxType {typeId} not found.");
            try
            {
                var roll = _engine.Roll(input, boxStars);
                if (roll.WindowWidened)
                    _logger.LogWarning("Lootbox type {TypeId}: no pool item in the ★{BoxStars} window, used the nearest grade", typeId, boxStars);
                return roll;
            }
            catch (InvalidOperationException ex)
            {
                throw Conflict("EmptyPool", ex.Message);
            }
        }

        // Inserts the ItemInstance (non-stackable items only) and the claim in one save, then points the instance's
        // OriginRef at the claim id, which only exists after that save (DESIGN.md §3.3 step 4).
        private async Task<LootboxClaim> MintAsync(LootRollResult roll, int userId, int typeId, int boxGradeId, int? spawnId, string key, DateTime now)
        {
            var blueprint = await _repo.GetBlueprintAsync(roll.Item.BlueprintId)
                ?? throw new InvalidOperationException($"ItemBlueprint {roll.Item.BlueprintId} not found.");

            ItemInstance? instance = null;
            if (blueprint.MaxStackSize <= 1)
            {
                instance = await _instances.BuildAsync(
                    blueprint,
                    roll.ItemGrade?.Id,
                    userId,
                    ItemInstanceOrigin.Lootbox,
                    roll.Enchantments.Select(e => new ItemInstanceEnchantmentSpec(e.DefinitionId, e.Level)));
                instance.CreatedAt = now;
            }

            var claim = new LootboxClaim
            {
                LootboxSpawnId = spawnId,
                UserId = userId,
                LootboxTypeId = typeId,
                BoxGradeId = boxGradeId,
                ItemBlueprintId = blueprint.Id,
                ItemGradeId = roll.ItemGrade?.Id,
                Quantity = roll.Quantity,
                IsSpecial = roll.IsSpecial,
                ItemInstance = instance,
                IdempotencyKey = key,
                ClaimedAt = now,
            };
            _repo.AddClaim(claim);
            await _repo.SaveChangesAsync();

            if (instance != null)
            {
                instance.OriginRef = claim.Id.ToString(CultureInfo.InvariantCulture);
                await _repo.SaveChangesAsync();
            }
            return claim;
        }

        private async Task<LootboxClaimResultDto> ReplayAsync(LootboxClaim stored, int userId, int? spawnId, int? typeId)
        {
            EnsureSameClaim(stored, userId, spawnId, typeId);
            _logger.LogInformation("Lootbox claim {ClaimId} replayed for user {UserId}", stored.Id, userId);
            return await ResultAsync(stored.Id, replay: true)
                ?? throw new InvalidOperationException($"Lootbox claim {stored.Id} vanished.");
        }

        // An idempotency key belongs to one (user, box); anyone else reusing it gets a 409, never someone's item.
        private static void EnsureSameClaim(LootboxClaim stored, int userId, int? spawnId, int? typeId)
        {
            if (stored.UserId != userId || stored.LootboxSpawnId != spawnId || (typeId != null && stored.LootboxTypeId != typeId))
                throw Conflict("IdempotencyKeyReused", "That idempotency key was already used for another claim.");
        }

        private async Task<LootboxClaimResultDto?> ResultAsync(int claimId, bool replay)
        {
            var claim = await _repo.GetClaimAsync(claimId);
            if (claim == null) return null;
            var config = await _repo.GetConfigurationAsync() ?? new LootboxConfiguration();
            return ToResult(claim, config, replay);
        }

        private static LootboxClaimResultDto ToResult(LootboxClaim claim, LootboxConfiguration config, bool replay)
        {
            // The instance holds the final set (defaults merged with the rolls); a stackable item has no instance and
            // no rolls, so its set is the blueprint's defaults.
            var enchantments = claim.ItemInstance != null
                ? claim.ItemInstance.Enchantments
                    .Where(e => e.EnchantmentDefinition != null)
                    .Select(e => new LootboxClaimEnchantmentDto
                    {
                        DefinitionId = e.EnchantmentDefinitionId,
                        Key = e.EnchantmentDefinition.Key,
                        IsCustom = e.EnchantmentDefinition.IsCustom,
                        Level = e.Level,
                    })
                : (claim.ItemBlueprint?.DefaultEnchantments ?? new List<ItemBlueprintDefaultEnchantment>())
                    .Where(e => e.EnchantmentDefinition != null && e.Level > 0)
                    .GroupBy(e => e.EnchantmentDefinitionId)
                    .Select(g => g.OrderByDescending(e => e.Level).First())
                    .Select(e => new LootboxClaimEnchantmentDto
                    {
                        DefinitionId = e.EnchantmentDefinitionId,
                        Key = e.EnchantmentDefinition.Key,
                        IsCustom = e.EnchantmentDefinition.IsCustom,
                        Level = e.Level,
                    });

            var itemStars = claim.ItemGrade?.Stars;
            var threshold = claim.LootboxType?.AnnounceMinItemStars ?? config.AnnounceMinItemStars;
            return new LootboxClaimResultDto
            {
                ClaimId = claim.Id,
                Replay = replay,
                UserId = claim.UserId,
                LootboxSpawnId = claim.LootboxSpawnId,
                LootboxTypeId = claim.LootboxTypeId,
                BoxStars = claim.BoxGrade?.Stars ?? 0,
                BoxLabel = BoxLabel(claim.BoxGrade, claim.LootboxType),
                ItemInstanceId = claim.ItemInstanceId,
                ItemBlueprintId = claim.ItemBlueprintId,
                ItemName = ItemName(claim.ItemBlueprint),
                ItemGradeId = claim.ItemGradeId,
                ItemGradeStars = itemStars,
                Quantity = claim.Quantity,
                IsSpecial = claim.IsSpecial,
                Enchantments = enchantments.OrderBy(e => e.DefinitionId).ToList(),
                Announce = claim.IsSpecial || (itemStars is int stars && stars >= threshold),
                ClaimedAt = Utc(claim.ClaimedAt),
                DeliveredAt = Utc(claim.DeliveredAt),
            };
        }

        // ===== Spawn helpers =====

        private LootboxType PickType(IReadOnlyList<LootboxType> candidates)
        {
            var total = candidates.Sum(t => (long)t.SpawnWeight);
            if (total > int.MaxValue) total = int.MaxValue;
            var target = (long)_random.NextInt(0, (int)total);
            foreach (var type in candidates)
            {
                target -= type.SpawnWeight;
                if (target < 0) return type;
            }
            return candidates[^1];
        }

        private async Task<LootGrade?> RollBoxGradeAsync(LootboxType type)
        {
            var grades = (await _repo.GetGradesAsync()).Select(LootboxRollInputBuilder.ToLootGrade).ToList();
            var weights = type.GradeWeights.ToDictionary(w => w.GradeId, w => w.Weight);
            return _engine.RollBoxGrade(grades, type.MinBoxStars, type.MaxBoxStars, weights);
        }

        // An explicit star count (1-5, any type) or the type's own box-grade roll.
        private async Task<LootGrade> BoxGradeAsync(LootboxType type, int? boxStars)
        {
            if (boxStars == null)
            {
                return await RollBoxGradeAsync(type)
                    ?? throw Conflict("NoBoxGrade", $"'{type.Name}' has no box grade with weight in ★{type.MinBoxStars}-{type.MaxBoxStars}.");
            }
            if (boxStars < 1 || boxStars > LootboxRollEngine.MaxBoxStars)
                throw new ArgumentException($"boxStars must be 1-{LootboxRollEngine.MaxBoxStars}.", nameof(boxStars));
            var grade = (await _repo.GetGradesAsync()).FirstOrDefault(g => g.Stars == boxStars)
                ?? throw new ArgumentException($"No grade has {boxStars} stars.", nameof(boxStars));
            return LootboxRollInputBuilder.ToLootGrade(grade);
        }

        // A staff spawn of a type that can give nothing would be a box nobody can open.
        private async Task EnsureHasLootAsync(LootboxType type, int boxStars)
        {
            var input = await _types.BuildRollInputAsync(type.Id)
                ?? throw new KeyNotFoundException($"LootboxType {type.Id} not found.");
            if (!input.Pool.Any(i => i.GradeId != null && i.Weight > 0m))
                throw Conflict("EmptyPool", $"'{type.Name}' has no item to give.");
        }

        private static void ValidatePosition(string? world, int x, int y, int z, string? serverId)
        {
            if (string.IsNullOrWhiteSpace(world) || world.Trim().Length > 64)
                throw new ArgumentException("world is required (at most 64 characters).");
            // Minecraft's world border and build limits, with room to spare.
            if (Math.Abs(x) > 30_000_000 || Math.Abs(z) > 30_000_000 || y < -2048 || y > 4096)
                throw new ArgumentException("Coordinates are outside the world.");
            if (serverId is { Length: > 64 }) throw new ArgumentException("serverId can be at most 64 characters.");
        }

        private async Task<LootboxSpawnDto> SpawnDtoAsync(int spawnId)
        {
            var spawn = await _repo.GetSpawnAsync(spawnId)
                ?? throw new KeyNotFoundException($"Lootbox spawn {spawnId} not found.");
            return ToSpawnDto(spawn);
        }

        private static LootboxSpawnDto ToSpawnDto(LootboxSpawn spawn) => new()
        {
            Id = spawn.Id,
            Token = spawn.Token,
            LootboxTypeId = spawn.LootboxTypeId,
            LootboxTypeName = spawn.LootboxType?.Name ?? string.Empty,
            CategoryName = spawn.LootboxType?.Category?.Name,
            BoxGradeId = spawn.BoxGradeId,
            BoxGradeName = spawn.BoxGrade?.Name ?? string.Empty,
            BoxStars = spawn.BoxGrade?.Stars ?? 0,
            BoxLabel = BoxLabel(spawn.BoxGrade, spawn.LootboxType),
            SpawnAreaId = spawn.SpawnAreaId,
            SpawnAreaName = spawn.SpawnArea?.Name,
            World = spawn.World,
            X = spawn.X,
            Y = spawn.Y,
            Z = spawn.Z,
            Status = spawn.Status.ToString(),
            SpawnedAt = Utc(spawn.SpawnedAt),
            ExpiresAt = Utc(spawn.ExpiresAt),
            ClaimedAt = Utc(spawn.ClaimedAt),
            ClaimedByUserId = spawn.ClaimedByUserId,
            ServerId = spawn.ServerId,
            CreatedByUserId = spawn.CreatedByUserId,
        };

        // ===== Shared =====

        private async Task<int?> ExistingUserIdAsync(int? userId)
        {
            if (userId is not int id || id <= 0) return null;
            if (await _users.GetByIdAsync(id) != null) return id;
            _logger.LogWarning("Lootbox: acting user {UserId} doesn't exist; recording the action without an actor", id);
            return null;
        }

        // AuditLogEntry needs a target player. An admin spawn has none, so it is recorded against the staff member
        // who made it; without a known staff member (a console command) only the structured log keeps it.
        private async Task AuditAsync(int? actorUserId, int? targetUserId, AuditAction action, object details)
        {
            var target = targetUserId ?? actorUserId;
            if (target is not int targetId)
            {
                _logger.LogInformation("Lootbox {Action} without a known staff member: {Details}", action, JsonSerializer.Serialize(details));
                return;
            }
            await _audit.RecordAsync(actorUserId, targetId, action, JsonSerializer.Serialize(details));
        }

        private static string BoxLabel(Grade? grade, LootboxType? type) =>
            $"{grade?.Name} {type?.Name}".Trim();

        private static string TypeLabel(LootboxClaimResultDto result) =>
            result.BoxLabel.Length > 0 ? result.BoxLabel : result.LootboxTypeId.ToString(CultureInfo.InvariantCulture);

        private static string ItemName(ItemBlueprint? blueprint) =>
            blueprint == null ? string.Empty : (string.IsNullOrWhiteSpace(blueprint.Name) ? blueprint.DefaultDisplayName : blueprint.Name);

        private static string? IdempotencyKey(string? raw)
        {
            var key = raw?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > LootboxRuntimeServiceConstants.MaxIdempotencyKeyLength) return null;
            return key;
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static LootboxConflictException Conflict(string code, string message) => new(code, message);

        // Whole seconds: the columns are MySQL datetime, which would round a fraction up (possibly into tomorrow).
        private DateTime Now()
        {
            var utc = _time.GetUtcNow().UtcDateTime;
            return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }

        private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
    }
}

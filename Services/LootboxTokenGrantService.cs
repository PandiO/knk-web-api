using System.Globalization;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;

namespace knkwebapi_v2.Services
{
    /// <inheritdoc cref="ILootboxTokenGrantService"/>
    public class LootboxTokenGrantService : ILootboxTokenGrantService
    {
        private readonly ILootboxTokenGrantRepository _repo;
        private readonly ILootboxRuntimeService _runtime;
        private readonly IUserRepository _users;
        private readonly TimeProvider _time;
        private readonly ILogger<LootboxTokenGrantService> _logger;
        private readonly IPlayerNotificationQueue? _notificationQueue;

        public LootboxTokenGrantService(
            ILootboxTokenGrantRepository repo,
            ILootboxRuntimeService runtime,
            IUserRepository users,
            TimeProvider time,
            ILogger<LootboxTokenGrantService> logger,
            IPlayerNotificationQueue? notificationQueue = null)
        {
            _repo = repo;
            _runtime = runtime;
            _users = users;
            _time = time;
            _logger = logger;
            _notificationQueue = notificationQueue;
        }

        // ===== Rules (web app) =====

        public async Task<List<LootboxTokenGrantDto>> GetAllAsync()
        {
            return (await _repo.GetAllAsync()).Select(ToDto).ToList();
        }

        public async Task<LootboxTokenGrantDto> CreateAsync(LootboxTokenGrantDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var grant = new LootboxTokenGrant();
            await ApplyAsync(grant, dto);
            await _repo.AddAsync(grant);
            _logger.LogInformation("Lootbox token grant {GrantId} created", grant.Id);
            return ToDto((await _repo.GetAllAsync()).Single(g => g.Id == grant.Id));
        }

        public async Task<LootboxTokenGrantDto> UpdateAsync(int id, LootboxTokenGrantDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var grant = await _repo.GetByIdAsync(id) ?? throw new KeyNotFoundException($"Lootbox token grant {id} not found.");
            await ApplyAsync(grant, dto);
            await _repo.UpdateAsync(grant);
            return ToDto((await _repo.GetAllAsync()).Single(g => g.Id == grant.Id));
        }

        public async Task DeleteAsync(int id)
        {
            var grant = await _repo.GetByIdAsync(id) ?? throw new KeyNotFoundException($"Lootbox token grant {id} not found.");
            await _repo.DeleteAsync(grant);
        }

        private async Task ApplyAsync(LootboxTokenGrant grant, LootboxTokenGrantDto dto)
        {
            if ((dto.PermissionGroupId == null) == (dto.KitId == null))
                throw new ArgumentException("Set exactly one of permissionGroupId (a premium tier) and kitId.");
            if (dto.BoxStars is int stars && (stars < 1 || stars > LootboxRollEngine.MaxBoxStars))
                throw new ArgumentException($"boxStars must be 1-{LootboxRollEngine.MaxBoxStars} (or empty to roll it).");
            if (dto.Quantity < 1 || dto.Quantity > LootboxRuntimeServiceConstants.MaxTokensPerIssue)
                throw new ArgumentException($"quantity must be 1-{LootboxRuntimeServiceConstants.MaxTokensPerIssue}.");
            if (!await _repo.TypeExistsAsync(dto.LootboxTypeId))
                throw new ArgumentException($"LootboxType {dto.LootboxTypeId} not found.");
            if (dto.PermissionGroupId is int groupId)
            {
                var group = await _repo.GetPermissionGroupAsync(groupId)
                    ?? throw new ArgumentException($"PermissionGroup {groupId} not found.");
                // Only premium tiers: Default is re-granted whenever a tier lapses, which would issue again and again.
                if (!group.IsPremiumTier)
                    throw new ArgumentException($"'{group.Name}' isn't a premium tier; only premium tiers grant lootbox tokens.");
            }
            if (dto.KitId is int kitId && !await _repo.KitExistsAsync(kitId))
                throw new ArgumentException($"Kit {kitId} not found.");

            grant.LootboxTypeId = dto.LootboxTypeId;
            grant.BoxStars = dto.BoxStars;
            grant.Quantity = dto.Quantity;
            grant.PermissionGroupId = dto.PermissionGroupId;
            grant.KitId = dto.KitId;
            grant.Enabled = dto.Enabled;
        }

        private static LootboxTokenGrantDto ToDto(LootboxTokenGrant grant) => new()
        {
            Id = grant.Id,
            LootboxTypeId = grant.LootboxTypeId,
            LootboxTypeName = grant.LootboxType?.Name,
            BoxStars = grant.BoxStars,
            Quantity = grant.Quantity,
            PermissionGroupId = grant.PermissionGroupId,
            PermissionGroupName = grant.PermissionGroup?.Name,
            KitId = grant.KitId,
            KitName = grant.Kit?.Name,
            Enabled = grant.Enabled,
        };

        // ===== Hooks =====

        public async Task<int> IssueForPremiumTierAsync(int userId, int permissionGroupId, int? actorUserId)
        {
            List<LootboxTokenGrant> rules;
            try
            {
                rules = await _repo.GetEnabledForGroupAsync(permissionGroupId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lootbox token grants for group {GroupId} could not be read; user {UserId} got none", permissionGroupId, userId);
                return 0;
            }
            // One issue per rule per membership start (to the second): a retried or repeated assignment in the same
            // second replays instead of issuing twice.
            var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            return await IssueAsync(userId, rules, LootboxTokenReason.PremiumTier, actorUserId,
                rule => $"tier:{userId}:{permissionGroupId}:{rule.Id}:{stamp}", $"premium tier {permissionGroupId}");
        }

        public async Task<int> IssueForKitAsync(int userId, int kitId, int kitClaimId, int? actorUserId)
        {
            List<LootboxTokenGrant> rules;
            try
            {
                rules = await _repo.GetEnabledForKitAsync(kitId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lootbox token grants for kit {KitId} could not be read; user {UserId} got none", kitId, userId);
                return 0;
            }
            return await IssueAsync(userId, rules, LootboxTokenReason.Kit, actorUserId,
                rule => $"kit:{kitClaimId}:{rule.Id}", $"kit {kitId} (claim {kitClaimId})");
        }

        private async Task<int> IssueAsync(
            int userId, List<LootboxTokenGrant> rules, LootboxTokenReason reason, int? actorUserId,
            Func<LootboxTokenGrant, string> keyOf, string source)
        {
            if (rules.Count == 0) return 0;
            var issued = 0;
            foreach (var rule in rules)
            {
                try
                {
                    var result = await _runtime.IssueTokensAsync(new LootboxTokenIssueRequestDto
                    {
                        UserId = userId,
                        TypeId = rule.LootboxTypeId,
                        BoxStars = rule.BoxStars,
                        Quantity = rule.Quantity,
                        Reason = reason.ToString(),
                        Note = $"{source}, rule {rule.Id}",
                        IdempotencyKey = keyOf(rule),
                    }, actorUserId);
                    if (!result.Replay) issued += result.Tokens.Count;
                }
                catch (Exception ex)
                {
                    // Never undo the rank change or kit grant that triggered this; staff can issue by hand.
                    _logger.LogError(ex, "Lootbox token grant {GrantId} ({Source}) failed for user {UserId}", rule.Id, source, userId);
                }
            }

            if (issued > 0 && _notificationQueue != null)
            {
                var user = await _users.GetByIdAsync(userId);
                if (user != null)
                    _notificationQueue.Enqueue(user.Id, user.Uuid, user.Username, PlayerNotificationTypes.LootboxTokensIssued, null);
            }
            return issued;
        }
    }
}

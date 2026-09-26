using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class UserPermissionGroupService : IUserPermissionGroupService
    {
        private readonly IUserPermissionGroupRepository _repo;
        private readonly IUserRepository _userRepo;
        private readonly IPermissionGroupRepository _groupRepo;
        private readonly IAuditLogService _auditLogService;
        private readonly IPlayerNotificationQueue? _notificationQueue;

        public UserPermissionGroupService(
            IUserPermissionGroupRepository repo,
            IUserRepository userRepo,
            IPermissionGroupRepository groupRepo,
            IAuditLogService auditLogService,
            IPlayerNotificationQueue? notificationQueue = null)
        {
            _repo = repo;
            _userRepo = userRepo;
            _groupRepo = groupRepo;
            _auditLogService = auditLogService;
            _notificationQueue = notificationQueue;
        }

        /// <summary>
        /// A player's rank: the free "Default" group or a premium tier (Noble, Royal, Dragon Blood),
        /// which upgrade from it. A user holds at most one active rank - see UpsertAsync/DeleteAsync
        /// and docs/specs/user-features/RANK_DISPLAY.md. Default is matched by name, like
        /// UserService.DefaultGroupName.
        /// </summary>
        public static bool IsRank(PermissionGroup? group) =>
            group != null && (group.IsPremiumTier || IsDefault(group));

        private static bool IsDefault(PermissionGroup group) =>
            !group.IsPremiumTier && string.Equals(group.Name, UserService.DefaultGroupName, StringComparison.OrdinalIgnoreCase);

        public async Task<List<UserPermissionGroupDto>> GetByUserAsync(int userId)
        {
            var now = DateTime.UtcNow;
            var memberships = await _repo.GetByUserAsync(userId);
            return memberships.Select(m => ToDto(m, now)).ToList();
        }

        public async Task<List<UserPermissionGroupDto>> GetByGroupAsync(int permissionGroupId)
        {
            var now = DateTime.UtcNow;
            var memberships = await _repo.GetByGroupAsync(permissionGroupId);
            return memberships.Select(m => ToDto(m, now)).ToList();
        }

        public async Task<UserPermissionGroupDto> UpsertAsync(UpsertUserPermissionGroupDto dto, int? actorUserId = null)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var now = DateTime.UtcNow;
            var expiresAt = NormalizeToUtc(dto.ExpiresAt);
            if (expiresAt.HasValue && expiresAt.Value <= now)
                throw new ArgumentException("expiresAt must be in the future (omit it for a permanent membership).", nameof(dto));

            var user = await _userRepo.GetByIdAsync(dto.UserId);
            if (user == null)
                throw new KeyNotFoundException($"User with id {dto.UserId} not found.");

            var group = await _groupRepo.GetByIdAsync(dto.PermissionGroupId);
            if (group == null)
                throw new KeyNotFoundException($"PermissionGroup with id {dto.PermissionGroupId} not found.");

            var existing = await _repo.GetAsync(dto.UserId, dto.PermissionGroupId);
            if (existing != null)
            {
                existing.ExpiresAt = expiresAt;
                await _repo.UpdateAsync(existing);
                existing.PermissionGroup ??= group;

                await _auditLogService.RecordAsync(actorUserId, dto.UserId, AuditAction.GroupAssigned, JsonSerializer.Serialize(new
                {
                    permissionGroupId = dto.PermissionGroupId,
                    groupName = group.Name,
                    expiresAt,
                    updatedExisting = true
                }));

                await AfterMembershipChangeAsync(user, group, now, actorUserId);
                return ToDto(existing, now);
            }

            var membership = new UserPermissionGroup
            {
                UserId = dto.UserId,
                PermissionGroupId = dto.PermissionGroupId,
                ExpiresAt = expiresAt
            };
            await _repo.AddAsync(membership);
            membership.PermissionGroup = group;

            await _auditLogService.RecordAsync(actorUserId, dto.UserId, AuditAction.GroupAssigned, JsonSerializer.Serialize(new
            {
                permissionGroupId = dto.PermissionGroupId,
                groupName = group.Name,
                expiresAt,
                updatedExisting = false
            }));

            await AfterMembershipChangeAsync(user, group, now, actorUserId);
            return ToDto(membership, now);
        }

        /// <summary>
        /// One rank per user: granting a rank removes every other active rank they hold (premium
        /// tiers and Default alike) - an upgrade from Default, a switch between tiers, or a drop
        /// back to Default. Expired rows are history and are left alone. Then tells the plugin.
        /// </summary>
        private async Task AfterMembershipChangeAsync(User user, PermissionGroup group, DateTime now, int? actorUserId)
        {
            if (IsRank(group))
            {
                var others = (await _repo.GetByUserAsync(user.Id))
                    .Where(m => m.PermissionGroupId != group.Id && IsRank(m.PermissionGroup))
                    .Where(m => m.ExpiresAt == null || m.ExpiresAt > now)
                    .ToList();
                foreach (var other in others)
                {
                    var otherName = other.PermissionGroup?.Name;
                    await _repo.DeleteAsync(other);
                    await _auditLogService.RecordAsync(actorUserId, user.Id, AuditAction.GroupRemoved, JsonSerializer.Serialize(new
                    {
                        permissionGroupId = other.PermissionGroupId,
                        groupName = otherName,
                        replacedBy = group.Name
                    }));
                }
            }
            NotifyRankChanged(user);
        }

        /// <summary>
        /// Puts a user who holds no active rank back on Default (permanent). No-op when they still
        /// hold one, or when no "Default" group exists.
        /// </summary>
        private async Task EnsureDefaultRankAsync(User user, DateTime now, int? actorUserId)
        {
            var memberships = await _repo.GetByUserAsync(user.Id);
            if (memberships.Any(m => IsRank(m.PermissionGroup) && (m.ExpiresAt == null || m.ExpiresAt > now)))
                return;
            var defaultGroup = await _groupRepo.GetByNameAsync(UserService.DefaultGroupName);
            if (defaultGroup == null)
                return;

            var existing = memberships.FirstOrDefault(m => m.PermissionGroupId == defaultGroup.Id);
            if (existing != null)
            {
                existing.ExpiresAt = null; // an expired Default row: make it permanent again
                await _repo.UpdateAsync(existing);
            }
            else
            {
                await _repo.AddAsync(new UserPermissionGroup { UserId = user.Id, PermissionGroupId = defaultGroup.Id, ExpiresAt = null });
            }
            await _auditLogService.RecordAsync(actorUserId, user.Id, AuditAction.GroupAssigned, JsonSerializer.Serialize(new
            {
                permissionGroupId = defaultGroup.Id,
                groupName = defaultGroup.Name,
                expiresAt = (DateTime?)null,
                reason = "back to the free rank"
            }));
        }

        private void NotifyRankChanged(User user)
        {
            _notificationQueue?.Enqueue(user.Id, user.Uuid, user.Username, PlayerNotificationTypes.RankChanged, null);
        }

        public async Task<int> SweepExpiredRanksAsync(DateTime after, DateTime asOf)
        {
            var notify = new Dictionary<int, User>();
            foreach (var expired in await _repo.GetRanksExpiredBetweenAsync(after, asOf, UserService.DefaultGroupName))
            {
                if (expired.User != null)
                    notify[expired.UserId] = expired.User;
            }
            foreach (var user in await _repo.GetUsersLeftWithoutRankAsync(asOf, UserService.DefaultGroupName))
            {
                await EnsureDefaultRankAsync(user, asOf, null);
                notify[user.Id] = user;
            }
            foreach (var user in notify.Values)
                NotifyRankChanged(user);
            return notify.Count;
        }

        public async Task DeleteAsync(int userId, int permissionGroupId, int? actorUserId = null)
        {
            var existing = await _repo.GetAsync(userId, permissionGroupId);
            if (existing == null)
                throw new KeyNotFoundException($"User {userId} is not a member of PermissionGroup {permissionGroupId}.");
            var groupName = existing.PermissionGroup?.Name;
            var wasPaidRank = existing.PermissionGroup?.IsPremiumTier == true;
            await _repo.DeleteAsync(existing);

            await _auditLogService.RecordAsync(actorUserId, userId, AuditAction.GroupRemoved, JsonSerializer.Serialize(new
            {
                permissionGroupId,
                groupName
            }));

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                return;
            // Removing a premium rank drops the user back to the free Default rank.
            if (wasPaidRank)
                await EnsureDefaultRankAsync(user, DateTime.UtcNow, actorUserId);
            NotifyRankChanged(user);
        }

        public async Task<UserPermissionGroupDto?> GetActivePremiumTierAsync(int userId)
        {
            var now = DateTime.UtcNow;
            var tier = SelectPremiumTier(await _repo.GetByUserAsync(userId), now);
            return tier == null ? null : ToDto(tier, now);
        }

        public async Task<RankMultipliersDto> GetActiveRankMultipliersAsync(int userId) =>
            RankMultipliersDto.FromMemberships(await _repo.GetByUserAsync(userId), DateTime.UtcNow);

        /// <summary>
        /// Highest-Weight active premium membership (ties broken by lowest group id, matching
        /// PermissionGroupRepository.GetActiveGroupsForUserAsync's ordering). A temporary higher
        /// tier held on top of a permanent lower one therefore wins until it expires, then the
        /// lower one shows through again — v1's DonatorTemp/previousDonatorID "restore" without
        /// storing the previous tier anywhere.
        /// </summary>
        public static UserPermissionGroup? SelectPremiumTier(IEnumerable<UserPermissionGroup> memberships, DateTime asOf)
        {
            return memberships
                .Where(m => m.PermissionGroup != null && m.PermissionGroup.IsPremiumTier)
                .Where(m => m.ExpiresAt == null || m.ExpiresAt > asOf)
                .OrderByDescending(m => m.PermissionGroup.Weight)
                .ThenBy(m => m.PermissionGroupId)
                .FirstOrDefault();
        }

        private static UserPermissionGroupDto ToDto(UserPermissionGroup m, DateTime asOf) => new()
        {
            UserId = m.UserId,
            PermissionGroupId = m.PermissionGroupId,
            PermissionGroupName = m.PermissionGroup?.Name,
            Weight = m.PermissionGroup?.Weight ?? 0,
            IsPremiumTier = m.PermissionGroup?.IsPremiumTier ?? false,
            ChatPrimaryColor = m.PermissionGroup?.ChatPrimaryColor,
            ChatSecondaryColor = m.PermissionGroup?.ChatSecondaryColor,
            NameColor = m.PermissionGroup?.NameColor,
            // Stored as UTC (see NormalizeToUtc) but MySQL reads it back as Unspecified — mark it
            // UTC so it serializes with a "Z" and clients don't read it as local time.
            ExpiresAt = m.ExpiresAt.HasValue ? DateTime.SpecifyKind(m.ExpiresAt.Value, DateTimeKind.Utc) : null,
            IsActive = m.ExpiresAt == null || m.ExpiresAt > asOf
        };

        /// <summary>The resolution engine compares ExpiresAt against DateTime.UtcNow, so store UTC.
        /// A timestamp without an offset is taken to already be UTC.</summary>
        private static DateTime? NormalizeToUtc(DateTime? value)
        {
            if (!value.HasValue) return null;
            return value.Value.Kind switch
            {
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
                _ => value.Value
            };
        }
    }
}

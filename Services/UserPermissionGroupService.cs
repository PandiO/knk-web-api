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

        public UserPermissionGroupService(
            IUserPermissionGroupRepository repo,
            IUserRepository userRepo,
            IPermissionGroupRepository groupRepo,
            IAuditLogService auditLogService)
        {
            _repo = repo;
            _userRepo = userRepo;
            _groupRepo = groupRepo;
            _auditLogService = auditLogService;
        }

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

            if (await _userRepo.GetByIdAsync(dto.UserId) == null)
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

            return ToDto(membership, now);
        }

        public async Task DeleteAsync(int userId, int permissionGroupId, int? actorUserId = null)
        {
            var existing = await _repo.GetAsync(userId, permissionGroupId);
            if (existing == null)
                throw new KeyNotFoundException($"User {userId} is not a member of PermissionGroup {permissionGroupId}.");
            var groupName = existing.PermissionGroup?.Name;
            await _repo.DeleteAsync(existing);

            await _auditLogService.RecordAsync(actorUserId, userId, AuditAction.GroupRemoved, JsonSerializer.Serialize(new
            {
                permissionGroupId,
                groupName
            }));
        }

        public async Task<UserPermissionGroupDto?> GetActivePremiumTierAsync(int userId)
        {
            var now = DateTime.UtcNow;
            var tier = SelectPremiumTier(await _repo.GetByUserAsync(userId), now);
            return tier == null ? null : ToDto(tier, now);
        }

        public async Task<RankMultipliersDto> GetActiveRankMultipliersAsync(int userId)
        {
            var now = DateTime.UtcNow;
            var groups = (await _repo.GetByUserAsync(userId))
                .Where(m => m.PermissionGroup != null && (m.ExpiresAt == null || m.ExpiresAt > now))
                .Select(m => m.PermissionGroup!)
                .ToList();
            return new RankMultipliersDto
            {
                Salary = groups.Aggregate(1.0m, (product, g) => product * g.SalaryMultiplier),
                GemBonus = groups.Aggregate(1.0m, (product, g) => product * g.GemBonusMultiplier),
                ExpBonus = groups.Aggregate(1.0m, (product, g) => product * g.ExpBonusMultiplier)
            };
        }

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

using System.Text.Json;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class PermissionGrantService : IPermissionGrantService
    {
        private readonly IPermissionGrantRepository _repo;
        private readonly IMapper _mapper;
        private readonly IUserRepository _userRepo;
        private readonly IAuditLogService _auditLogService;

        public PermissionGrantService(IPermissionGrantRepository repo, IMapper mapper, IUserRepository userRepo, IAuditLogService auditLogService)
        {
            _repo = repo;
            _mapper = mapper;
            _userRepo = userRepo;
            _auditLogService = auditLogService;
        }

        /// <summary>
        /// A grant's HolderId is a User's own id only when the holder is actually a User (it may
        /// instead be a PermissionGroup — see PermissionHolder's own doc comment on the two
        /// possible holder types). Audit entries need one specific TargetUserId, so group-level
        /// grants (which affect every member indirectly, not one player) are intentionally not
        /// audited per-grant here — see this file's retrofit notes in ACTIVE_SESSIONS.md.
        /// </summary>
        private async Task<bool> IsUserHolderAsync(int holderId) => await _userRepo.GetByIdAsync(holderId) != null;

        public async Task<IEnumerable<PermissionGrantDto>> GetAllAsync()
        {
            var grants = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<PermissionGrantDto>>(grants);
        }

        public async Task<PermissionGrantDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var grant = await _repo.GetByIdAsync(id);
            return _mapper.Map<PermissionGrantDto>(grant);
        }

        public async Task<PermissionGrantDto> CreateAsync(PermissionGrantDto dto, int? actorUserId = null)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Node)) throw new ArgumentException("Permission node is required.", nameof(dto));
            if (dto.HolderId <= 0 || !await _repo.HolderExistsAsync(dto.HolderId))
                throw new ArgumentException($"PermissionHolder with id {dto.HolderId} not found.", nameof(dto));

            var grant = _mapper.Map<PermissionGrant>(dto);
            await _repo.AddAsync(grant);

            if (await IsUserHolderAsync(dto.HolderId))
            {
                await _auditLogService.RecordAsync(actorUserId, dto.HolderId, AuditAction.GrantAdded, JsonSerializer.Serialize(new
                {
                    grantId = grant.Id,
                    node = dto.Node,
                    value = dto.Value,
                    expiresAt = dto.ExpiresAt
                }));
            }

            return _mapper.Map<PermissionGrantDto>(grant);
        }

        public async Task UpdateAsync(int id, PermissionGrantDto dto, int? actorUserId = null)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.Node)) throw new ArgumentException("Permission node is required.", nameof(dto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"PermissionGrant with id {id} not found.");

            if (dto.HolderId != existing.HolderId)
            {
                if (dto.HolderId <= 0 || !await _repo.HolderExistsAsync(dto.HolderId))
                    throw new ArgumentException($"PermissionHolder with id {dto.HolderId} not found.", nameof(dto));
            }

            var previousNode = existing.Node;
            var previousValue = existing.Value;
            var previousExpiresAt = existing.ExpiresAt;

            existing.HolderId = dto.HolderId;
            existing.Node = dto.Node;
            existing.Value = dto.Value;
            existing.ExpiresAt = dto.ExpiresAt;

            await _repo.UpdateAsync(existing);

            if (await IsUserHolderAsync(dto.HolderId))
            {
                await _auditLogService.RecordAsync(actorUserId, dto.HolderId, AuditAction.GrantUpdated, JsonSerializer.Serialize(new
                {
                    grantId = id,
                    from = new { node = previousNode, value = previousValue, expiresAt = previousExpiresAt },
                    to = new { node = dto.Node, value = dto.Value, expiresAt = dto.ExpiresAt }
                }));
            }
        }

        public async Task DeleteAsync(int id, int? actorUserId = null)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"PermissionGrant with id {id} not found.");

            var holderId = existing.HolderId;
            var node = existing.Node;
            var value = existing.Value;

            await _repo.DeleteAsync(id);

            if (await IsUserHolderAsync(holderId))
            {
                await _auditLogService.RecordAsync(actorUserId, holderId, AuditAction.GrantRemoved, JsonSerializer.Serialize(new
                {
                    grantId = id,
                    node,
                    value
                }));
            }
        }

        public async Task<PagedResultDto<PermissionGrantListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<PermissionGrantListDto>>(result);
        }

        public async Task<PermissionGrantDto> UpsertByNodeAsync(int holderId, string node, bool value, DateTime? expiresAt, int? actorUserId = null)
        {
            if (holderId <= 0) throw new ArgumentException("Invalid holder id.", nameof(holderId));
            if (string.IsNullOrWhiteSpace(node)) throw new ArgumentException("Permission node is required.", nameof(node));
            if (!await _repo.HolderExistsAsync(holderId))
                throw new ArgumentException($"PermissionHolder with id {holderId} not found.", nameof(holderId));

            var existing = (await _repo.GetActiveGrantsForHolderAsync(holderId, DateTime.UtcNow))
                .FirstOrDefault(g => g.Node == node);

            if (existing != null)
            {
                var previousValue = existing.Value;
                var previousExpiresAt = existing.ExpiresAt;
                existing.Value = value;
                existing.ExpiresAt = expiresAt;
                await _repo.UpdateAsync(existing);

                if (await IsUserHolderAsync(holderId))
                {
                    await _auditLogService.RecordAsync(actorUserId, holderId, AuditAction.GrantUpdated, JsonSerializer.Serialize(new
                    {
                        grantId = existing.Id,
                        from = new { node, value = previousValue, expiresAt = previousExpiresAt },
                        to = new { node, value, expiresAt }
                    }));
                }

                return _mapper.Map<PermissionGrantDto>(existing);
            }

            var grant = new PermissionGrant { HolderId = holderId, Node = node, Value = value, ExpiresAt = expiresAt };
            await _repo.AddAsync(grant);

            if (await IsUserHolderAsync(holderId))
            {
                await _auditLogService.RecordAsync(actorUserId, holderId, AuditAction.GrantAdded, JsonSerializer.Serialize(new
                {
                    grantId = grant.Id,
                    node,
                    value,
                    expiresAt
                }));
            }

            return _mapper.Map<PermissionGrantDto>(grant);
        }

        public async Task RevokeByNodeAsync(int holderId, string node, int? actorUserId = null)
        {
            if (holderId <= 0) throw new ArgumentException("Invalid holder id.", nameof(holderId));
            if (string.IsNullOrWhiteSpace(node)) throw new ArgumentException("Permission node is required.", nameof(node));

            var existing = (await _repo.GetActiveGrantsForHolderAsync(holderId, DateTime.UtcNow))
                .FirstOrDefault(g => g.Node == node);
            if (existing == null)
                throw new KeyNotFoundException($"Holder {holderId} has no active grant for node '{node}'.");

            var value = existing.Value;
            await _repo.DeleteAsync(existing.Id);

            if (await IsUserHolderAsync(holderId))
            {
                await _auditLogService.RecordAsync(actorUserId, holderId, AuditAction.GrantRemoved, JsonSerializer.Serialize(new
                {
                    grantId = existing.Id,
                    node,
                    value
                }));
            }
        }
    }
}

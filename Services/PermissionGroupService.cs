using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Services
{
    public class PermissionGroupService : IPermissionGroupService
    {
        private readonly IPermissionGroupRepository _repo;
        private readonly IMapper _mapper;

        public PermissionGroupService(IPermissionGroupRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<PermissionGroupDto>> GetAllAsync()
        {
            var groups = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<PermissionGroupDto>>(groups);
        }

        public async Task<PermissionGroupDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var group = await _repo.GetByIdAsync(id);
            return _mapper.Map<PermissionGroupDto>(group);
        }

        private static void ValidateMultipliers(PermissionGroupDto dto)
        {
            if (dto.SalaryMultiplier < 0) throw new ArgumentException("SalaryMultiplier cannot be negative.", nameof(dto));
            if (dto.GemBonusMultiplier < 0) throw new ArgumentException("GemBonusMultiplier cannot be negative.", nameof(dto));
            if (dto.ExpBonusMultiplier < 0) throw new ArgumentException("ExpBonusMultiplier cannot be negative.", nameof(dto));
        }

        public async Task<PermissionGroupDto> CreateAsync(PermissionGroupDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Group name is required.", nameof(dto));
            ValidateMultipliers(dto);
            NormalizeColors(dto);

            if (dto.ParentGroupId.HasValue && dto.ParentGroupId > 0)
            {
                var parent = await _repo.GetByIdAsync(dto.ParentGroupId.Value);
                if (parent == null)
                    throw new ArgumentException($"Parent PermissionGroup with id {dto.ParentGroupId} not found.", nameof(dto));
            }

            var group = _mapper.Map<PermissionGroup>(dto);
            await _repo.AddAsync(group);
            return _mapper.Map<PermissionGroupDto>(group);
        }

        public async Task UpdateAsync(int id, PermissionGroupDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Group name is required.", nameof(dto));
            ValidateMultipliers(dto);
            NormalizeColors(dto);

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"PermissionGroup with id {id} not found.");

            if (dto.ParentGroupId.HasValue && dto.ParentGroupId > 0 &&
                (!existing.ParentGroupId.HasValue || existing.ParentGroupId != dto.ParentGroupId))
            {
                if (dto.ParentGroupId == id)
                    throw new ArgumentException("A group cannot be its own parent.", nameof(dto));

                if (await WouldCreateCycleAsync(id, dto.ParentGroupId.Value))
                    throw new ArgumentException("Assigning this parent would create an inheritance cycle.", nameof(dto));

                var parent = await _repo.GetByIdAsync(dto.ParentGroupId.Value);
                if (parent == null)
                    throw new ArgumentException($"Parent PermissionGroup with id {dto.ParentGroupId} not found.", nameof(dto));
            }

            existing.Name = dto.Name;
            existing.Weight = dto.Weight;
            existing.IsPremiumTier = dto.IsPremiumTier;
            existing.SalaryMultiplier = dto.SalaryMultiplier;
            // Omitted = keep: a client that predates these fields (KNG-16) must not reset them.
            existing.GemBonusMultiplier = dto.GemBonusMultiplier ?? existing.GemBonusMultiplier;
            existing.ExpBonusMultiplier = dto.ExpBonusMultiplier ?? existing.ExpBonusMultiplier;
            existing.ChatPrefix = dto.ChatPrefix;
            existing.ChatSuffix = dto.ChatSuffix;
            existing.ChatPrimaryColor = dto.ChatPrimaryColor;
            existing.ChatSecondaryColor = dto.ChatSecondaryColor;
            existing.NameColor = dto.NameColor;
            existing.ParentGroupId = dto.ParentGroupId;

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"PermissionGroup with id {id} not found.");

            if (await _repo.HasChildrenAsync(id))
                throw new InvalidOperationException("Cannot delete a group with child groups. Reassign or delete children first.");

            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<PermissionGroupListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<PermissionGroupListDto>>(result);
        }

        public async Task<IEnumerable<ExpiringMembershipDto>> GetExpiringMembershipsAsync(int groupId, int withinDays)
        {
            if (groupId <= 0) throw new ArgumentException("Invalid group id.", nameof(groupId));
            if (withinDays <= 0) throw new ArgumentException("withinDays must be positive.", nameof(withinDays));
            var group = await _repo.GetByIdAsync(groupId);
            if (group == null) throw new KeyNotFoundException($"PermissionGroup with id {groupId} not found.");

            var memberships = await _repo.GetExpiringMembershipsAsync(groupId, withinDays, DateTime.UtcNow);
            return memberships.Select(m => new ExpiringMembershipDto
            {
                UserId = m.UserId,
                Username = m.User.Username,
                PermissionGroupId = m.PermissionGroupId,
                ExpiresAt = DateTime.SpecifyKind(m.ExpiresAt!.Value, DateTimeKind.Utc)
            });
        }

        /// <summary>Validates the KNG-7 style fields and stores them in canonical form ("&amp;6&amp;L"
        /// → "&amp;6&amp;l", blank → null), so the plugin only ever sees codes it can parse.</summary>
        private static void NormalizeColors(PermissionGroupDto dto)
        {
            dto.ChatPrimaryColor = MinecraftTextStyle.Normalize(dto.ChatPrimaryColor, "ChatPrimaryColor");
            dto.ChatSecondaryColor = MinecraftTextStyle.Normalize(dto.ChatSecondaryColor, "ChatSecondaryColor");
            dto.NameColor = MinecraftTextStyle.Normalize(dto.NameColor, "NameColor");
        }

        /// <summary>Walks candidateParentId's own ancestor chain to make sure groupId doesn't
        /// appear in it — i.e. that setting groupId.ParentGroupId = candidateParentId wouldn't
        /// close a loop. The resolution engine walks this chain on every permission check, so an
        /// undetected cycle here would hang every future check against it.</summary>
        private async Task<bool> WouldCreateCycleAsync(int groupId, int candidateParentId)
        {
            var visited = new HashSet<int>();
            int? currentId = candidateParentId;
            while (currentId.HasValue)
            {
                if (currentId.Value == groupId) return true;
                if (!visited.Add(currentId.Value)) return true; // pre-existing cycle in the data
                var current = await _repo.GetByIdAsync(currentId.Value);
                currentId = current?.ParentGroupId;
            }
            return false;
        }
    }
}

using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using System.Globalization;
using System.Text.Json;

namespace knkwebapi_v2.Services
{
    public class ItemInstanceService : IItemInstanceService
    {
        private readonly IItemInstanceRepository _repo;
        private readonly IMapper _mapper;
        private readonly IAuditLogService? _audit;

        // audit is optional so the read/mint unit tests keep their two-argument construction; DI always supplies it.
        public ItemInstanceService(IItemInstanceRepository repo, IMapper mapper, IAuditLogService? audit = null)
        {
            _repo = repo;
            _mapper = mapper;
            _audit = audit;
        }

        public async Task<ItemInstance> BuildAsync(
            ItemBlueprint blueprint,
            int? gradeId,
            int? ownerUserId,
            ItemInstanceOrigin origin,
            IEnumerable<ItemInstanceEnchantmentSpec> enchantments,
            string? originRef = null)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            if (blueprint.MaxStackSize > 1)
            {
                // A per-item PDC id would stop the stack from stacking (DESIGN.md §3.2).
                throw new InvalidOperationException(
                    $"ItemBlueprint {blueprint.Id} is stackable (MaxStackSize {blueprint.MaxStackSize}); stackable items get no instance.");
            }

            var merged = new Dictionary<int, int>();
            foreach (var spec in enchantments ?? Enumerable.Empty<ItemInstanceEnchantmentSpec>())
            {
                if (spec.Level < 1)
                    throw new ArgumentException($"Enchantment {spec.EnchantmentDefinitionId} has level {spec.Level}; levels start at 1.");
                merged[spec.EnchantmentDefinitionId] = Math.Max(merged.GetValueOrDefault(spec.EnchantmentDefinitionId), spec.Level);
            }

            var existing = await _repo.GetExistingEnchantmentDefinitionIdsAsync(merged.Keys);
            var unknown = merged.Keys.Where(id => !existing.Contains(id)).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"EnchantmentDefinition(s) not found: {string.Join(", ", unknown)}.");

            var instance = new ItemInstance
            {
                // FK only: the blueprint may come from another (or no-tracking) query, and setting the
                // navigation would make EF try to insert it.
                ItemBlueprintId = blueprint.Id,
                GradeId = gradeId,
                OwnerUserId = ownerUserId,
                Origin = origin,
                OriginRef = originRef,
                CreatedAt = DateTime.UtcNow,
                OwnerCount = 1,
                IsSoulbound = false,
                IsGhosted = false,
            };
            foreach (var (definitionId, level) in merged.OrderBy(kv => kv.Key))
            {
                instance.Enchantments.Add(new ItemInstanceEnchantment
                {
                    ItemInstance = instance,
                    EnchantmentDefinitionId = definitionId,
                    Level = level,
                });
            }
            return instance;
        }

        public async Task<ItemInstanceDto?> GetAsync(long id)
        {
            if (id <= 0) return null;
            var instance = await _repo.GetByIdAsync(id);
            return instance == null ? null : _mapper.Map<ItemInstanceDto>(instance);
        }

        public async Task<ItemInstanceCloneResultDto> CloneAsync(long sourceId, int count, int? ownerUserId, int? actorUserId)
        {
            if (count < 1 || count > IItemInstanceService.MaxCloneCount)
                throw new ArgumentException($"count must be 1..{IItemInstanceService.MaxCloneCount}, was {count}.");
            var source = (sourceId > 0 ? await _repo.GetByIdAsync(sourceId) : null)
                ?? throw new KeyNotFoundException($"ItemInstance {sourceId} not found.");
            if (ownerUserId is int owner && !await _repo.UserExistsAsync(owner))
                throw new KeyNotFoundException($"User {owner} not found.");
            if (source.ItemBlueprint != null && source.ItemBlueprint.MaxStackSize > 1)
                throw new InvalidOperationException(
                    $"ItemBlueprint {source.ItemBlueprintId} is stackable (MaxStackSize {source.ItemBlueprint.MaxStackSize}); stackable items get no instance.");

            var now = DateTime.UtcNow;
            var originRef = "clone:" + sourceId.ToString(CultureInfo.InvariantCulture);
            var clones = new List<ItemInstance>(count);
            for (var i = 0; i < count; i++)
            {
                var clone = new ItemInstance
                {
                    // FK only, like BuildAsync: the source was read without tracking.
                    ItemBlueprintId = source.ItemBlueprintId,
                    GradeId = source.GradeId,
                    OwnerUserId = ownerUserId ?? source.OwnerUserId,
                    Origin = ItemInstanceOrigin.Admin,
                    OriginRef = originRef,
                    CreatedAt = now,
                    OwnerCount = 1,
                    CustomDisplayName = source.CustomDisplayName,
                    IsSoulbound = source.IsSoulbound,
                    IsGhosted = source.IsGhosted,
                };
                foreach (var enchantment in source.Enchantments.OrderBy(e => e.EnchantmentDefinitionId))
                {
                    clone.Enchantments.Add(new ItemInstanceEnchantment
                    {
                        ItemInstance = clone,
                        EnchantmentDefinitionId = enchantment.EnchantmentDefinitionId,
                        Level = enchantment.Level,
                    });
                }
                clones.Add(clone);
            }
            await _repo.AddAndSaveAsync(clones);

            var ids = clones.Select(c => c.Id).ToList();
            var target = ownerUserId ?? source.OwnerUserId ?? actorUserId;
            if (_audit != null && target is int targetId)
            {
                await _audit.RecordAsync(actorUserId, targetId, AuditAction.ItemInstanceCloned, JsonSerializer.Serialize(new
                {
                    sourceItemInstanceId = sourceId,
                    itemBlueprintId = source.ItemBlueprintId,
                    itemInstanceIds = ids,
                }));
            }
            return new ItemInstanceCloneResultDto { SourceItemInstanceId = sourceId, ItemInstanceIds = ids };
        }
    }
}

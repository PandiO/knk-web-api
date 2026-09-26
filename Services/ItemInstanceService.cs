using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class ItemInstanceService : IItemInstanceService
    {
        private readonly IItemInstanceRepository _repo;
        private readonly IMapper _mapper;

        public ItemInstanceService(IItemInstanceRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
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
    }
}

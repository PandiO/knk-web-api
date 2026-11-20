using System.Text.RegularExpressions;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    // Siege shop catalogue (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.3, § 5.4, § 6; P05, P06, P19, P25;
    // PandiO/knk-workspace#867): save-time rules for categories, entries and a scenario's overrides, and the effective
    // catalogue the plugin fetches once at match start. Data only: nothing here charges or counts a purchase (phase 3).
    // Same error convention as the other siege services (ArgumentException -> 400, KeyNotFoundException -> 404,
    // InvalidOperationException -> 409).
    public class SiegeShopService : ISiegeShopService
    {
        // One purchase never gives more than a full inventory of 64-stacks.
        public const int MaxQuantity = 36 * 64;

        private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9-]{0,49}$", RegexOptions.Compiled);

        private readonly ISiegeShopRepository _repo;
        private readonly IMapper _mapper;

        public SiegeShopService(ISiegeShopRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        // ==== Categories ====

        public async Task<List<SiegeShopCategoryReadDto>> GetCategoriesAsync() =>
            _mapper.Map<List<SiegeShopCategoryReadDto>>(await _repo.GetCategoriesAsync());

        public async Task<SiegeShopCategoryReadDto?> GetCategoryByIdAsync(int id)
        {
            if (id <= 0) return null;
            var category = await _repo.GetCategoryByIdAsync(id);
            return category == null ? null : _mapper.Map<SiegeShopCategoryReadDto>(category);
        }

        public async Task<SiegeShopCategoryReadDto> CreateCategoryAsync(SiegeShopCategoryUpsertDto dto)
        {
            var category = new SiegeShopCategory();
            await ApplyCategoryAsync(category, dto, existingId: null);
            await _repo.AddCategoryAsync(category);
            return _mapper.Map<SiegeShopCategoryReadDto>(category);
        }

        public async Task UpdateCategoryAsync(int id, SiegeShopCategoryUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var category = await _repo.GetCategoryByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeShopCategory with id {id} not found.");
            await ApplyCategoryAsync(category, dto, existingId: id);
            await _repo.UpdateCategoryAsync(category);
        }

        public async Task DeleteCategoryAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var category = await _repo.GetCategoryByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeShopCategory with id {id} not found.");
            var used = await _repo.CountEntriesInCategoryAsync(id);
            if (used > 0)
                throw new InvalidOperationException(
                    $"'{category.Name}' has {used} shop {(used == 1 ? "entry" : "entries")}; move or delete them first.");
            await _repo.DeleteCategoryAsync(category);
        }

        public async Task<PagedResultDto<SiegeShopCategoryReadDto>> SearchCategoriesAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchCategoriesAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<SiegeShopCategoryReadDto>>(result);
        }

        private async Task ApplyCategoryAsync(SiegeShopCategory target, SiegeShopCategoryUpsertDto dto, int? existingId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var key = RequireText(dto.Key, "Key", 50).ToLowerInvariant();
            if (!KeyPattern.IsMatch(key))
                throw new ArgumentException("Key may only use lower-case letters, digits and dashes (e.g. consumables).");
            var name = RequireText(dto.Name, "Name", 100);
            if (dto.SortOrder < 0) throw new ArgumentException("sortOrder can't be negative.");

            var sameKey = await _repo.GetCategoryByKeyAsync(key);
            if (sameKey != null && sameKey.Id != existingId)
                throw new InvalidOperationException($"A shop category with key '{sameKey.Key}' already exists.");

            target.Key = key;
            target.Name = name;
            target.IconMaterial = OptionalText(dto.IconMaterial, "iconMaterial", 100);
            target.SortOrder = dto.SortOrder;
        }

        // ==== Entries ====

        public async Task<List<SiegeShopEntryReadDto>> GetEntriesAsync() =>
            _mapper.Map<List<SiegeShopEntryReadDto>>(await _repo.GetEntriesAsync());

        public async Task<SiegeShopEntryReadDto?> GetEntryByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entry = await _repo.GetEntryByIdAsync(id);
            return entry == null ? null : _mapper.Map<SiegeShopEntryReadDto>(entry);
        }

        public async Task<SiegeShopEntryReadDto> CreateEntryAsync(SiegeShopEntryUpsertDto dto)
        {
            var entry = new SiegeShopEntry();
            await ApplyEntryAsync(entry, dto);
            await _repo.AddEntryAsync(entry);
            var created = await _repo.GetEntryByIdAsync(entry.Id);
            return _mapper.Map<SiegeShopEntryReadDto>(created ?? entry);
        }

        public async Task UpdateEntryAsync(int id, SiegeShopEntryUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var entry = await _repo.GetEntryByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeShopEntry with id {id} not found.");
            var wasDefault = entry.SiegeScenarioId == null;
            await ApplyEntryAsync(entry, dto);
            // Overrides only point at default entries (§ 6.1.3); turning an overridden default into a scenario-only
            // entry would leave them pointing at nothing a scenario can override.
            if (wasDefault && entry.SiegeScenarioId != null && await _repo.EntryHasOverridesAsync(id))
                throw new InvalidOperationException("Scenarios override this default entry; remove their overrides before making it scenario-only.");
            await _repo.UpdateEntryAsync(entry);
        }

        public async Task DeleteEntryAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var entry = await _repo.GetEntryByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeShopEntry with id {id} not found.");
            // The scenarios' overrides of this entry go with it.
            await _repo.DeleteEntryAsync(entry);
        }

        public async Task<PagedResultDto<SiegeShopEntryListDto>> SearchEntriesAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchEntriesAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<SiegeShopEntryListDto>>(result);
        }

        private async Task ApplyEntryAsync(SiegeShopEntry target, SiegeShopEntryUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (!Enum.IsDefined(dto.GameMode)) throw new ArgumentException($"Unknown game mode '{dto.GameMode}'.");
            if (!Enum.IsDefined(dto.Kind)) throw new ArgumentException($"Unknown kind '{dto.Kind}'.");
            if (!Enum.IsDefined(dto.PriceCurve)) throw new ArgumentException($"Unknown price curve '{dto.PriceCurve}'.");
            if (!Enum.IsDefined(dto.CooldownCurve)) throw new ArgumentException($"Unknown cooldown curve '{dto.CooldownCurve}'.");

            // The wizard can send 0 for an empty optional reference - treat it as "none".
            var scenarioId = dto.SiegeScenarioId is > 0 ? dto.SiegeScenarioId : null;
            var blueprintId = dto.ItemBlueprintId is > 0 ? dto.ItemBlueprintId : null;
            var templateId = dto.SiegeWeaponTemplateId is > 0 ? dto.SiegeWeaponTemplateId : null;

            if (dto.CategoryId <= 0) throw new ArgumentException("A shop entry needs a category.");
            if (await _repo.GetCategoryByIdAsync(dto.CategoryId) == null)
                throw new ArgumentException($"SiegeShopCategory with id {dto.CategoryId} not found.");
            if (scenarioId != null && !await _repo.ScenarioExistsAsync(scenarioId.Value))
                throw new ArgumentException($"SiegeScenario with id {scenarioId} not found.");

            // P19, A14: the target must match the kind.
            if (dto.Kind == SiegeShopEntryKind.Item)
            {
                if (templateId != null) throw new ArgumentException("An Item entry sells an item blueprint, not a siege weapon template.");
                if (blueprintId == null) throw new ArgumentException("An Item entry needs an item blueprint.");
                if (!await _repo.ItemBlueprintExistsAsync(blueprintId.Value))
                    throw new ArgumentException($"ItemBlueprint with id {blueprintId} not found.");
                if (dto.Price == null) throw new ArgumentException("An Item entry needs a price (only weapon entries fall back to the template's match price).");
            }
            else
            {
                if (blueprintId != null) throw new ArgumentException("A SiegeWeapon entry sells a siege weapon template, not an item blueprint.");
                if (templateId == null) throw new ArgumentException("A SiegeWeapon entry needs a siege weapon template.");
                if (!await _repo.WeaponTemplateExistsAsync(templateId.Value))
                    throw new ArgumentException($"SiegeWeaponTemplate with id {templateId} not found.");
                if (dto.Quantity != 1) throw new ArgumentException("A SiegeWeapon entry sells one weapon per purchase (quantity 1).");
            }

            if (dto.Quantity < 1 || dto.Quantity > MaxQuantity)
                throw new ArgumentException($"quantity must be between 1 and {MaxQuantity}.");

            // § 5.4 brakes: non-negative numbers, a maximum not below its base, factor > 1 for exponential growth.
            if (dto.Price is < 0) throw new ArgumentException("price can't be negative.");
            if (dto.PriceStep is < 0) throw new ArgumentException("priceStep can't be negative.");
            if (dto.PriceMax is < 0) throw new ArgumentException("priceMax can't be negative.");
            if (dto.PriceMax.HasValue && dto.Price.HasValue && dto.PriceMax.Value < dto.Price.Value)
                throw new ArgumentException("priceMax can't be below the price.");
            CheckGrowthFactor(dto.PriceCurve, dto.PriceGrowthFactor, "priceGrowthFactor");

            if (dto.CooldownSeconds is < 0) throw new ArgumentException("cooldownSeconds can't be negative.");
            if (dto.CooldownStepSeconds is < 0) throw new ArgumentException("cooldownStepSeconds can't be negative.");
            if (dto.CooldownMaxSeconds is < 0) throw new ArgumentException("cooldownMaxSeconds can't be negative.");
            if (dto.CooldownSeconds == null && (dto.CooldownStepSeconds.HasValue || dto.CooldownMaxSeconds.HasValue))
                throw new ArgumentException("A cooldown step or maximum needs a base cooldown (cooldownSeconds).");
            if (dto.CooldownMaxSeconds.HasValue && dto.CooldownSeconds.HasValue && dto.CooldownMaxSeconds.Value < dto.CooldownSeconds.Value)
                throw new ArgumentException("cooldownMaxSeconds can't be below cooldownSeconds.");
            CheckGrowthFactor(dto.CooldownCurve, dto.CooldownGrowthFactor, "cooldownGrowthFactor");

            if (dto.MaxPerMatch is < 1) throw new ArgumentException("maxPerMatch must be at least 1 (leave it empty for no cap).");
            if (dto.SortOrder < 0) throw new ArgumentException("sortOrder can't be negative.");

            target.GameMode = dto.GameMode;
            target.SiegeScenarioId = scenarioId;
            target.CategoryId = dto.CategoryId;
            target.Kind = dto.Kind;
            target.ItemBlueprintId = blueprintId;
            target.SiegeWeaponTemplateId = templateId;
            target.Quantity = dto.Quantity;
            target.DisplayName = OptionalText(dto.DisplayName, "displayName", 100);
            target.IconMaterial = OptionalText(dto.IconMaterial, "iconMaterial", 100);
            target.Price = dto.Price;
            target.PriceStep = dto.PriceStep;
            target.PriceCurve = dto.PriceCurve;
            target.PriceGrowthFactor = dto.PriceGrowthFactor;
            target.PriceMax = dto.PriceMax;
            target.CooldownSeconds = dto.CooldownSeconds;
            target.CooldownStepSeconds = dto.CooldownStepSeconds;
            target.CooldownCurve = dto.CooldownCurve;
            target.CooldownGrowthFactor = dto.CooldownGrowthFactor;
            target.CooldownMaxSeconds = dto.CooldownMaxSeconds;
            target.MaxPerMatch = dto.MaxPerMatch;
            target.SortOrder = dto.SortOrder;
            target.Enabled = dto.Enabled;
        }

        private static void CheckGrowthFactor(SiegeShopCurve curve, double? factor, string field)
        {
            if (factor.HasValue && (double.IsNaN(factor.Value) || double.IsInfinity(factor.Value)))
                throw new ArgumentException($"{field} must be a number.");
            if (curve == SiegeShopCurve.Exponential && factor is <= 1)
                throw new ArgumentException($"{field} must be above 1 for exponential growth (leave it empty for 1.5).");
            if (factor is <= 0) throw new ArgumentException($"{field} must be above 0.");
        }

        // ==== Overrides (owned by a scenario) ====

        public async Task<List<SiegeScenarioShopOverrideReadDto>> GetOverridesAsync(int siegeScenarioId)
        {
            await RequireScenarioAsync(siegeScenarioId);
            return _mapper.Map<List<SiegeScenarioShopOverrideReadDto>>(await _repo.GetOverridesAsync(siegeScenarioId));
        }

        public async Task<SiegeScenarioShopOverrideReadDto?> GetOverrideByIdAsync(int id)
        {
            if (id <= 0) return null;
            var shopOverride = await _repo.GetOverrideByIdAsync(id);
            return shopOverride == null ? null : _mapper.Map<SiegeScenarioShopOverrideReadDto>(shopOverride);
        }

        public async Task<SiegeScenarioShopOverrideReadDto> CreateOverrideAsync(int siegeScenarioId, SiegeScenarioShopOverrideUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.SiegeScenarioId is > 0 && dto.SiegeScenarioId.Value != siegeScenarioId)
                throw new ArgumentException("siegeScenarioId in the body doesn't match the scenario in the URL.");
            await RequireScenarioAsync(siegeScenarioId);

            var shopOverride = new SiegeScenarioShopOverride { SiegeScenarioId = siegeScenarioId };
            await ApplyOverrideAsync(shopOverride, dto, existingId: null);
            await _repo.AddOverrideAsync(shopOverride);
            var created = await _repo.GetOverrideByIdAsync(shopOverride.Id);
            return _mapper.Map<SiegeScenarioShopOverrideReadDto>(created ?? shopOverride);
        }

        public async Task UpdateOverrideAsync(int id, SiegeScenarioShopOverrideUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var shopOverride = await _repo.GetOverrideByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeScenarioShopOverride with id {id} not found.");
            if (dto.SiegeScenarioId is > 0 && dto.SiegeScenarioId.Value != shopOverride.SiegeScenarioId)
                throw new ArgumentException("An override can't be moved to another scenario; delete it and add it there instead.");
            await ApplyOverrideAsync(shopOverride, dto, existingId: id);
            await _repo.UpdateOverrideAsync(shopOverride);
        }

        public async Task DeleteOverrideAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var shopOverride = await _repo.GetOverrideByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeScenarioShopOverride with id {id} not found.");
            await _repo.DeleteOverrideAsync(shopOverride);
        }

        private async Task ApplyOverrideAsync(SiegeScenarioShopOverride target, SiegeScenarioShopOverrideUpsertDto dto, int? existingId)
        {
            if (dto.SiegeShopEntryId <= 0) throw new ArgumentException("An override needs the shop entry it changes.");
            var entry = await _repo.GetEntryByIdAsync(dto.SiegeShopEntryId)
                ?? throw new ArgumentException($"SiegeShopEntry with id {dto.SiegeShopEntryId} not found.");
            if (entry.SiegeScenarioId != null)
                throw new ArgumentException("Only a mode's default entries can be overridden; edit a scenario-only entry directly.");

            var same = await _repo.GetOverrideAsync(target.SiegeScenarioId, dto.SiegeShopEntryId);
            if (same != null && same.Id != existingId)
                throw new InvalidOperationException("This scenario already overrides that entry; edit that override instead.");

            if (dto.Price is < 0) throw new ArgumentException("price can't be negative.");
            if (dto.Quantity is < 1 or > MaxQuantity) throw new ArgumentException($"quantity must be between 1 and {MaxQuantity}.");
            if (dto.Quantity.HasValue && dto.Quantity.Value != 1 && entry.Kind == SiegeShopEntryKind.SiegeWeapon)
                throw new ArgumentException("A SiegeWeapon entry sells one weapon per purchase (quantity 1).");
            if (dto.MaxPerMatch is < 1) throw new ArgumentException("maxPerMatch must be at least 1 (leave it empty to keep the default).");
            if (dto.CooldownSeconds is < 0) throw new ArgumentException("cooldownSeconds can't be negative.");
            if (dto.PriceStep is < 0) throw new ArgumentException("priceStep can't be negative.");
            if (dto.CooldownStepSeconds is < 0) throw new ArgumentException("cooldownStepSeconds can't be negative.");
            if (dto.SortOrder is < 0) throw new ArgumentException("sortOrder can't be negative.");

            target.SiegeShopEntryId = dto.SiegeShopEntryId;
            target.Hidden = dto.Hidden;
            target.Price = dto.Price;
            target.Quantity = dto.Quantity;
            target.MaxPerMatch = dto.MaxPerMatch;
            target.CooldownSeconds = dto.CooldownSeconds;
            target.PriceStep = dto.PriceStep;
            target.CooldownStepSeconds = dto.CooldownStepSeconds;
            target.SortOrder = dto.SortOrder;
        }

        private async Task RequireScenarioAsync(int siegeScenarioId)
        {
            if (siegeScenarioId <= 0 || !await _repo.ScenarioExistsAsync(siegeScenarioId))
                throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
        }

        // ==== Effective catalogue (§ 5.3, P25) ====

        public async Task<SiegeShopCatalogueDto> GetCatalogueAsync(SiegeGameMode mode, int? siegeScenarioId)
        {
            if (!Enum.IsDefined(mode)) throw new ArgumentException($"Unknown game mode '{mode}'.");
            var scenarioId = siegeScenarioId is > 0 ? siegeScenarioId : null;
            if (siegeScenarioId.HasValue && scenarioId == null)
                throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
            if (scenarioId != null) await RequireScenarioAsync(scenarioId.Value);

            var entries = await _repo.GetEnabledEntriesForCatalogueAsync(mode, scenarioId);
            var overrides = scenarioId != null
                ? await _repo.GetOverridesAsync(scenarioId.Value)
                : new List<SiegeScenarioShopOverride>();
            return SiegeShopCatalogueResolver.Resolve(mode, scenarioId, entries, overrides);
        }

        // ==== Helpers ====

        private static string RequireText(string? value, string field, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException($"{field} is required.");
            if (trimmed.Length > maxLength) throw new ArgumentException($"{field} can be at most {maxLength} characters.");
            return trimmed;
        }

        private static string? OptionalText(string? value, string field, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            if (trimmed.Length > maxLength) throw new ArgumentException($"{field} can be at most {maxLength} characters.");
            return trimmed;
        }
    }

    /// <summary>
    /// § 5.3.1-5.3.2 as a pure function: the mode's enabled default entries, minus those the scenario hides, with the
    /// scenario's non-empty override fields applied, plus the scenario's own enabled entries; weapon prices fall back
    /// to the template's MatchPrice (P19, A16); categories without a visible entry are left out.
    /// </summary>
    public static class SiegeShopCatalogueResolver
    {
        public const string SourceDefault = "Default";
        public const string SourceOverridden = "Overridden";
        public const string SourceScenario = "Scenario";

        public static SiegeShopCatalogueDto Resolve(
            SiegeGameMode mode, int? siegeScenarioId,
            IEnumerable<SiegeShopEntry> entries, IEnumerable<SiegeScenarioShopOverride> overrides)
        {
            var byEntry = overrides.Where(o => siegeScenarioId != null && o.SiegeScenarioId == siegeScenarioId)
                .GroupBy(o => o.SiegeShopEntryId).ToDictionary(g => g.Key, g => g.First());

            var visible = new List<(SiegeShopEntry Entry, SiegeShopCatalogueEntryDto Dto)>();
            foreach (var entry in entries.Where(e => e.Enabled && e.GameMode == mode))
            {
                SiegeScenarioShopOverride? shopOverride = null;
                string source;
                if (entry.SiegeScenarioId == null)
                {
                    byEntry.TryGetValue(entry.Id, out shopOverride);
                    if (shopOverride?.Hidden == true) continue;
                    source = shopOverride != null && HasFieldOverride(shopOverride) ? SourceOverridden : SourceDefault;
                }
                else if (siegeScenarioId != null && entry.SiegeScenarioId == siegeScenarioId)
                {
                    source = SourceScenario;
                }
                else
                {
                    continue;
                }

                var price = shopOverride?.Price ?? entry.Price ?? entry.SiegeWeaponTemplate?.MatchPrice;
                if (price == null) continue; // an Item entry always has one (the service refuses it otherwise)

                visible.Add((entry, new SiegeShopCatalogueEntryDto
                {
                    Id = entry.Id,
                    CategoryId = entry.CategoryId,
                    Kind = entry.Kind,
                    Source = source,
                    ItemBlueprintId = entry.ItemBlueprintId,
                    ItemBlueprintName = entry.ItemBlueprint?.Name,
                    Quantity = shopOverride?.Quantity ?? entry.Quantity,
                    SiegeWeaponTemplateId = entry.SiegeWeaponTemplateId,
                    SiegeWeaponTemplateName = entry.SiegeWeaponTemplate?.Name,
                    DisplayName = entry.DisplayName,
                    IconMaterial = entry.IconMaterial,
                    Price = price.Value,
                    PriceStep = shopOverride?.PriceStep ?? entry.PriceStep,
                    PriceCurve = entry.PriceCurve,
                    PriceGrowthFactor = entry.PriceGrowthFactor,
                    PriceMax = entry.PriceMax,
                    CooldownSeconds = shopOverride?.CooldownSeconds ?? entry.CooldownSeconds,
                    CooldownStepSeconds = shopOverride?.CooldownStepSeconds ?? entry.CooldownStepSeconds,
                    CooldownCurve = entry.CooldownCurve,
                    CooldownGrowthFactor = entry.CooldownGrowthFactor,
                    CooldownMaxSeconds = entry.CooldownMaxSeconds,
                    MaxPerMatch = shopOverride?.MaxPerMatch ?? entry.MaxPerMatch,
                    SortOrder = shopOverride?.SortOrder ?? entry.SortOrder,
                }));
            }

            var categories = visible
                .Where(v => v.Entry.Category != null)
                .GroupBy(v => v.Entry.CategoryId)
                .Select(g =>
                {
                    var category = g.First().Entry.Category;
                    return new SiegeShopCatalogueCategoryDto
                    {
                        Id = category.Id,
                        Key = category.Key,
                        Name = category.Name,
                        IconMaterial = category.IconMaterial,
                        SortOrder = category.SortOrder,
                        Entries = g.Select(v => v.Dto).OrderBy(d => d.SortOrder).ThenBy(d => d.Id).ToList(),
                    };
                })
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
                .ToList();

            return new SiegeShopCatalogueDto { GameMode = mode, SiegeScenarioId = siegeScenarioId, Categories = categories };
        }

        private static bool HasFieldOverride(SiegeScenarioShopOverride o) =>
            o.Price.HasValue || o.Quantity.HasValue || o.MaxPerMatch.HasValue || o.CooldownSeconds.HasValue
            || o.PriceStep.HasValue || o.CooldownStepSeconds.HasValue || o.SortOrder.HasValue;
    }
}

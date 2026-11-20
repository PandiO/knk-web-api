using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    // Siege shop catalogue (siege-shop DESIGN.md § 6, PandiO/knk-workspace#867). Read side only: writes go through
    // SiegeShopService, where the rules are. The override and entry maps are also used by the scenario's read DTO
    // (SiegeMappingProfile); the effective catalogue is built by SiegeShopCatalogueResolver.
    public class SiegeShopMappingProfile : Profile
    {
        public SiegeShopMappingProfile()
        {
            CreateMap<SiegeShopCategory, SiegeShopCategoryReadDto>();

            CreateMap<SiegeShopEntry, SiegeShopEntryReadDto>()
                .ForMember(d => d.SiegeScenarioName, o => o.MapFrom(s => s.SiegeScenario != null ? s.SiegeScenario.Name : null))
                .ForMember(d => d.CategoryName, o => o.MapFrom(s => s.Category != null ? s.Category.Name : null))
                .ForMember(d => d.ItemBlueprintName, o => o.MapFrom(s => s.ItemBlueprint != null ? s.ItemBlueprint.Name : null))
                .ForMember(d => d.SiegeWeaponTemplateName, o => o.MapFrom(s => s.SiegeWeaponTemplate != null ? s.SiegeWeaponTemplate.Name : null));

            CreateMap<SiegeShopEntry, SiegeShopEntryListDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => EntryName(s)))
                .ForMember(d => d.SiegeScenarioName, o => o.MapFrom(s => s.SiegeScenario != null ? s.SiegeScenario.Name : null))
                .ForMember(d => d.CategoryName, o => o.MapFrom(s => s.Category != null ? s.Category.Name : null));

            CreateMap<SiegeScenarioShopOverride, SiegeScenarioShopOverrideReadDto>()
                .ForMember(d => d.SiegeShopEntryName, o => o.MapFrom(s => s.SiegeShopEntry != null ? EntryName(s.SiegeShopEntry) : null));
        }

        // The display name, else the target's name; what the dashboard and the override list show.
        public static string EntryName(SiegeShopEntry entry)
        {
            var name = !string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.DisplayName
                : entry.ItemBlueprint?.Name ?? entry.SiegeWeaponTemplate?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = $"Shop entry {entry.Id}";
            return entry.Quantity > 1 ? $"{name} x{entry.Quantity}" : name;
        }
    }
}

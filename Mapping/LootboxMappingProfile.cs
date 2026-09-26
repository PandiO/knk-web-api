using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    // Lootbox configuration (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 1). The nav DTOs it reuses
    // (CategoryNavDto, ItemBlueprintNavDto, MinecraftMaterialRefNavDto, GradeNavDto) are mapped by the ItemBlueprint
    // and Grade profiles, which the app's assembly scan registers alongside this one.
    public class LootboxMappingProfile : Profile
    {
        public LootboxMappingProfile()
        {
            // LootboxType -> LootboxTypeDto
            CreateMap<LootboxType, LootboxTypeDto>()
                .ForMember(dest => dest.Category, opt => opt.MapFrom(src => src.Category))
                .ForMember(dest => dest.DisplayMaterial, opt => opt.MapFrom(src => src.DisplayMaterial))
                .ForMember(dest => dest.GradeWeights, opt => opt.MapFrom(src => src.GradeWeights.OrderBy(w => w.GradeId)))
                .ForMember(dest => dest.PoolEntries, opt => opt.MapFrom(src => src.PoolEntries.OrderBy(p => p.ItemBlueprintId)))
                .ForMember(dest => dest.EnchantRolls, opt => opt.MapFrom(src => src.EnchantRolls.OrderBy(r => r.SortOrder).ThenBy(r => r.Id)));

            // LootboxTypeDto -> LootboxType: scalars only; the child collections are rebuilt by LootboxTypeService.
            CreateMap<LootboxTypeDto, LootboxType>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Category, opt => opt.Ignore())
                .ForMember(dest => dest.DisplayMaterial, opt => opt.Ignore())
                .ForMember(dest => dest.GradeWeights, opt => opt.Ignore())
                .ForMember(dest => dest.PoolEntries, opt => opt.Ignore())
                .ForMember(dest => dest.EnchantRolls, opt => opt.Ignore());

            CreateMap<LootboxType, LootboxTypeNavDto>();

            CreateMap<LootboxTypeGradeWeight, LootboxTypeGradeWeightDto>()
                .ForMember(dest => dest.Grade, opt => opt.MapFrom(src => src.Grade));

            CreateMap<LootboxPoolEntry, LootboxPoolEntryDto>()
                .ForMember(dest => dest.ItemBlueprint, opt => opt.MapFrom(src => src.ItemBlueprint))
                .ForMember(dest => dest.Mode, opt => opt.MapFrom(src => src.Mode.ToString()))
                .ForMember(dest => dest.GradeOverride, opt => opt.MapFrom(src => src.GradeOverride));

            CreateMap<LootboxEnchantRoll, LootboxEnchantRollDto>()
                .ForMember(dest => dest.EnchantmentKey, opt => opt.MapFrom(src => src.EnchantmentDefinition != null ? src.EnchantmentDefinition.Key : null));

            // LootboxSpecialEntry <-> LootboxSpecialEntryDto
            CreateMap<LootboxSpecialEntry, LootboxSpecialEntryDto>()
                .ForMember(dest => dest.LootboxType, opt => opt.MapFrom(src => src.LootboxType))
                .ForMember(dest => dest.ItemBlueprint, opt => opt.MapFrom(src => src.ItemBlueprint));

            CreateMap<LootboxSpecialEntryDto, LootboxSpecialEntry>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.LootboxType, opt => opt.Ignore())
                .ForMember(dest => dest.ItemBlueprint, opt => opt.Ignore());

            // LootboxSpawnArea <-> LootboxSpawnAreaDto
            CreateMap<LootboxSpawnArea, LootboxSpawnAreaDto>()
                .ForMember(dest => dest.AllowedTypes, opt => opt.MapFrom(src => src.AllowedTypes.OrderBy(t => t.LootboxTypeId)));

            CreateMap<LootboxSpawnAreaType, LootboxSpawnAreaTypeDto>()
                .ForMember(dest => dest.LootboxType, opt => opt.MapFrom(src => src.LootboxType));

            CreateMap<LootboxSpawnAreaDto, LootboxSpawnArea>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedByUser, opt => opt.Ignore())
                .ForMember(dest => dest.AllowedTypes, opt => opt.Ignore());

            // LootboxConfiguration -> LootboxConfigurationDto
            CreateMap<LootboxConfiguration, LootboxConfigurationDto>()
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.SpecifyKind(src.UpdatedAt, DateTimeKind.Utc)));
        }
    }
}

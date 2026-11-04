using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    // Read-only: instances have no create/update DTO (services mint them, DESIGN.md §3.2).
    public class ItemInstanceMappingProfile : Profile
    {
        public ItemInstanceMappingProfile()
        {
            CreateMap<ItemInstance, ItemInstanceDto>()
                .ForMember(dest => dest.ItemBlueprint, opt => opt.MapFrom(src => src.ItemBlueprint))
                .ForMember(dest => dest.Grade, opt => opt.MapFrom(src => src.Grade))
                .ForMember(dest => dest.OwnerUsername, opt => opt.MapFrom(src => src.OwnerUser != null ? src.OwnerUser.Username : null))
                .ForMember(dest => dest.Origin, opt => opt.MapFrom(src => src.Origin.ToString()))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.SpecifyKind(src.CreatedAt, DateTimeKind.Utc)))
                .ForMember(dest => dest.Enchantments, opt => opt.MapFrom(src => src.Enchantments.OrderBy(e => e.EnchantmentDefinitionId)))
                // #232: the look frozen at mint, layers bottom to top.
                .ForMember(dest => dest.ShieldBannerBaseColor, opt => opt.MapFrom(src => src.ShieldBannerBaseColor.HasValue ? src.ShieldBannerBaseColor.Value.ToString() : null))
                .ForMember(dest => dest.ShieldBannerLayers, opt => opt.MapFrom(src => src.ShieldBannerLayers.OrderBy(l => l.SortOrder).ThenBy(l => l.Id)))
                .ForMember(dest => dest.ArmorTrimPattern, opt => opt.MapFrom(src => src.ArmorTrimPattern))
                .ForMember(dest => dest.ArmorTrimMaterial, opt => opt.MapFrom(src => src.ArmorTrimMaterial));

            CreateMap<ItemInstanceBannerLayer, ItemBannerLayerDto>()
                .ForMember(dest => dest.PatternKey, opt => opt.MapFrom(src => src.PatternKey))
                .ForMember(dest => dest.Color, opt => opt.MapFrom(src => src.Color.ToString()));

            CreateMap<ItemInstanceEnchantment, ItemInstanceEnchantmentDto>()
                .ForMember(dest => dest.Key, opt => opt.MapFrom(src => src.EnchantmentDefinition != null ? src.EnchantmentDefinition.Key : string.Empty))
                .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.EnchantmentDefinition != null ? src.EnchantmentDefinition.DisplayName : string.Empty))
                .ForMember(dest => dest.IsCustom, opt => opt.MapFrom(src => src.EnchantmentDefinition != null && src.EnchantmentDefinition.IsCustom));
        }
    }
}

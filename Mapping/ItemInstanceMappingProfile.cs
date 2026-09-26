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
                .ForMember(dest => dest.Enchantments, opt => opt.MapFrom(src => src.Enchantments.OrderBy(e => e.EnchantmentDefinitionId)));

            CreateMap<ItemInstanceEnchantment, ItemInstanceEnchantmentDto>()
                .ForMember(dest => dest.Key, opt => opt.MapFrom(src => src.EnchantmentDefinition != null ? src.EnchantmentDefinition.Key : string.Empty))
                .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.EnchantmentDefinition != null ? src.EnchantmentDefinition.DisplayName : string.Empty))
                .ForMember(dest => dest.IsCustom, opt => opt.MapFrom(src => src.EnchantmentDefinition != null && src.EnchantmentDefinition.IsCustom));
        }
    }
}

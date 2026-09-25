using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class KitProfile : Profile
    {
        public KitProfile()
        {
            // Kit -> KitDto
            CreateMap<Kit, KitDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.HelmetId, opt => opt.MapFrom(src => src.HelmetId))
                .ForMember(dest => dest.ChestplateId, opt => opt.MapFrom(src => src.ChestplateId))
                .ForMember(dest => dest.LeggingsId, opt => opt.MapFrom(src => src.LeggingsId))
                .ForMember(dest => dest.BootsId, opt => opt.MapFrom(src => src.BootsId))
                .ForMember(dest => dest.ShieldId, opt => opt.MapFrom(src => src.ShieldId))
                .ForMember(dest => dest.HandId, opt => opt.MapFrom(src => src.HandId))
                .ForMember(dest => dest.Contents, opt => opt.MapFrom(src => src.Contents.OrderBy(c => c.SlotIndex)))
                .ForMember(dest => dest.MinTitleBracketId, opt => opt.MapFrom(src => src.MinTitleBracketId))
                .ForMember(dest => dest.RequiredPermissionGroupId, opt => opt.MapFrom(src => src.RequiredPermissionGroupId))
                .ForMember(dest => dest.RequiredPermissionNode, opt => opt.MapFrom(src => src.RequiredPermissionNode))
                .ForMember(dest => dest.GrantOnFirstJoin, opt => opt.MapFrom(src => src.GrantOnFirstJoin))
                .ForMember(dest => dest.CooldownSeconds, opt => opt.MapFrom(src => src.CooldownSeconds))
                .ForMember(dest => dest.CostAmount, opt => opt.MapFrom(src => src.CostAmount))
                .ForMember(dest => dest.CostCurrency, opt => opt.MapFrom(src => src.CostCurrency != null ? src.CostCurrency.ToString() : null))
                .ForMember(dest => dest.IsSinglePurchasePremium, opt => opt.MapFrom(src => src.IsSinglePurchasePremium))
                .ForMember(dest => dest.PremiumPriceGems, opt => opt.MapFrom(src => src.PremiumPriceGems));

            // KitDto -> Kit
            CreateMap<KitDto, Kit>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Helmet, opt => opt.Ignore())
                .ForMember(dest => dest.Chestplate, opt => opt.Ignore())
                .ForMember(dest => dest.Leggings, opt => opt.Ignore())
                .ForMember(dest => dest.Boots, opt => opt.Ignore())
                .ForMember(dest => dest.Shield, opt => opt.Ignore())
                .ForMember(dest => dest.Hand, opt => opt.Ignore())
                .ForMember(dest => dest.Contents, opt => opt.Ignore()) // handled in service (Phase 2)
                .ForMember(dest => dest.MinTitleBracket, opt => opt.Ignore())
                .ForMember(dest => dest.RequiredPermissionGroup, opt => opt.Ignore())
                .ForMember(dest => dest.CostCurrency, opt => opt.MapFrom(src =>
                    string.IsNullOrEmpty(src.CostCurrency) ? (KitCostCurrency?)null : Enum.Parse<KitCostCurrency>(src.CostCurrency, true)));

            // KitContent <-> KitContentDto
            CreateMap<KitContent, KitContentDto>()
                .ForMember(dest => dest.SlotIndex, opt => opt.MapFrom(src => src.SlotIndex))
                .ForMember(dest => dest.ItemBlueprintId, opt => opt.MapFrom(src => src.ItemBlueprintId))
                .ForMember(dest => dest.Quantity, opt => opt.MapFrom(src => src.Quantity));

            CreateMap<KitContentDto, KitContent>()
                .ForMember(dest => dest.Kit, opt => opt.Ignore())
                .ForMember(dest => dest.KitId, opt => opt.Ignore()) // set by the owning service call
                .ForMember(dest => dest.ItemBlueprint, opt => opt.Ignore());

            // KitContent -> KitContentSlotDto (resolved loadout shape, DESIGN.md §4.1)
            CreateMap<KitContent, KitContentSlotDto>()
                .ForMember(dest => dest.SlotIndex, opt => opt.MapFrom(src => src.SlotIndex))
                .ForMember(dest => dest.ItemBlueprintId, opt => opt.MapFrom(src => src.ItemBlueprintId))
                .ForMember(dest => dest.Quantity, opt => opt.MapFrom(src => src.Quantity));
        }
    }
}

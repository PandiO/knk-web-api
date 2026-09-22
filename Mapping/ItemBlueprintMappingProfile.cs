using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class ItemBlueprintMappingProfile : Profile
    {
        public ItemBlueprintMappingProfile()
        {
            // ItemBlueprint → Read DTO
            CreateMap<ItemBlueprint, ItemBlueprintReadDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.IconMaterialRefId, opt => opt.MapFrom(src => src.IconMaterialRefId))
                .ForMember(dest => dest.IconMaterialRef, opt => opt.MapFrom(src => src.IconMaterial))
                .ForMember(dest => dest.DefaultDisplayName, opt => opt.MapFrom(src => src.DefaultDisplayName))
                .ForMember(dest => dest.DefaultDisplayDescription, opt => opt.MapFrom(src => src.DefaultDisplayDescription))
                .ForMember(dest => dest.DefaultQuantity, opt => opt.MapFrom(src => src.DefaultQuantity))
                .ForMember(dest => dest.MaxStackSize, opt => opt.MapFrom(src => src.MaxStackSize))
                .ForMember(dest => dest.CategoryId, opt => opt.MapFrom(src => src.CategoryId))
                .ForMember(dest => dest.Category, opt => opt.MapFrom(src => src.Category))
                .ForMember(dest => dest.GradeId, opt => opt.MapFrom(src => src.GradeId))
                .ForMember(dest => dest.Grade, opt => opt.MapFrom(src => src.Grade))
                .ForMember(dest => dest.BasePriceMin, opt => opt.MapFrom(src => src.BasePriceMin))
                .ForMember(dest => dest.BasePriceMax, opt => opt.MapFrom(src => src.BasePriceMax))
                .ForMember(dest => dest.DefaultEnchantments, opt => opt.MapFrom(src => src.DefaultEnchantments))
                .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.Tags))
                .ForMember(dest => dest.Origins, opt => opt.MapFrom(src => src.Origins.OrderBy(o => o.SequenceNumber)));

            // Create/Update DTO → ItemBlueprint
            CreateMap<ItemBlueprintCreateDto, ItemBlueprint>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.DefaultEnchantments, opt => opt.Ignore()) // Handled in service
                .ForMember(dest => dest.Tags, opt => opt.Ignore()) // Handled in service
                .ForMember(dest => dest.Origins, opt => opt.Ignore()); // Handled in service

            CreateMap<ItemBlueprintUpdateDto, ItemBlueprint>()
                .ForMember(dest => dest.DefaultEnchantments, opt => opt.Ignore()) // Handled in service
                .ForMember(dest => dest.Tags, opt => opt.Ignore()) // Handled in service
                .ForMember(dest => dest.Origins, opt => opt.Ignore()); // Handled in service

            // ItemBlueprint → List DTO
            CreateMap<ItemBlueprint, ItemBlueprintListDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.DefaultDisplayName, opt => opt.MapFrom(src => src.DefaultDisplayName))
                .ForMember(dest => dest.IconMaterialRefId, opt => opt.MapFrom(src => src.IconMaterialRefId))
                .ForMember(dest => dest.IconNamespaceKey, opt => opt.MapFrom(src => src.IconMaterial != null ? src.IconMaterial.NamespaceKey : null))
                .ForMember(dest => dest.CategoryId, opt => opt.MapFrom(src => src.CategoryId))
                .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category != null ? src.Category.Name : null))
                .ForMember(dest => dest.GradeId, opt => opt.MapFrom(src => src.GradeId))
                .ForMember(dest => dest.GradeName, opt => opt.MapFrom(src => src.Grade != null ? src.Grade.Name : null))
                .ForMember(dest => dest.BasePriceMin, opt => opt.MapFrom(src => src.BasePriceMin))
                .ForMember(dest => dest.BasePriceMax, opt => opt.MapFrom(src => src.BasePriceMax))
                .ForMember(dest => dest.DefaultEnchantmentsCount, opt => opt.MapFrom(src => src.DefaultEnchantments != null ? src.DefaultEnchantments.Count : 0))
                .ForMember(dest => dest.TagsCount, opt => opt.MapFrom(src => src.Tags != null ? src.Tags.Count : 0));

            // Category → Nav DTO
            CreateMap<Category, CategoryNavDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name));

            // ItemBlueprint → Nav DTO
            CreateMap<ItemBlueprint, ItemBlueprintNavDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.IconMaterialRefId, opt => opt.MapFrom(src => src.IconMaterialRefId))
                .ForMember(dest => dest.DefaultDisplayName, opt => opt.MapFrom(src => src.DefaultDisplayName));

            // Join entity mappings
            CreateMap<ItemBlueprintDefaultEnchantment, ItemBlueprintDefaultEnchantmentDto>()
                .ForMember(dest => dest.ItemBlueprintId, opt => opt.MapFrom(src => src.ItemBlueprintId))
                .ForMember(dest => dest.EnchantmentDefinitionId, opt => opt.MapFrom(src => src.EnchantmentDefinitionId))
                .ForMember(dest => dest.EnchantmentDefinition, opt => opt.MapFrom(src => src.EnchantmentDefinition))
                .ForMember(dest => dest.Level, opt => opt.MapFrom(src => src.Level));

            // MinecraftMaterialRef → Nav DTO
            CreateMap<MinecraftMaterialRef, MinecraftMaterialRefNavDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.NamespaceKey, opt => opt.MapFrom(src => src.NamespaceKey))
                .ForMember(dest => dest.LegacyName, opt => opt.MapFrom(src => src.LegacyName))
                .ForMember(dest => dest.IconUrl, opt => opt.MapFrom(src => src.IconUrl));

            // ItemBlueprintTag join entity → Dto
            CreateMap<ItemBlueprintTag, ItemBlueprintTagDto>()
                .ForMember(dest => dest.ItemBlueprintId, opt => opt.MapFrom(src => src.ItemBlueprintId))
                .ForMember(dest => dest.TagId, opt => opt.MapFrom(src => src.TagId))
                .ForMember(dest => dest.Tag, opt => opt.MapFrom(src => src.Tag));

            // ItemBlueprintOrigin join entity → Dto
            CreateMap<ItemBlueprintOrigin, ItemBlueprintOriginDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.ItemBlueprintId, opt => opt.MapFrom(src => src.ItemBlueprintId))
                .ForMember(dest => dest.DomainId, opt => opt.MapFrom(src => src.DomainId))
                .ForMember(dest => dest.Domain, opt => opt.MapFrom(src => src.Domain))
                .ForMember(dest => dest.SequenceNumber, opt => opt.MapFrom(src => src.SequenceNumber));

            // Domain → Nav DTO (id/name/subtype - same GetType().Name convention as DomainMappingProfile)
            CreateMap<Domain, DomainNavDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.DomainType, opt => opt.MapFrom(src => src.GetType().Name));
        }
    }
}

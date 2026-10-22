using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.MinecraftCatalog;

namespace knkwebapi_v2.Mapping;

public class MinecraftEnchantmentRefProfile : Profile
{
    public MinecraftEnchantmentRefProfile()
    {
        // Read DTOs
        CreateMap<MinecraftEnchantmentRef, MinecraftEnchantmentRefDto>();
        CreateMap<MinecraftEnchantmentRef, MinecraftEnchantmentRefListDto>()
            .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.DisplayName ?? MinecraftNamespaceKey.DisplayNameFromKey(src.NamespaceKey)));
        CreateMap<PagedResult<MinecraftEnchantmentRef>, PagedResultDto<MinecraftEnchantmentRefListDto>>();

        // Create DTOs
        CreateMap<MinecraftEnchantmentRefCreateDto, MinecraftEnchantmentRef>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.IsCustom, opt => opt.Ignore()) // Set by service
            .ForMember(dest => dest.CatalogStatus, opt => opt.Ignore())
            .ForMember(dest => dest.FirstSeenVersion, opt => opt.Ignore())
            .ForMember(dest => dest.LastSeenVersion, opt => opt.Ignore())
            .ForMember(dest => dest.LastScanId, opt => opt.Ignore());

        // Update DTOs
        CreateMap<MinecraftEnchantmentRefUpdateDto, MinecraftEnchantmentRef>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.NamespaceKey, opt => opt.Ignore())
            .ForMember(dest => dest.IsCustom, opt => opt.Ignore())
            .ForMember(dest => dest.CatalogStatus, opt => opt.Ignore())
            .ForMember(dest => dest.FirstSeenVersion, opt => opt.Ignore())
            .ForMember(dest => dest.LastSeenVersion, opt => opt.Ignore())
            .ForMember(dest => dest.LastScanId, opt => opt.Ignore());
    }
}

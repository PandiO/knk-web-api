using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.MinecraftCatalog;

namespace knkwebapi_v2.Mapping
{
    public class MinecraftMaterialRefMappingProfile : Profile
    {
        public MinecraftMaterialRefMappingProfile()
        {
            // KNG-200: the shown icon is the manual override when there is one.
            CreateMap<MinecraftMaterialRef, MinecraftMaterialRefDto>()
                .ForMember(dest => dest.IconUrl, opt => opt.MapFrom(src => src.IconUrlOverride ?? src.IconUrl));
            CreateMap<MinecraftMaterialRefCreateDto, MinecraftMaterialRef>()
                .ForMember(dest => dest.IconUrl, opt => opt.MapFrom(src => src.IconUrl));
            CreateMap<MinecraftMaterialRefUpdateDto, MinecraftMaterialRef>()
                .ForMember(dest => dest.IconUrl, opt => opt.MapFrom(src => src.IconUrl));
            // A database row is saved by definition; the picker shows a "New" badge only for unsaved catalogue rows.
            CreateMap<MinecraftMaterialRef, MinecraftMaterialRefListDto>()
                .ForMember(dest => dest.IconUrl, opt => opt.MapFrom(src => src.IconUrlOverride ?? src.IconUrl))
                .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.DisplayName ?? MinecraftNamespaceKey.DisplayNameFromKey(src.NamespaceKey)))
                .ForMember(dest => dest.IsPersisted, opt => opt.MapFrom(_ => true));

            CreateMap<PagedResult<MinecraftMaterialRef>, PagedResultDto<MinecraftMaterialRefListDto>>()
                .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.Items))
                .ForMember(dest => dest.PageNumber, opt => opt.MapFrom(src => src.PageNumber))
                .ForMember(dest => dest.PageSize, opt => opt.MapFrom(src => src.PageSize))
                .ForMember(dest => dest.TotalCount, opt => opt.MapFrom(src => src.TotalCount));
        }
    }
}

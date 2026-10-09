using System.Linq;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class DistrictMappingProfile : Profile
    {
        public DistrictMappingProfile()
        {
            CreateMap<District, DistrictDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.AllowEntry, src => src.MapFrom(src => src.AllowEntry))
                .ForMember(dest => dest.AllowExit, src => src.MapFrom(src => src.AllowExit))
                .ForMember(dest => dest.WgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.LocationId, src => src.MapFrom(src => src.LocationId))
                // Map embedded Location as full DTO (uses LocationMappingProfile)
                .ForMember(dest => dest.Location, src => src.MapFrom(src => src.Location))
                .ForMember(dest => dest.TownId, src => src.MapFrom(src => src.TownId))
                .ForMember(dest => dest.StreetIds, src => src.MapFrom(src => src.Streets.Select(s => s.Id).ToList()))
                // Map embedded Town as lightweight DTO to avoid cycles
                .ForMember(dest => dest.Town, src => src.MapFrom(s => s.Town == null ? null : new DistrictTownDto
                {
                    Id = s.Town.Id,
                    Name = s.Town.Name,
                    Description = s.Town.Description,
                    AllowEntry = s.Town.AllowEntry,
                    AllowExit = s.Town.AllowExit,
                    WgRegionId = s.Town.WgRegionId,
                    LocationId = s.Town.LocationId
                }))
                // Map embedded Streets as lightweight DTOs
                .ForMember(dest => dest.Streets, src => src.MapFrom(s => s.Streets.Select(st => new DistrictStreetDto
                {
                    Id = st.Id,
                    Name = st.Name
                }).ToList()))
                // Map embedded Structures as lightweight DTOs
                .ForMember(dest => dest.Structures, src => src.MapFrom(s => s.Structures.Select(st => new DistrictStructureDto
                {
                    Id = st.Id,
                    Name = st.Name,
                    Description = st.Description,
                    HouseNumber = st.HouseNumber,
                    StreetId = st.StreetId
                }).ToList()));

            CreateMap<DistrictDto, District>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => src.CreatedAt ?? System.DateTime.UtcNow))
                .ForMember(dest => dest.AllowEntry, src => src.MapFrom(src => src.AllowEntry ?? true))
                .ForMember(dest => dest.AllowExit, src => src.MapFrom(src => src.AllowExit ?? true))
                .ForMember(dest => dest.WgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.LocationId, src => src.MapFrom(src => src.LocationId))
                .ForMember(dest => dest.Location, src => src.Ignore())
                .ForMember(dest => dest.TownId, src => src.MapFrom(src => src.TownId))
                .ForMember(dest => dest.Town, src => src.Ignore())
                .ForMember(dest => dest.Streets, src => src.Ignore())
                .ForMember(dest => dest.Structures, src => src.Ignore())
                .IgnoreTeleportSettings()
                .IgnoreNavigationDefault();

            CreateMap<District, DistrictListDto>()
                .ForMember(dest => dest.id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.wgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.townId, src => src.MapFrom(src => src.TownId))
                .ForMember(dest => dest.townName, src => src.MapFrom(src => src.Town != null ? src.Town.Name : null));
        }
    }
}

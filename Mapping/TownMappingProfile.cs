using System.Linq;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class TownMappingProfile : Profile
    {
        public TownMappingProfile()
        {
            CreateMap<Town, TownDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.AllowEntry, src => src.MapFrom(src => src.AllowEntry))
                .ForMember(dest => dest.AllowExit, src => src.MapFrom(src => src.AllowExit))
                .ForMember(dest => dest.WgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.LocationId, src => src.MapFrom(src => src.LocationId))
                .ForMember(dest => dest.StreetIds, src => src.MapFrom(src => src.Streets.Select(s => s.Id).ToList()))
                // Map embedded Streets as lightweight DTOs
                .ForMember(dest => dest.Streets, src => src.MapFrom(src => src.Streets.Select(s => new TownStreetDto
                {
                    Id = s.Id,
                    Name = s.Name
                }).ToList()))
                // Map embedded Districts as lightweight DTOs
                .ForMember(dest => dest.Districts, src => src.MapFrom(src => src.Districts.Select(d => new TownDistrictDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    Description = d.Description,
                    AllowEntry = d.AllowEntry,
                    AllowExit = d.AllowExit,
                    WgRegionId = d.WgRegionId,
                    WorldName = d.WorldName
                }).ToList()));

            CreateMap<TownDto, Town>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => src.CreatedAt ?? System.DateTime.UtcNow))
                .ForMember(dest => dest.AllowEntry, src => src.MapFrom(src => src.AllowEntry ?? true))
                .ForMember(dest => dest.AllowExit, src => src.MapFrom(src => src.AllowExit ?? true))
                .ForMember(dest => dest.WgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.LocationId, src => src.MapFrom(src => src.LocationId))
                .ForMember(dest => dest.Location, src => src.Ignore())
                .ForMember(dest => dest.Streets, src => src.Ignore())
                .ForMember(dest => dest.Districts, src => src.Ignore())
                .IgnoreTeleportSettings()
                .IgnoreNavigationDefault();

            CreateMap<Town, TownListDto>()
                .ForMember(dest => dest.id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.wgRegionId, src => src.MapFrom(src => src.WgRegionId));
        }
    }
}

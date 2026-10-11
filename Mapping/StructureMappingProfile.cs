using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class StructureMappingProfile : Profile
    {
        public StructureMappingProfile()
        {
            CreateMap<Structure, StructureDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.AllowEntry, src => src.MapFrom(src => src.AllowEntry))
                .ForMember(dest => dest.AllowExit, src => src.MapFrom(src => src.AllowExit))
                .ForMember(dest => dest.WgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.LocationId, src => src.MapFrom(src => src.LocationId))
                .ForMember(dest => dest.StreetId, src => src.MapFrom(src => src.StreetId))
                .ForMember(dest => dest.DistrictId, src => src.MapFrom(src => src.DistrictId))
                .ForMember(dest => dest.HouseNumber, src => src.MapFrom(src => src.HouseNumber))
                // Embedded lightweight navigations
                .ForMember(dest => dest.Street, src => src.MapFrom(s => s.Street == null ? null : new StructureStreetDto
                {
                    Id = s.Street.Id,
                    Name = s.Street.Name
                }))
                .ForMember(dest => dest.District, src => src.MapFrom(s => s.District == null ? null : new StructureDistrictDto
                {
                    Id = s.District.Id,
                    Name = s.District.Name,
                    Description = s.District.Description,
                    AllowEntry = s.District.AllowEntry,
                    AllowExit = s.District.AllowExit,
                    WgRegionId = s.District.WgRegionId
                }));

            CreateMap<StructureDto, Structure>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => src.CreatedAt ?? System.DateTime.UtcNow))
                .ForMember(dest => dest.AllowEntry, src => src.MapFrom(src => src.AllowEntry ?? true))
                .ForMember(dest => dest.AllowExit, src => src.MapFrom(src => src.AllowExit ?? true))
                .ForMember(dest => dest.WgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.LocationId, src => src.MapFrom(src => src.LocationId))
                .ForMember(dest => dest.Location, src => src.Ignore())
                .ForMember(dest => dest.StreetId, src => src.MapFrom(src => src.StreetId))
                .ForMember(dest => dest.Street, src => src.Ignore())
                .ForMember(dest => dest.DistrictId, src => src.MapFrom(src => src.DistrictId))
                .ForMember(dest => dest.District, src => src.Ignore())
                .ForMember(dest => dest.HouseNumber, src => src.MapFrom(src => src.HouseNumber))
                .IgnoreTeleportSettings()
                .IgnoreNavigationDefault();

            CreateMap<Structure, StructureListDto>()
                .ForMember(dest => dest.id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.wgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.houseNumber, src => src.MapFrom(src => src.HouseNumber))
                .ForMember(dest => dest.streetId, src => src.MapFrom(src => src.StreetId))
                .ForMember(dest => dest.streetName, src => src.MapFrom(src => src.Street != null ? src.Street.Name : null))
                .ForMember(dest => dest.districtId, src => src.MapFrom(src => src.DistrictId))
                .ForMember(dest => dest.districtName, src => src.MapFrom(src => src.District != null ? src.District.Name : null));
        }
    }
}

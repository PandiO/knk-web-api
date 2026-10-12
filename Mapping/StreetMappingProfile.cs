using System.Linq;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class StreetMappingProfile : Profile
    {
        /// <summary>KNG-166: ", "-joined distinct names, sorted; null when there are none.</summary>
        private static string? JoinNames(System.Collections.Generic.IEnumerable<string?> names)
        {
            var list = names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .Distinct(System.StringComparer.Ordinal)
                .OrderBy(name => name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            return list.Count == 0 ? null : string.Join(", ", list);
        }

        public StreetMappingProfile()
        {
            CreateMap<Street, StreetDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Districts, src => src.MapFrom(src => src.Districts))
                // KNG-135: filled on read too, so a form field authored as DistrictIds loads the Street's Districts.
                .ForMember(dest => dest.DistrictIds, src => src.MapFrom(src => src.Districts.Select(d => d.Id).ToList()))
                .ForMember(dest => dest.Structures, src => src.MapFrom(src => src.Structures))
                .ForMember(dest => dest.EdgeCount, src => src.Ignore())
                .ForMember(dest => dest.TotalLength, src => src.Ignore());

            CreateMap<StreetDto, Street>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Districts, src => src.Ignore())
                .ForMember(dest => dest.Structures, src => src.Ignore());

            CreateMap<Street, StreetListDto>()
                .ForMember(dest => dest.id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.districtNames, src => src.MapFrom(src => JoinNames(src.Districts.Select(d => d.Name))))
                .ForMember(dest => dest.townNames, src => src.MapFrom(src => JoinNames(src.Districts.Select(d => d.Town != null ? d.Town.Name : null))));

            // Map District to StreetDistrictDto for embedded District collections in Street
            CreateMap<District, StreetDistrictDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.AllowEntry, src => src.MapFrom(src => src.AllowEntry))
                .ForMember(dest => dest.AllowExit, src => src.MapFrom(src => src.AllowExit))
                .ForMember(dest => dest.WgRegionId, src => src.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.Town, src => src.MapFrom(src => src.Town));

            CreateMap<Town, StreetDistrictTownDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name));

            // Map Structure to StreetStructureDto for embedded Structure collections in Street
            CreateMap<Structure, StreetStructureDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, src => src.MapFrom(src => src.Description))
                .ForMember(dest => dest.HouseNumber, src => src.MapFrom(src => src.HouseNumber))
                .ForMember(dest => dest.DistrictId, src => src.MapFrom(src => src.DistrictId));
        }
    }

    // Extension method for dynamic shaping (optional, can be used in services)
    public static class StreetMappingExtensions
    {
        // Note: Actual shaping will happen in StreetService similar to DistrictService
    }
}

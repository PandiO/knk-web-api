using System.Linq;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    /// <summary>
    /// Landmarks (landmark-domain DESIGN § 6.2, PandiO/knk-workspace#983). Writes go through LandmarkService, which sets
    /// every field itself. The #708 names (<c>domainId</c>, <c>extentKind</c>, <c>regionId</c>, …) are filled from the new
    /// fields for knk-plugin until L3 (#985): see <see cref="AreaMainRegion"/>.
    /// </summary>
    public class LandmarkMappingProfile : Profile
    {
        public LandmarkMappingProfile()
        {
            CreateMap<Landmark, LandmarkReadDto>()
                .ForMember(d => d.DomainType, o => o.MapFrom(s => nameof(Landmark)))
                .ForMember(d => d.LocationId, o => o.MapFrom(s => s.LocationId ?? 0))
                .ForMember(d => d.World, o => o.MapFrom(s => s.Location != null ? s.Location.World : null))
                .ForMember(d => d.AreaKind, o => o.MapFrom(s => s.AreaKind.ToString()))
                .ForMember(d => d.SubRegionIds, o => o.MapFrom(s => s.SubRegions.OrderBy(r => r.Id).Select(r => r.WgRegionId).ToList()))
                .ForMember(d => d.SubRegions, o => o.MapFrom(s => s.SubRegions.OrderBy(r => r.Id)))
                .ForMember(d => d.ParentDomain, o => o.MapFrom(s => s.ParentDomain))
                .ForMember(d => d.ParentDomainName, o => o.MapFrom(s => s.ParentDomain != null ? s.ParentDomain.Name : null))
                .ForMember(d => d.Kind, o => o.MapFrom(s => s.Kind.ToString()))
                .ForMember(d => d.DomainId, o => o.MapFrom(s => s.ParentDomainId))
                .ForMember(d => d.Domain, o => o.MapFrom(s => s.ParentDomain))
                .ForMember(d => d.DomainName, o => o.MapFrom(s => s.ParentDomain != null ? s.ParentDomain.Name : null))
                .ForMember(d => d.ExtentKind, o => o.MapFrom(s => AreaMainRegion(s) != null ? "Region" : "None"))
                .ForMember(d => d.RegionId, o => o.MapFrom(s => AreaMainRegion(s)))
                .ForMember(d => d.Radius, o => o.MapFrom(s => (double?)null));

            CreateMap<Landmark, LandmarkListDto>()
                .ForMember(d => d.World, o => o.MapFrom(s => s.Location != null ? s.Location.World : null))
                .ForMember(d => d.X, o => o.MapFrom(s => s.Location != null ? (double?)s.Location.X : null))
                .ForMember(d => d.Y, o => o.MapFrom(s => s.Location != null ? (double?)s.Location.Y : null))
                .ForMember(d => d.Z, o => o.MapFrom(s => s.Location != null ? (double?)s.Location.Z : null))
                .ForMember(d => d.AreaKind, o => o.MapFrom(s => s.AreaKind.ToString()))
                .ForMember(d => d.ParentDomainName, o => o.MapFrom(s => s.ParentDomain != null ? s.ParentDomain.Name : null))
                .ForMember(d => d.ParentDomainType, o => o.MapFrom(s => s.ParentDomain != null ? s.ParentDomain.GetType().Name : null))
                .ForMember(d => d.Kind, o => o.MapFrom(s => s.Kind.ToString()))
                .ForMember(d => d.DomainId, o => o.MapFrom(s => s.ParentDomainId))
                .ForMember(d => d.DomainName, o => o.MapFrom(s => s.ParentDomain != null ? s.ParentDomain.Name : null))
                .ForMember(d => d.DomainType, o => o.MapFrom(s => s.ParentDomain != null ? s.ParentDomain.GetType().Name : null))
                .ForMember(d => d.ExtentKind, o => o.MapFrom(s => AreaMainRegion(s) != null ? "Region" : "None"));

            CreateMap<Landmark, LandmarkNavDto>();

            CreateMap<Domain, LandmarkDomainNavDto>()
                .ForMember(d => d.DomainType, o => o.MapFrom(s => s.GetType().Name));
        }

        /// <summary>
        /// The main region of the landmark's area: its own region (<c>OwnRegion</c>), the parent's main region
        /// (<c>LinkedDomain</c>, read live, LD07), or null for a point. Only the #708 compatibility fields use it.
        /// </summary>
        public static string? AreaMainRegion(Landmark landmark) => landmark.AreaKind switch
        {
            LandmarkAreaKind.OwnRegion => string.IsNullOrWhiteSpace(landmark.WgRegionId) ? null : landmark.WgRegionId,
            LandmarkAreaKind.LinkedDomain => string.IsNullOrWhiteSpace(landmark.ParentDomain?.WgRegionId) ? null : landmark.ParentDomain!.WgRegionId,
            _ => null
        };
    }
}

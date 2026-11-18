using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    /// <summary>Landmarks (PandiO/knk-workspace#708). Writes go through LandmarkService, which sets every field itself.</summary>
    public class LandmarkMappingProfile : Profile
    {
        public LandmarkMappingProfile()
        {
            CreateMap<Landmark, LandmarkReadDto>()
                .ForMember(d => d.World, o => o.MapFrom(s => s.Location != null ? s.Location.World : null))
                .ForMember(d => d.DomainName, o => o.MapFrom(s => s.Domain != null ? s.Domain.Name : null))
                .ForMember(d => d.Domain, o => o.MapFrom(s => s.Domain))
                .ForMember(d => d.ExtentKind, o => o.MapFrom(s => s.ExtentKind.ToString()))
                .ForMember(d => d.Kind, o => o.MapFrom(s => s.Kind.ToString()));

            CreateMap<Landmark, LandmarkListDto>()
                .ForMember(d => d.World, o => o.MapFrom(s => s.Location != null ? s.Location.World : null))
                .ForMember(d => d.X, o => o.MapFrom(s => s.Location != null ? (double?)s.Location.X : null))
                .ForMember(d => d.Y, o => o.MapFrom(s => s.Location != null ? (double?)s.Location.Y : null))
                .ForMember(d => d.Z, o => o.MapFrom(s => s.Location != null ? (double?)s.Location.Z : null))
                .ForMember(d => d.DomainName, o => o.MapFrom(s => s.Domain != null ? s.Domain.Name : null))
                .ForMember(d => d.DomainType, o => o.MapFrom(s => s.Domain != null ? s.Domain.GetType().Name : null))
                .ForMember(d => d.ExtentKind, o => o.MapFrom(s => s.ExtentKind.ToString()))
                .ForMember(d => d.Kind, o => o.MapFrom(s => s.Kind.ToString()));

            CreateMap<Landmark, LandmarkNavDto>();

            CreateMap<Domain, LandmarkDomainNavDto>()
                .ForMember(d => d.DomainType, o => o.MapFrom(s => s.GetType().Name));
        }
    }
}

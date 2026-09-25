using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    // Siege Phase 1: BannerDesign / BannerLayer / Clan. Read-side only - writes go through
    // BannerDesignService / ClanService explicitly (validation + normalization happen there).
    public class ClanMappingProfile : Profile
    {
        public const int SurvivalLoomLayerLimit = 6;

        public ClanMappingProfile()
        {
            CreateMap<BannerLayer, BannerLayerDto>();

            CreateMap<BannerDesign, BannerDesignReadDto>()
                .ForMember(d => d.Layers, o => o.MapFrom(s => s.Layers.OrderBy(l => l.SortOrder).ThenBy(l => l.Id)))
                .ForMember(d => d.ExceedsSurvivalLoomLimit, o => o.MapFrom(s => s.Layers.Count > SurvivalLoomLayerLimit));

            CreateMap<BannerDesign, BannerDesignListDto>()
                .ForMember(d => d.LayerCount, o => o.MapFrom(s => s.Layers.Count));

            CreateMap<Clan, ClanReadDto>()
                .ForMember(d => d.DefaultForTownName, o => o.MapFrom(s => s.DefaultForTown != null ? s.DefaultForTown.Name : null));

            CreateMap<Clan, ClanListDto>()
                .ForMember(d => d.BannerDesignName, o => o.MapFrom(s => s.BannerDesign != null ? s.BannerDesign.Name : null))
                .ForMember(d => d.DefaultForTownName, o => o.MapFrom(s => s.DefaultForTown != null ? s.DefaultForTown.Name : null));
        }
    }
}

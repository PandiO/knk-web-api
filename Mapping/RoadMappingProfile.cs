using System.Collections.Generic;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Roads;

namespace knkwebapi_v2.Mapping
{
    // Road navigation (docs/specs/navigation/DESIGN.md §3). Read-side only - writes go through
    // RoadNetworkService explicitly (validation happens there). JSON columns become lists/arrays
    // through JsonColumn (plan R29); RoadEdge.Flags becomes its flag names.
    public class RoadMappingProfile : Profile
    {
        public RoadMappingProfile()
        {
            CreateMap<RoadProfile, RoadProfileDto>()
                .ForMember(d => d.Materials, o => o.MapFrom(s => JsonColumn.DeserializeList<RoadMaterialDto>(s.MaterialsJson)))
                .ForMember(d => d.ScopeTownIds, o => o.MapFrom(s => s.ScopeTownIdsJson == null ? null : JsonColumn.DeserializeList<int>(s.ScopeTownIdsJson)))
                .ForMember(d => d.Stats, o => o.MapFrom(s => RoadJson.Element(s.StatsJson)));

            CreateMap<RoadSurvey, RoadSurveyDto>()
                .ForMember(d => d.Breadcrumb, o => o.MapFrom(s => JsonColumn.DeserializeList<RoadBreadcrumbPointDto>(s.BreadcrumbJson)))
                .ForMember(d => d.Stats, o => o.MapFrom(s => RoadJson.Element(s.StatsJson)));

            CreateMap<RoadTile, RoadTileDto>()
                .ForMember(d => d.Warnings, o => o.MapFrom(s => RoadJson.StringList(s.WarningsJson)));

            CreateMap<RoadSeed, RoadSeedDto>();

            CreateMap<RoadNode, RoadNodeDto>();

            CreateMap<RoadEdge, RoadEdgeDto>()
                .ForMember(d => d.Geometry, o => o.MapFrom(s => RoadJson.Geometry(s.GeometryJson)))
                .ForMember(d => d.Flags, o => o.MapFrom(s => RoadJson.FlagNames(s.Flags)))
                .ForMember(d => d.GateDoorIds, o => o.MapFrom(s => RoadJson.IntList(s.GateDoorIdsJson)))
                .ForMember(d => d.DomainIds, o => o.MapFrom(s => RoadJson.IntList(s.DomainIdsJson)))
                .ForMember(d => d.RegionIds, o => o.MapFrom(s => RoadJson.StringList(s.RegionIdsJson)));
        }
    }
}

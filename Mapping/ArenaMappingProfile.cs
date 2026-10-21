using System.Linq;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    /// <summary>
    /// Arena duels (KNG-210): entity → DTO for arenas, battlegrounds, spectator areas and eligible kits. Writes go
    /// through ArenaService, which applies each DTO field after validation (a null setting keeps its value).
    /// </summary>
    public class ArenaMappingProfile : Profile
    {
        public ArenaMappingProfile()
        {
            CreateMap<Arena, ArenaDto>()
                .ForMember(d => d.DefaultGearMode, o => o.MapFrom(s => s.DefaultGearMode.ToString()))
                .ForMember(d => d.NavigationDefaultOverride, o => o.MapFrom(s => s.NavigationDefaultOverride == null ? null : s.NavigationDefaultOverride.ToString()))
                .ForMember(d => d.RoadAccessOverride, o => o.MapFrom(s => s.RoadAccessOverride == null ? null : s.RoadAccessOverride.ToString()))
                .ForMember(d => d.Street, o => o.MapFrom(s => s.Street == null ? null : new StructureStreetDto { Id = s.Street.Id, Name = s.Street.Name }))
                .ForMember(d => d.District, o => o.MapFrom(s => s.District == null ? null : new StructureDistrictDto
                {
                    Id = s.District.Id,
                    Name = s.District.Name,
                    Description = s.District.Description,
                    AllowEntry = s.District.AllowEntry,
                    AllowExit = s.District.AllowExit,
                    WgRegionId = s.District.WgRegionId,
                    WorldName = s.District.WorldName,
                    TownName = s.District.Town != null ? s.District.Town.Name : null
                }))
                .ForMember(d => d.EligibleKits, o => o.MapFrom(s => s.EligibleKits.OrderBy(k => k.SequenceNumber).ThenBy(k => k.KitId)))
                .ForMember(d => d.Battlegrounds, o => o.MapFrom(s => s.Battlegrounds.OrderBy(b => b.SortOrder).ThenBy(b => b.Id)))
                .ForMember(d => d.SpectatorAreas, o => o.MapFrom(s => s.SpectatorAreas.OrderBy(a => a.Id)));

            CreateMap<Arena, ArenaListDto>()
                .ForMember(d => d.DistrictName, o => o.MapFrom(s => s.District != null ? s.District.Name : null))
                .ForMember(d => d.TownName, o => o.MapFrom(s => s.District != null && s.District.Town != null ? s.District.Town.Name : null))
                .ForMember(d => d.BattlegroundCount, o => o.MapFrom(s => s.Battlegrounds.Count));

            CreateMap<ArenaKit, ArenaKitDto>()
                .ForMember(d => d.KitName, o => o.MapFrom(s => s.Kit != null ? s.Kit.Name : null));

            CreateMap<ArenaBattleground, ArenaBattlegroundDto>()
                .ForMember(d => d.NavigationDefaultOverride, o => o.MapFrom(s => s.NavigationDefaultOverride == null ? null : s.NavigationDefaultOverride.ToString()))
                .ForMember(d => d.RoadAccessOverride, o => o.MapFrom(s => s.RoadAccessOverride == null ? null : s.RoadAccessOverride.ToString()))
                .ForMember(d => d.ArenaName, o => o.MapFrom(s => s.Arena != null ? s.Arena.Name : null));

            CreateMap<ArenaSpectatorArea, ArenaSpectatorAreaDto>()
                .ForMember(d => d.NavigationDefaultOverride, o => o.MapFrom(s => s.NavigationDefaultOverride == null ? null : s.NavigationDefaultOverride.ToString()))
                .ForMember(d => d.RoadAccessOverride, o => o.MapFrom(s => s.RoadAccessOverride == null ? null : s.RoadAccessOverride.ToString()))
                .ForMember(d => d.ArenaName, o => o.MapFrom(s => s.Arena != null ? s.Arena.Name : null));
        }
    }
}

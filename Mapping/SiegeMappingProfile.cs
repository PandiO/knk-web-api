using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    // Siege Phase 2 (docs/specs/siege-minigame/DESIGN.md §3.3–3.8). Read-side only - writes go
    // through SiegeScenarioService / SiegeLobbyService explicitly (validation happens there), and
    // the runtime-config payload is assembled by SiegeLobbyService. Location -> LocationDto comes
    // from LocationMappingProfile.
    public class SiegeMappingProfile : Profile
    {
        public SiegeMappingProfile()
        {
            CreateMap<SiegeScenarioDistrict, SiegeScenarioDistrictDto>()
                .ForMember(d => d.DistrictName, o => o.MapFrom(s => s.District != null ? s.District.Name : null));

            CreateMap<SiegeScenarioGate, SiegeScenarioGateDto>()
                .ForMember(d => d.GateStructureName, o => o.MapFrom(s => s.GateStructure != null ? s.GateStructure.Name : null));

            CreateMap<SiegeSpawnpoint, SiegeSpawnpointReadDto>();

            CreateMap<SiegeTeam, SiegeTeamReadDto>()
                .ForMember(d => d.ClanName, o => o.MapFrom(s => s.Clan != null ? s.Clan.Name : null))
                .ForMember(d => d.ResolvedName, o => o.MapFrom(s => SiegeTeamIdentity.ResolveName(s)))
                .ForMember(d => d.ResolvedChatColor, o => o.MapFrom(s => SiegeTeamIdentity.ResolveChatColor(s)))
                .ForMember(d => d.ResolvedBannerDesignId, o => o.MapFrom(s => SiegeTeamIdentity.ResolveBannerDesignId(s)))
                .ForMember(d => d.Spawnpoints, o => o.MapFrom(s => s.Spawnpoints.OrderBy(p => p.SortOrder).ThenBy(p => p.Id)));

            CreateMap<SiegeObjective, SiegeObjectiveReadDto>()
                .ForMember(d => d.GateStructureName, o => o.MapFrom(s => s.GateStructure != null ? s.GateStructure.Name : null));

            CreateMap<SiegeScenario, SiegeScenarioReadDto>()
                .ForMember(d => d.TownName, o => o.MapFrom(s => s.Town != null ? s.Town.Name : null))
                .ForMember(d => d.Teams, o => o.MapFrom(s => s.Teams.OrderBy(t => t.SortOrder).ThenBy(t => t.Id)))
                .ForMember(d => d.Objectives, o => o.MapFrom(s => s.Objectives.OrderBy(t => t.SortOrder).ThenBy(t => t.Id)));

            CreateMap<SiegeScenario, SiegeScenarioListDto>()
                .ForMember(d => d.TownName, o => o.MapFrom(s => s.Town != null ? s.Town.Name : null))
                .ForMember(d => d.TeamCount, o => o.MapFrom(s => s.Teams.Count))
                .ForMember(d => d.ObjectiveCount, o => o.MapFrom(s => s.Objectives.Count))
                .ForMember(d => d.GateCount, o => o.MapFrom(s => s.Gates.Count));

            CreateMap<SiegeLobbyScenario, SiegeLobbyScenarioDto>()
                .ForMember(d => d.SiegeScenarioName, o => o.MapFrom(s => s.SiegeScenario != null ? s.SiegeScenario.Name : null));

            CreateMap<SiegeLobby, SiegeLobbyReadDto>();

            CreateMap<SiegeLobby, SiegeLobbyListDto>()
                .ForMember(d => d.RotationCount, o => o.MapFrom(s => s.Rotation.Count));
        }
    }
}

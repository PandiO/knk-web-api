using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Mapping
{
    // Siege Survival phase 1 (PandiO/knk-workspace#41). Read-side only - writes go through SiegeSurvivalService
    // explicitly (validation happens there); the frozen configuration is assembled by the service from these
    // read DTOs plus the resolved defaults. Location -> LocationDto comes from LocationMappingProfile.
    public class SiegeSurvivalMappingProfile : Profile
    {
        public SiegeSurvivalMappingProfile()
        {
            CreateMap<SurvivalSettingsKit, SurvivalSelectedKitDto>()
                .ForMember(d => d.KitName, o => o.MapFrom(s => s.Kit != null ? s.Kit.Name : null));

            CreateMap<SiegeSurvivalSettings, SiegeSurvivalSettingsReadDto>()
                .ForMember(d => d.SiegeScenarioName, o => o.MapFrom(s => s.SiegeScenario != null ? s.SiegeScenario.Name : null))
                .ForMember(d => d.SelectedKits, o => o.MapFrom(s => s.SelectedKits.OrderBy(k => k.SortOrder).ThenBy(k => k.KitId)));

            CreateMap<SurvivalLobbySettings, SurvivalLobbySettingsReadDto>()
                .ForMember(d => d.SiegeLobbyName, o => o.MapFrom(s => s.SiegeLobby != null ? s.SiegeLobby.Name : null));

            CreateMap<SurvivalDifficultyProfile, SurvivalDifficultyProfileDto>();
            CreateMap<SurvivalMonsterType, SurvivalMonsterTypeDto>();
            CreateMap<SurvivalSpecialWaveMonsterType, SurvivalSpecialWaveMonsterTypeDto>();

            CreateMap<SurvivalSpecialWaveProfile, SurvivalSpecialWaveProfileDto>()
                .ForMember(d => d.MonsterTypes, o => o.MapFrom(s => s.MonsterTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id)));

            CreateMap<SurvivalSpecialWaveEntry, SurvivalSpecialWaveEntryDto>()
                .ForMember(d => d.ProfileKey, o => o.MapFrom(s => s.SurvivalSpecialWaveProfile != null ? s.SurvivalSpecialWaveProfile.Key : null));

            CreateMap<SurvivalSpawnZonePoint, SurvivalSpawnZonePointDto>();

            CreateMap<SurvivalSpawnZone, SurvivalSpawnZoneDto>()
                .ForMember(d => d.PrerequisiteObjectiveIds, o => o.MapFrom(s => s.PrerequisiteObjectives.Select(p => p.SiegeObjectiveId).OrderBy(id => id)))
                .ForMember(d => d.Points, o => o.MapFrom(s => s.Points.OrderBy(p => p.Kind).ThenBy(p => p.SortOrder).ThenBy(p => p.Id)));

            CreateMap<SurvivalTargetOverride, SurvivalTargetOverrideDto>()
                .ForMember(d => d.SiegeObjectiveName, o => o.MapFrom(s => s.SiegeObjective != null ? s.SiegeObjective.Name : null))
                .ForMember(d => d.GateStructureName, o => o.MapFrom(s => s.GateStructure != null ? s.GateStructure.Name : null));

            CreateMap<SurvivalPurchaseOffer, SurvivalPurchaseOfferDto>()
                .ForMember(d => d.ItemBlueprintName, o => o.MapFrom(s => s.ItemBlueprint != null ? s.ItemBlueprint.Name : null));

            CreateMap<SurvivalPurchasePoint, SurvivalPurchasePointDto>()
                .ForMember(d => d.Offers, o => o.MapFrom(s => s.Offers.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)));

            CreateMap<SurvivalMysteryBoxEntryEnchantment, SurvivalMysteryBoxEntryEnchantmentDto>()
                .ForMember(d => d.EnchantmentKey, o => o.MapFrom(s => s.EnchantmentDefinition != null ? s.EnchantmentDefinition.Key : null))
                .ForMember(d => d.ChanceBasisPoints, o => o.MapFrom(s => BasisPointsOrMinusOne(s.ChancePercent)));

            CreateMap<SurvivalMysteryBoxEntry, SurvivalMysteryBoxEntryDto>()
                .ForMember(d => d.ItemBlueprintName, o => o.MapFrom(s => s.ItemBlueprint != null ? s.ItemBlueprint.Name : null))
                .ForMember(d => d.EnchantmentKey, o => o.MapFrom(s => s.EnchantmentDefinition != null ? s.EnchantmentDefinition.Key : null))
                .ForMember(d => d.ChanceBasisPoints, o => o.MapFrom(s => BasisPointsOrMinusOne(s.ChancePercent)))
                .ForMember(d => d.Enchantments, o => o.MapFrom(s => s.Enchantments.OrderBy(c => c.Id)));

            CreateMap<SurvivalMysteryBoxPool, SurvivalMysteryBoxPoolDto>()
                .ForMember(d => d.TotalChancePercent, o => o.MapFrom(s => s.Entries.Sum(e => e.ChancePercent)))
                .ForMember(d => d.Entries, o => o.MapFrom(s => s.Entries.OrderBy(e => e.SortOrder).ThenBy(e => e.Id)));

            CreateMap<SurvivalMysteryBoxPoint, SurvivalMysteryBoxPointDto>()
                .ForMember(d => d.PoolName, o => o.MapFrom(s => s.SurvivalMysteryBoxPool != null ? s.SurvivalMysteryBoxPool.Name : null));
        }

        private static int BasisPointsOrMinusOne(decimal percent)
        {
            try { return SurvivalScaling.ToBasisPoints(percent); }
            catch (ArgumentException) { return -1; }
        }
    }
}

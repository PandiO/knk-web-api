using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class GateStructureMappingProfile : Profile
    {
        public GateStructureMappingProfile()
        {
            // Lightweight nav mappings
            CreateMap<Street, GateStructureStreetDto>();
            CreateMap<District, GateStructureDistrictDto>();

            // GateStructure -> GateStructureDto (full read with embedded navigations)
            CreateMap<GateStructure, GateStructureDto>()
                // Base Structure/Domain fields
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.AllowEntry, opt => opt.MapFrom(src => src.AllowEntry))
                .ForMember(dest => dest.AllowExit, opt => opt.MapFrom(src => src.AllowExit))
                .ForMember(dest => dest.WgRegionId, opt => opt.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.LocationId, opt => opt.MapFrom(src => src.LocationId))
                .ForMember(dest => dest.Location, opt => opt.MapFrom(src => src.Location))
                .ForMember(dest => dest.StreetId, opt => opt.MapFrom(src => src.StreetId))
                .ForMember(dest => dest.DistrictId, opt => opt.MapFrom(src => src.DistrictId))
                .ForMember(dest => dest.HouseNumber, opt => opt.MapFrom(src => src.HouseNumber))
                .ForMember(dest => dest.IconMaterialRefId, opt => opt.MapFrom(src => src.IconMaterialRefId))

                // Guard & Defense System
                .ForMember(dest => dest.GuardSpawnLocationIds, opt => opt.MapFrom(src => src.GuardSpawnLocations.Select(l => l.Id).ToList()))
                .ForMember(dest => dest.GuardSpawnLocations, opt => opt.MapFrom(src => src.GuardSpawnLocations))
                .ForMember(dest => dest.GuardCount, opt => opt.MapFrom(src => src.GuardCount))
                .ForMember(dest => dest.GuardNpcTemplateId, opt => opt.MapFrom(src => src.GuardNpcTemplateId))

                // Siege Integration
                .ForMember(dest => dest.IsOverridable, opt => opt.MapFrom(src => src.IsOverridable))
                .ForMember(dest => dest.AnimateDuringSiege, opt => opt.MapFrom(src => src.AnimateDuringSiege))
                .ForMember(dest => dest.CurrentSiegeId, opt => opt.MapFrom(src => src.CurrentSiegeId))
                .ForMember(dest => dest.IsSiegeObjective, opt => opt.MapFrom(src => src.IsSiegeObjective))

                // Structure-level cascading overrides (decision 5.0-B)
                .ForMember(dest => dest.IsActiveOverride, opt => opt.MapFrom(src => src.IsActiveOverride))
                .ForMember(dest => dest.CanRespawnOverride, opt => opt.MapFrom(src => src.CanRespawnOverride))
                .ForMember(dest => dest.IsDestroyedOverride, opt => opt.MapFrom(src => src.IsDestroyedOverride))
                .ForMember(dest => dest.IsInvincibleOverride, opt => opt.MapFrom(src => src.IsInvincibleOverride))
                .ForMember(dest => dest.OpenedStateOverride, opt => opt.MapFrom(src => src.OpenedStateOverride))
                .ForMember(dest => dest.AllowPassThroughOverride, opt => opt.MapFrom(src => src.AllowPassThroughOverride))
                .ForMember(dest => dest.PassThroughDurationSecondsOverride, opt => opt.MapFrom(src => src.PassThroughDurationSecondsOverride))
                .ForMember(dest => dest.ShowHealthDisplayOverride, opt => opt.MapFrom(src => src.ShowHealthDisplayOverride))
                .ForMember(dest => dest.HealthDisplayModeOverride, opt => opt.MapFrom(src => src.HealthDisplayModeOverride))
                .ForMember(dest => dest.HealthDisplayYOffsetOverride, opt => opt.MapFrom(src => src.HealthDisplayYOffsetOverride))
                .ForMember(dest => dest.GateNameDisplayModeOverride, opt => opt.MapFrom(src => src.GateNameDisplayModeOverride))
                .ForMember(dest => dest.StatusDisplayModeOverride, opt => opt.MapFrom(src => src.StatusDisplayModeOverride))
                .ForMember(dest => dest.AllowContinuousDamageOverride, opt => opt.MapFrom(src => src.AllowContinuousDamageOverride))
                .ForMember(dest => dest.ContinuousDamageMultiplierOverride, opt => opt.MapFrom(src => src.ContinuousDamageMultiplierOverride))

                // Navigation Properties
                .ForMember(dest => dest.GateDoors, opt => opt.MapFrom(src => src.GateDoors))
                .ForMember(dest => dest.Street, opt => opt.MapFrom(s => s.Street == null ? null : new GateStructureStreetDto
                {
                    Id = s.Street.Id,
                    Name = s.Street.Name
                }))
                .ForMember(dest => dest.District, opt => opt.MapFrom(s => s.District == null ? null : new GateStructureDistrictDto
                {
                    Id = s.District.Id,
                    Name = s.District.Name,
                    Description = s.District.Description,
                    AllowEntry = s.District.AllowEntry,
                    AllowExit = s.District.AllowExit,
                    WgRegionId = s.District.WgRegionId
                }))
                .ForMember(dest => dest.IconMaterialRef, opt => opt.MapFrom(s => s.IconMaterial == null ? null : new MinecraftMaterialRefDto
                {
                    Id = s.IconMaterial.Id,
                    NamespaceKey = s.IconMaterial.NamespaceKey,
                    LegacyName = s.IconMaterial.LegacyName,
                    Category = s.IconMaterial.Category,
                    IconUrl = s.IconMaterial.IconUrl
                }));

            // GateStructureDto -> GateStructure (create/update)
            CreateMap<GateStructureDto, GateStructure>()
                // Base fields
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.AllowEntry, opt => opt.MapFrom(src => src.AllowEntry))
                .ForMember(dest => dest.AllowExit, opt => opt.MapFrom(src => src.AllowExit))
                .ForMember(dest => dest.WgRegionId, opt => opt.MapFrom(src => src.WgRegionId ?? string.Empty))
                .ForMember(dest => dest.LocationId, opt => opt.MapFrom(src => src.LocationId))
                .ForMember(dest => dest.StreetId, opt => opt.MapFrom(src => src.StreetId))
                .ForMember(dest => dest.DistrictId, opt => opt.MapFrom(src => src.DistrictId))
                .ForMember(dest => dest.HouseNumber, opt => opt.MapFrom(src => src.HouseNumber))
                .ForMember(dest => dest.IconMaterialRefId, opt => opt.MapFrom(src => src.IconMaterialRefId))

                // Guard System
                .ForMember(dest => dest.GuardCount, opt => opt.MapFrom(src => src.GuardCount ?? 0))
                .ForMember(dest => dest.GuardNpcTemplateId, opt => opt.MapFrom(src => src.GuardNpcTemplateId))

                // Siege Integration
                .ForMember(dest => dest.IsOverridable, opt => opt.MapFrom(src => src.IsOverridable ?? true))
                .ForMember(dest => dest.AnimateDuringSiege, opt => opt.MapFrom(src => src.AnimateDuringSiege ?? true))
                .ForMember(dest => dest.CurrentSiegeId, opt => opt.MapFrom(src => src.CurrentSiegeId))
                .ForMember(dest => dest.IsSiegeObjective, opt => opt.MapFrom(src => src.IsSiegeObjective ?? false))

                // Structure-level cascading overrides (decision 5.0-B) - GateStructureDto carries
                // these for reads, but writes to them only ever happen via the dedicated
                // PATCH /api/GateStructures/{id}/overrides endpoint (GateStructureOverridesUpdateDto),
                // never through a general create/update, so an incoming override value here is
                // ignored rather than silently overwriting an existing override.
                .ForMember(dest => dest.IsActiveOverride, opt => opt.Ignore())
                .ForMember(dest => dest.CanRespawnOverride, opt => opt.Ignore())
                .ForMember(dest => dest.IsDestroyedOverride, opt => opt.Ignore())
                .ForMember(dest => dest.IsInvincibleOverride, opt => opt.Ignore())
                .ForMember(dest => dest.OpenedStateOverride, opt => opt.Ignore())
                .ForMember(dest => dest.AllowPassThroughOverride, opt => opt.Ignore())
                .ForMember(dest => dest.PassThroughDurationSecondsOverride, opt => opt.Ignore())
                .ForMember(dest => dest.ShowHealthDisplayOverride, opt => opt.Ignore())
                .ForMember(dest => dest.HealthDisplayModeOverride, opt => opt.Ignore())
                .ForMember(dest => dest.HealthDisplayYOffsetOverride, opt => opt.Ignore())
                .ForMember(dest => dest.GateNameDisplayModeOverride, opt => opt.Ignore())
                .ForMember(dest => dest.StatusDisplayModeOverride, opt => opt.Ignore())
                .ForMember(dest => dest.AllowContinuousDamageOverride, opt => opt.Ignore())
                .ForMember(dest => dest.ContinuousDamageMultiplierOverride, opt => opt.Ignore())

                // Ignore navigation properties
                .ForMember(dest => dest.GateDoors, opt => opt.Ignore())
                .ForMember(dest => dest.Street, opt => opt.Ignore())
                .ForMember(dest => dest.District, opt => opt.Ignore())
                .ForMember(dest => dest.Location, opt => opt.Ignore())
                .ForMember(dest => dest.GuardSpawnLocations, opt => opt.Ignore())
                .ForMember(dest => dest.IconMaterial, opt => opt.Ignore());

            // GateStructure -> GateStructureListDto (for search results)
            CreateMap<GateStructure, GateStructureListDto>()
                .ForMember(dest => dest.id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.wgRegionId, opt => opt.MapFrom(src => src.WgRegionId))
                .ForMember(dest => dest.houseNumber, opt => opt.MapFrom(src => src.HouseNumber))
                .ForMember(dest => dest.streetId, opt => opt.MapFrom(src => src.StreetId))
                .ForMember(dest => dest.streetName, opt => opt.MapFrom(src => src.Street != null ? src.Street.Name : null))
                .ForMember(dest => dest.districtId, opt => opt.MapFrom(src => src.DistrictId))
                .ForMember(dest => dest.districtName, opt => opt.MapFrom(src => src.District != null ? src.District.Name : null))
                .ForMember(dest => dest.doorCount, opt => opt.MapFrom(src => src.GateDoors.Count));

            // PagedQuery DTO conversions
            CreateMap<PagedQueryDto, PagedQuery>();
        }
    }
}

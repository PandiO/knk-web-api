using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class GateStructureMappingProfile : Profile
    {
        public GateStructureMappingProfile()
        {
            // GateStructure -> GateStructureReadDto (full read for v2 API)
            CreateMap<GateStructure, GateStructureReadDto>()
                .ForMember(dest => dest.Street, opt => opt.MapFrom(src => src.Street))
                .ForMember(dest => dest.District, opt => opt.MapFrom(src => src.District))
                .ForMember(dest => dest.IconMaterialRef, opt => opt.MapFrom(src => src.IconMaterial))
                .ForMember(dest => dest.FallbackMaterialRef, opt => opt.MapFrom(src => src.FallbackMaterial));

            // GateStructureCreateDto -> GateStructure
            CreateMap<GateStructureCreateDto, GateStructure>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.BlockSnapshots, opt => opt.Ignore())
                .ForMember(dest => dest.Street, opt => opt.Ignore())
                .ForMember(dest => dest.District, opt => opt.Ignore())
                .ForMember(dest => dest.Location, opt => opt.Ignore())
                .ForMember(dest => dest.IconMaterial, opt => opt.Ignore())
                .ForMember(dest => dest.FallbackMaterial, opt => opt.Ignore());

            // GateStructureUpdateDto -> GateStructure
            CreateMap<GateStructureUpdateDto, GateStructure>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.BlockSnapshots, opt => opt.Ignore())
                .ForMember(dest => dest.Street, opt => opt.Ignore())
                .ForMember(dest => dest.District, opt => opt.Ignore())
                .ForMember(dest => dest.Location, opt => opt.Ignore())
                .ForMember(dest => dest.IconMaterial, opt => opt.Ignore())
                .ForMember(dest => dest.FallbackMaterial, opt => opt.Ignore());

            // GateStructure -> GateStructureNavDto
            CreateMap<GateStructure, GateStructureNavDto>();

            // GateBlockSnapshotCreateDto -> GateBlockSnapshot
            CreateMap<GateBlockSnapshotCreateDto, GateBlockSnapshot>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.GateStructure, opt => opt.Ignore());

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
                
                // Core Gate State & Health System
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.CanRespawn, opt => opt.MapFrom(src => src.CanRespawn))
                .ForMember(dest => dest.IsDestroyed, opt => opt.MapFrom(src => src.IsDestroyed))
                .ForMember(dest => dest.IsInvincible, opt => opt.MapFrom(src => src.IsInvincible))
                .ForMember(dest => dest.IsOpened, opt => opt.MapFrom(src => src.IsOpened))
                .ForMember(dest => dest.IsJammed, opt => opt.MapFrom(src => src.IsJammed))
                .ForMember(dest => dest.HealthCurrent, opt => opt.MapFrom(src => src.HealthCurrent))
                .ForMember(dest => dest.HealthMax, opt => opt.MapFrom(src => src.HealthMax))
                .ForMember(dest => dest.FaceDirection, opt => opt.MapFrom(src => src.FaceDirection))
                .ForMember(dest => dest.RespawnRateSeconds, opt => opt.MapFrom(src => src.RespawnRateSeconds))
                .ForMember(dest => dest.IconMaterialRefId, opt => opt.MapFrom(src => src.IconMaterialRefId))
                .ForMember(dest => dest.RegionClosedId, opt => opt.MapFrom(src => src.RegionClosedId))
                .ForMember(dest => dest.RegionOpenedId, opt => opt.MapFrom(src => src.RegionOpenedId))
                
                // Gate Type & Animation Configuration
                .ForMember(dest => dest.GateType, opt => opt.MapFrom(src => src.GateType))
                .ForMember(dest => dest.GeometryDefinitionMode, opt => opt.MapFrom(src => src.GeometryDefinitionMode))
                .ForMember(dest => dest.MotionType, opt => opt.MapFrom(src => src.MotionType))
                .ForMember(dest => dest.AnimationDurationTicks, opt => opt.MapFrom(src => src.AnimationDurationTicks))
                .ForMember(dest => dest.AnimationTickRate, opt => opt.MapFrom(src => src.AnimationTickRate))
                
                // Geometry Definition (PLANE_GRID mode)
                .ForMember(dest => dest.AnchorPointId, opt => opt.MapFrom(src => src.AnchorPointId))
                .ForMember(dest => dest.AnchorPoint, opt => opt.MapFrom(src => src.AnchorPoint))
                .ForMember(dest => dest.OpenAnchorPointId, opt => opt.MapFrom(src => src.OpenAnchorPointId))
                .ForMember(dest => dest.OpenAnchorPoint, opt => opt.MapFrom(src => src.OpenAnchorPoint))
                .ForMember(dest => dest.ReferencePoint1Id, opt => opt.MapFrom(src => src.ReferencePoint1Id))
                .ForMember(dest => dest.ReferencePoint1, opt => opt.MapFrom(src => src.ReferencePoint1))
                .ForMember(dest => dest.ReferencePoint2Id, opt => opt.MapFrom(src => src.ReferencePoint2Id))
                .ForMember(dest => dest.ReferencePoint2, opt => opt.MapFrom(src => src.ReferencePoint2))
                .ForMember(dest => dest.GeometryWidth, opt => opt.MapFrom(src => src.GeometryWidth))
                .ForMember(dest => dest.GeometryHeight, opt => opt.MapFrom(src => src.GeometryHeight))
                .ForMember(dest => dest.GeometryDepth, opt => opt.MapFrom(src => src.GeometryDepth))
                .ForMember(dest => dest.MotionDistanceBlocks, opt => opt.MapFrom(src => src.MotionDistanceBlocks))
                .ForMember(dest => dest.ClipToGeometryBounds, opt => opt.MapFrom(src => src.ClipToGeometryBounds))
                
                // Geometry Definition (FLOOD_FILL mode)
                .ForMember(dest => dest.SeedBlocks, opt => opt.MapFrom(src => src.SeedBlocks))
                .ForMember(dest => dest.ScanMaxBlocks, opt => opt.MapFrom(src => src.ScanMaxBlocks))
                .ForMember(dest => dest.ScanMaxRadius, opt => opt.MapFrom(src => src.ScanMaxRadius))
                .ForMember(dest => dest.ScanMaterialWhitelist, opt => opt.MapFrom(src => src.ScanMaterialWhitelist))
                .ForMember(dest => dest.ScanMaterialBlacklist, opt => opt.MapFrom(src => src.ScanMaterialBlacklist))
                .ForMember(dest => dest.ScanPlaneConstraint, opt => opt.MapFrom(src => src.ScanPlaneConstraint))
                
                // Block Management
                .ForMember(dest => dest.FallbackMaterialRefId, opt => opt.MapFrom(src => src.FallbackMaterialRefId))
                .ForMember(dest => dest.TileEntityPolicy, opt => opt.MapFrom(src => src.TileEntityPolicy))
                
                // Rotation-Specific Fields
                .ForMember(dest => dest.RotationMaxAngleDegrees, opt => opt.MapFrom(src => src.RotationMaxAngleDegrees))
                .ForMember(dest => dest.HingeAxisId, opt => opt.MapFrom(src => src.HingeAxisId))
                .ForMember(dest => dest.HingeAxis, opt => opt.MapFrom(src => src.HingeAxis))
                
                // Double Doors Specific
                .ForMember(dest => dest.LeftDoorSeedBlockId, opt => opt.MapFrom(src => src.LeftDoorSeedBlockId))
                .ForMember(dest => dest.LeftDoorSeedBlock, opt => opt.MapFrom(src => src.LeftDoorSeedBlock))
                .ForMember(dest => dest.RightDoorSeedBlockId, opt => opt.MapFrom(src => src.RightDoorSeedBlockId))
                .ForMember(dest => dest.RightDoorSeedBlock, opt => opt.MapFrom(src => src.RightDoorSeedBlock))
                .ForMember(dest => dest.MirrorRotation, opt => opt.MapFrom(src => src.MirrorRotation))
                
                // Pass-Through System
                .ForMember(dest => dest.AllowPassThrough, opt => opt.MapFrom(src => src.AllowPassThrough))
                .ForMember(dest => dest.PassThroughDurationSeconds, opt => opt.MapFrom(src => src.PassThroughDurationSeconds))
                .ForMember(dest => dest.PassThroughConditionsJson, opt => opt.MapFrom(src => src.PassThroughConditionsJson))
                
                // Guard & Defense System
                .ForMember(dest => dest.GuardSpawnLocationIds, opt => opt.MapFrom(src => src.GuardSpawnLocations.Select(l => l.Id).ToList()))
                .ForMember(dest => dest.GuardSpawnLocations, opt => opt.MapFrom(src => src.GuardSpawnLocations))
                .ForMember(dest => dest.GuardCount, opt => opt.MapFrom(src => src.GuardCount))
                .ForMember(dest => dest.GuardNpcTemplateId, opt => opt.MapFrom(src => src.GuardNpcTemplateId))
                
                // Health Display Configuration
                .ForMember(dest => dest.ShowHealthDisplay, opt => opt.MapFrom(src => src.ShowHealthDisplay))
                .ForMember(dest => dest.HealthDisplayMode, opt => opt.MapFrom(src => src.HealthDisplayMode))
                .ForMember(dest => dest.HealthDisplayYOffset, opt => opt.MapFrom(src => src.HealthDisplayYOffset))
                .ForMember(dest => dest.InfoDisplayLocationId, opt => opt.MapFrom(src => src.InfoDisplayLocationId))
                .ForMember(dest => dest.InfoDisplayLocation, opt => opt.MapFrom(src => src.InfoDisplayLocation))
                .ForMember(dest => dest.GateNameDisplayMode, opt => opt.MapFrom(src => src.GateNameDisplayMode))
                .ForMember(dest => dest.StatusDisplayMode, opt => opt.MapFrom(src => src.StatusDisplayMode))

                // Siege Integration
                .ForMember(dest => dest.IsOverridable, opt => opt.MapFrom(src => src.IsOverridable))
                .ForMember(dest => dest.AnimateDuringSiege, opt => opt.MapFrom(src => src.AnimateDuringSiege))
                .ForMember(dest => dest.CurrentSiegeId, opt => opt.MapFrom(src => src.CurrentSiegeId))
                .ForMember(dest => dest.IsSiegeObjective, opt => opt.MapFrom(src => src.IsSiegeObjective))

                // Combat System: Continuous Damage
                .ForMember(dest => dest.AllowContinuousDamage, opt => opt.MapFrom(src => src.AllowContinuousDamage))
                .ForMember(dest => dest.ContinuousDamageMultiplier, opt => opt.MapFrom(src => src.ContinuousDamageMultiplier))
                .ForMember(dest => dest.ContinuousDamageDurationSeconds, opt => opt.MapFrom(src => src.ContinuousDamageDurationSeconds))

                // Navigation Properties
                .ForMember(dest => dest.BlockSnapshots, opt => opt.MapFrom(src => src.BlockSnapshots))
                .ForMember(dest => dest.OpenedBlockSnapshots, opt => opt.MapFrom(src => src.OpenedBlockSnapshots))
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
                }))
                .ForMember(dest => dest.FallbackMaterialRef, opt => opt.MapFrom(s => s.FallbackMaterial == null ? null : new MinecraftMaterialRefDto
                {
                    Id = s.FallbackMaterial.Id,
                    NamespaceKey = s.FallbackMaterial.NamespaceKey,
                    LegacyName = s.FallbackMaterial.LegacyName,
                    Category = s.FallbackMaterial.Category,
                    IconUrl = s.FallbackMaterial.IconUrl
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
                
                // Core Gate State & Health
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.IsActive ?? false))
                .ForMember(dest => dest.CanRespawn, opt => opt.MapFrom(src => src.CanRespawn ?? true))
                .ForMember(dest => dest.IsDestroyed, opt => opt.MapFrom(src => src.IsDestroyed ?? false))
                .ForMember(dest => dest.IsInvincible, opt => opt.MapFrom(src => src.IsInvincible ?? true))
                .ForMember(dest => dest.IsOpened, opt => opt.MapFrom(src => src.IsOpened ?? false))
                .ForMember(dest => dest.IsJammed, opt => opt.MapFrom(src => src.IsJammed ?? false))
                .ForMember(dest => dest.HealthMax, opt => opt.MapFrom(src => src.HealthMax ?? 500.0))
                .ForMember(dest => dest.HealthCurrent, opt => opt.MapFrom(src => src.HealthCurrent ?? src.HealthMax ?? 500.0))
                .ForMember(dest => dest.FaceDirection, opt => opt.MapFrom(src => src.FaceDirection ?? "north"))
                .ForMember(dest => dest.RespawnRateSeconds, opt => opt.MapFrom(src => src.RespawnRateSeconds ?? 300))
                .ForMember(dest => dest.IconMaterialRefId, opt => opt.MapFrom(src => src.IconMaterialRefId))
                .ForMember(dest => dest.RegionClosedId, opt => opt.MapFrom(src => src.RegionClosedId ?? string.Empty))
                .ForMember(dest => dest.RegionOpenedId, opt => opt.MapFrom(src => src.RegionOpenedId ?? string.Empty))
                
                // Gate Type & Animation
                .ForMember(dest => dest.GateType, opt => opt.MapFrom(src => src.GateType))
                .ForMember(dest => dest.GeometryDefinitionMode, opt => opt.MapFrom(src => src.GeometryDefinitionMode))
                .ForMember(dest => dest.MotionType, opt => opt.MapFrom(src => src.MotionType))
                .ForMember(dest => dest.AnimationDurationTicks, opt => opt.MapFrom(src => src.AnimationDurationTicks ?? 60))
                .ForMember(dest => dest.AnimationTickRate, opt => opt.MapFrom(src => src.AnimationTickRate ?? 1))
                
                // Geometry (PLANE_GRID)
                .ForMember(dest => dest.AnchorPointId, opt => opt.MapFrom(src => src.AnchorPointId))
                .ForMember(dest => dest.OpenAnchorPointId, opt => opt.MapFrom(src => src.OpenAnchorPointId))
                .ForMember(dest => dest.ReferencePoint1Id, opt => opt.MapFrom(src => src.ReferencePoint1Id))
                .ForMember(dest => dest.ReferencePoint2Id, opt => opt.MapFrom(src => src.ReferencePoint2Id))
                .ForMember(dest => dest.GeometryWidth, opt => opt.MapFrom(src => src.GeometryWidth ?? 0))
                .ForMember(dest => dest.GeometryHeight, opt => opt.MapFrom(src => src.GeometryHeight ?? 0))
                .ForMember(dest => dest.GeometryDepth, opt => opt.MapFrom(src => src.GeometryDepth ?? 0))
                .ForMember(dest => dest.MotionDistanceBlocks, opt => opt.MapFrom(src => src.MotionDistanceBlocks ?? 0))
                .ForMember(dest => dest.ClipToGeometryBounds, opt => opt.MapFrom(src => src.ClipToGeometryBounds ?? false))
                
                // Geometry (FLOOD_FILL)
                .ForMember(dest => dest.SeedBlocks, opt => opt.MapFrom(src => src.SeedBlocks ?? string.Empty))
                .ForMember(dest => dest.ScanMaxBlocks, opt => opt.MapFrom(src => src.ScanMaxBlocks ?? 500))
                .ForMember(dest => dest.ScanMaxRadius, opt => opt.MapFrom(src => src.ScanMaxRadius ?? 20))
                .ForMember(dest => dest.ScanMaterialWhitelist, opt => opt.MapFrom(src => src.ScanMaterialWhitelist ?? string.Empty))
                .ForMember(dest => dest.ScanMaterialBlacklist, opt => opt.MapFrom(src => src.ScanMaterialBlacklist ?? string.Empty))
                .ForMember(dest => dest.ScanPlaneConstraint, opt => opt.MapFrom(src => src.ScanPlaneConstraint ?? false))
                
                // Block Management
                .ForMember(dest => dest.FallbackMaterialRefId, opt => opt.MapFrom(src => src.FallbackMaterialRefId))
                .ForMember(dest => dest.TileEntityPolicy, opt => opt.MapFrom(src => src.TileEntityPolicy))
                
                // Rotation-Specific
                .ForMember(dest => dest.RotationMaxAngleDegrees, opt => opt.MapFrom(src => src.RotationMaxAngleDegrees ?? 90))
                .ForMember(dest => dest.HingeAxisId, opt => opt.MapFrom(src => src.HingeAxisId))
                
                // Double Doors
                .ForMember(dest => dest.LeftDoorSeedBlockId, opt => opt.MapFrom(src => src.LeftDoorSeedBlockId))
                .ForMember(dest => dest.RightDoorSeedBlockId, opt => opt.MapFrom(src => src.RightDoorSeedBlockId))
                .ForMember(dest => dest.MirrorRotation, opt => opt.MapFrom(src => src.MirrorRotation ?? true))
                
                // Pass-Through
                .ForMember(dest => dest.AllowPassThrough, opt => opt.MapFrom(src => src.AllowPassThrough ?? false))
                .ForMember(dest => dest.PassThroughDurationSeconds, opt => opt.MapFrom(src => src.PassThroughDurationSeconds ?? 4))
                .ForMember(dest => dest.PassThroughConditionsJson, opt => opt.MapFrom(src => src.PassThroughConditionsJson ?? string.Empty))
                
                // Guard System
                .ForMember(dest => dest.GuardCount, opt => opt.MapFrom(src => src.GuardCount ?? 0))
                .ForMember(dest => dest.GuardNpcTemplateId, opt => opt.MapFrom(src => src.GuardNpcTemplateId))
                
                // Health Display
                .ForMember(dest => dest.ShowHealthDisplay, opt => opt.MapFrom(src => src.ShowHealthDisplay ?? true))
                .ForMember(dest => dest.HealthDisplayMode, opt => opt.MapFrom(src => src.HealthDisplayMode))
                .ForMember(dest => dest.HealthDisplayYOffset, opt => opt.MapFrom(src => src.HealthDisplayYOffset ?? 2))
                .ForMember(dest => dest.InfoDisplayLocationId, opt => opt.MapFrom(src => src.InfoDisplayLocationId))
                .ForMember(dest => dest.GateNameDisplayMode, opt => opt.MapFrom(src => src.GateNameDisplayMode))
                .ForMember(dest => dest.StatusDisplayMode, opt => opt.MapFrom(src => src.StatusDisplayMode))

                // Siege Integration
                .ForMember(dest => dest.IsOverridable, opt => opt.MapFrom(src => src.IsOverridable ?? true))
                .ForMember(dest => dest.AnimateDuringSiege, opt => opt.MapFrom(src => src.AnimateDuringSiege ?? true))
                .ForMember(dest => dest.CurrentSiegeId, opt => opt.MapFrom(src => src.CurrentSiegeId))
                .ForMember(dest => dest.IsSiegeObjective, opt => opt.MapFrom(src => src.IsSiegeObjective ?? false))
                
                // Combat System
                .ForMember(dest => dest.AllowContinuousDamage, opt => opt.MapFrom(src => src.AllowContinuousDamage ?? true))
                .ForMember(dest => dest.ContinuousDamageMultiplier, opt => opt.MapFrom(src => src.ContinuousDamageMultiplier ?? 1.0))
                .ForMember(dest => dest.ContinuousDamageDurationSeconds, opt => opt.MapFrom(src => src.ContinuousDamageDurationSeconds ?? 5))
                
                // Ignore navigation properties
                .ForMember(dest => dest.BlockSnapshots, opt => opt.Ignore())
                .ForMember(dest => dest.OpenedBlockSnapshots, opt => opt.Ignore())
                .ForMember(dest => dest.Street, opt => opt.Ignore())
                .ForMember(dest => dest.District, opt => opt.Ignore())
                .ForMember(dest => dest.Location, opt => opt.Ignore())
                .ForMember(dest => dest.AnchorPoint, opt => opt.Ignore())
                .ForMember(dest => dest.OpenAnchorPoint, opt => opt.Ignore())
                .ForMember(dest => dest.ReferencePoint1, opt => opt.Ignore())
                .ForMember(dest => dest.ReferencePoint2, opt => opt.Ignore())
                .ForMember(dest => dest.HingeAxis, opt => opt.Ignore())
                .ForMember(dest => dest.LeftDoorSeedBlock, opt => opt.Ignore())
                .ForMember(dest => dest.RightDoorSeedBlock, opt => opt.Ignore())
                .ForMember(dest => dest.InfoDisplayLocation, opt => opt.Ignore())
                .ForMember(dest => dest.GuardSpawnLocations, opt => opt.Ignore())
                .ForMember(dest => dest.IconMaterial, opt => opt.Ignore())
                .ForMember(dest => dest.FallbackMaterial, opt => opt.Ignore());

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
                .ForMember(dest => dest.isActive, opt => opt.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.healthCurrent, opt => opt.MapFrom(src => src.HealthCurrent))
                .ForMember(dest => dest.healthMax, opt => opt.MapFrom(src => src.HealthMax))
                .ForMember(dest => dest.isDestroyed, opt => opt.MapFrom(src => src.IsDestroyed))
                .ForMember(dest => dest.isOpened, opt => opt.MapFrom(src => src.IsOpened))
                .ForMember(dest => dest.isJammed, opt => opt.MapFrom(src => src.IsJammed))
                .ForMember(dest => dest.gateType, opt => opt.MapFrom(src => src.GateType))
                .ForMember(dest => dest.faceDirection, opt => opt.MapFrom(src => src.FaceDirection));

            // GateBlockSnapshot <-> GateBlockSnapshotDto
            CreateMap<GateBlockSnapshot, GateBlockSnapshotDto>();
            CreateMap<GateBlockSnapshotDto, GateBlockSnapshot>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.GateStructure, opt => opt.Ignore());

            // GateOpenedBlockSnapshot <-> GateOpenedBlockSnapshotDto - mirrors the
            // GateBlockSnapshot mappings above exactly. See ROTATION_GAP_FILL_DESIGN.md.
            CreateMap<GateOpenedBlockSnapshot, GateOpenedBlockSnapshotDto>();
            CreateMap<GateOpenedBlockSnapshotDto, GateOpenedBlockSnapshot>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.GateStructure, opt => opt.Ignore());
            CreateMap<GateOpenedBlockSnapshotCreateDto, GateOpenedBlockSnapshot>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.GateStructure, opt => opt.Ignore());

            // PagedQuery DTO conversions
            CreateMap<PagedQueryDto, PagedQuery>();
        }
    }
}

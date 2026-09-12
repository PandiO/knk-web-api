using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class GateDoorMappingProfile : Profile
    {
        public GateDoorMappingProfile()
        {
            // GateDoor -> GateDoorDto (full read with embedded navigations)
            CreateMap<GateDoor, GateDoorDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.GateStructureId, opt => opt.MapFrom(src => src.GateStructureId))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.HealthCurrent, opt => opt.MapFrom(src => src.HealthCurrent))
                .ForMember(dest => dest.HealthMax, opt => opt.MapFrom(src => src.HealthMax))
                .ForMember(dest => dest.RespawnRateSeconds, opt => opt.MapFrom(src => src.RespawnRateSeconds))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.CanRespawn, opt => opt.MapFrom(src => src.CanRespawn))
                .ForMember(dest => dest.IsDestroyed, opt => opt.MapFrom(src => src.IsDestroyed))
                .ForMember(dest => dest.IsInvincible, opt => opt.MapFrom(src => src.IsInvincible))
                .ForMember(dest => dest.OpenedState, opt => opt.MapFrom(src => src.OpenedState))
                .ForMember(dest => dest.GateType, opt => opt.MapFrom(src => src.GateType))
                .ForMember(dest => dest.GeometryDefinitionMode, opt => opt.MapFrom(src => src.GeometryDefinitionMode))
                .ForMember(dest => dest.MotionType, opt => opt.MapFrom(src => src.MotionType))
                .ForMember(dest => dest.AnimationDurationTicks, opt => opt.MapFrom(src => src.AnimationDurationTicks))
                .ForMember(dest => dest.AnimationTickRate, opt => opt.MapFrom(src => src.AnimationTickRate))
                .ForMember(dest => dest.FaceDirection, opt => opt.MapFrom(src => src.FaceDirection))
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
                .ForMember(dest => dest.SeedBlocks, opt => opt.MapFrom(src => src.SeedBlocks))
                .ForMember(dest => dest.ScanMaxBlocks, opt => opt.MapFrom(src => src.ScanMaxBlocks))
                .ForMember(dest => dest.ScanMaxRadius, opt => opt.MapFrom(src => src.ScanMaxRadius))
                .ForMember(dest => dest.ScanMaterialWhitelist, opt => opt.MapFrom(src => src.ScanMaterialWhitelist))
                .ForMember(dest => dest.ScanMaterialBlacklist, opt => opt.MapFrom(src => src.ScanMaterialBlacklist))
                .ForMember(dest => dest.ScanPlaneConstraint, opt => opt.MapFrom(src => src.ScanPlaneConstraint))
                .ForMember(dest => dest.FallbackMaterialRefId, opt => opt.MapFrom(src => src.FallbackMaterialRefId))
                .ForMember(dest => dest.FallbackMaterialRef, opt => opt.MapFrom(s => s.FallbackMaterial == null ? null : new MinecraftMaterialRefDto
                {
                    Id = s.FallbackMaterial.Id,
                    NamespaceKey = s.FallbackMaterial.NamespaceKey,
                    LegacyName = s.FallbackMaterial.LegacyName,
                    Category = s.FallbackMaterial.Category,
                    IconUrl = s.FallbackMaterial.IconUrl
                }))
                .ForMember(dest => dest.TileEntityPolicy, opt => opt.MapFrom(src => src.TileEntityPolicy))
                .ForMember(dest => dest.RotationMaxAngleDegrees, opt => opt.MapFrom(src => src.RotationMaxAngleDegrees))
                .ForMember(dest => dest.HingeAxisId, opt => opt.MapFrom(src => src.HingeAxisId))
                .ForMember(dest => dest.HingeAxis, opt => opt.MapFrom(src => src.HingeAxis))
                .ForMember(dest => dest.MirrorRotation, opt => opt.MapFrom(src => src.MirrorRotation))
                .ForMember(dest => dest.LeftDoorSeedBlockId, opt => opt.MapFrom(src => src.LeftDoorSeedBlockId))
                .ForMember(dest => dest.LeftDoorSeedBlock, opt => opt.MapFrom(src => src.LeftDoorSeedBlock))
                .ForMember(dest => dest.RightDoorSeedBlockId, opt => opt.MapFrom(src => src.RightDoorSeedBlockId))
                .ForMember(dest => dest.RightDoorSeedBlock, opt => opt.MapFrom(src => src.RightDoorSeedBlock))
                .ForMember(dest => dest.RegionClosedId, opt => opt.MapFrom(src => src.RegionClosedId))
                .ForMember(dest => dest.RegionOpenedId, opt => opt.MapFrom(src => src.RegionOpenedId))
                .ForMember(dest => dest.AllowPassThrough, opt => opt.MapFrom(src => src.AllowPassThrough))
                .ForMember(dest => dest.PassThroughDurationSeconds, opt => opt.MapFrom(src => src.PassThroughDurationSeconds))
                .ForMember(dest => dest.PassThroughConditionsJson, opt => opt.MapFrom(src => src.PassThroughConditionsJson))
                .ForMember(dest => dest.InfoDisplayLocationId, opt => opt.MapFrom(src => src.InfoDisplayLocationId))
                .ForMember(dest => dest.InfoDisplayLocation, opt => opt.MapFrom(src => src.InfoDisplayLocation))
                .ForMember(dest => dest.ShowHealthDisplay, opt => opt.MapFrom(src => src.ShowHealthDisplay))
                .ForMember(dest => dest.HealthDisplayMode, opt => opt.MapFrom(src => src.HealthDisplayMode))
                .ForMember(dest => dest.HealthDisplayYOffset, opt => opt.MapFrom(src => src.HealthDisplayYOffset))
                .ForMember(dest => dest.GateNameDisplayMode, opt => opt.MapFrom(src => src.GateNameDisplayMode))
                .ForMember(dest => dest.StatusDisplayMode, opt => opt.MapFrom(src => src.StatusDisplayMode))
                .ForMember(dest => dest.DoorNameDisplayMode, opt => opt.MapFrom(src => src.DoorNameDisplayMode))
                .ForMember(dest => dest.AllowContinuousDamage, opt => opt.MapFrom(src => src.AllowContinuousDamage))
                .ForMember(dest => dest.ContinuousDamageMultiplier, opt => opt.MapFrom(src => src.ContinuousDamageMultiplier))
                .ForMember(dest => dest.ContinuousDamageDurationSeconds, opt => opt.MapFrom(src => src.ContinuousDamageDurationSeconds))
                .ForMember(dest => dest.BlockSnapshots, opt => opt.MapFrom(src => src.BlockSnapshots))
                .ForMember(dest => dest.OpenedBlockSnapshots, opt => opt.MapFrom(src => src.OpenedBlockSnapshots));

            // GateDoor -> GateDoorNavDto
            CreateMap<GateDoor, GateDoorNavDto>();

            // GateDoorDto -> GateDoor (create/update)
            CreateMap<GateDoorDto, GateDoor>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.GateStructureId, opt => opt.MapFrom(src => src.GateStructureId))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.HealthMax, opt => opt.MapFrom(src => src.HealthMax ?? 500.0))
                .ForMember(dest => dest.HealthCurrent, opt => opt.MapFrom(src => src.HealthCurrent ?? src.HealthMax ?? 500.0))
                .ForMember(dest => dest.RespawnRateSeconds, opt => opt.MapFrom(src => src.RespawnRateSeconds ?? 300))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.IsActive ?? false))
                .ForMember(dest => dest.CanRespawn, opt => opt.MapFrom(src => src.CanRespawn ?? true))
                .ForMember(dest => dest.IsDestroyed, opt => opt.MapFrom(src => src.IsDestroyed ?? false))
                .ForMember(dest => dest.IsInvincible, opt => opt.MapFrom(src => src.IsInvincible ?? true))
                .ForMember(dest => dest.OpenedState, opt => opt.MapFrom(src => src.OpenedState))
                .ForMember(dest => dest.GateType, opt => opt.MapFrom(src => src.GateType))
                .ForMember(dest => dest.GeometryDefinitionMode, opt => opt.MapFrom(src => src.GeometryDefinitionMode))
                .ForMember(dest => dest.MotionType, opt => opt.MapFrom(src => src.MotionType))
                .ForMember(dest => dest.AnimationDurationTicks, opt => opt.MapFrom(src => src.AnimationDurationTicks ?? 60))
                .ForMember(dest => dest.AnimationTickRate, opt => opt.MapFrom(src => src.AnimationTickRate ?? 1))
                .ForMember(dest => dest.FaceDirection, opt => opt.MapFrom(src => src.FaceDirection))
                .ForMember(dest => dest.AnchorPointId, opt => opt.MapFrom(src => src.AnchorPointId))
                .ForMember(dest => dest.OpenAnchorPointId, opt => opt.MapFrom(src => src.OpenAnchorPointId))
                .ForMember(dest => dest.ReferencePoint1Id, opt => opt.MapFrom(src => src.ReferencePoint1Id))
                .ForMember(dest => dest.ReferencePoint2Id, opt => opt.MapFrom(src => src.ReferencePoint2Id))
                .ForMember(dest => dest.GeometryWidth, opt => opt.MapFrom(src => src.GeometryWidth ?? 0))
                .ForMember(dest => dest.GeometryHeight, opt => opt.MapFrom(src => src.GeometryHeight ?? 0))
                .ForMember(dest => dest.GeometryDepth, opt => opt.MapFrom(src => src.GeometryDepth ?? 0))
                .ForMember(dest => dest.MotionDistanceBlocks, opt => opt.MapFrom(src => src.MotionDistanceBlocks ?? 0))
                .ForMember(dest => dest.ClipToGeometryBounds, opt => opt.MapFrom(src => src.ClipToGeometryBounds ?? false))
                .ForMember(dest => dest.SeedBlocks, opt => opt.MapFrom(src => src.SeedBlocks ?? string.Empty))
                .ForMember(dest => dest.ScanMaxBlocks, opt => opt.MapFrom(src => src.ScanMaxBlocks ?? 500))
                .ForMember(dest => dest.ScanMaxRadius, opt => opt.MapFrom(src => src.ScanMaxRadius ?? 20))
                .ForMember(dest => dest.ScanMaterialWhitelist, opt => opt.MapFrom(src => src.ScanMaterialWhitelist ?? string.Empty))
                .ForMember(dest => dest.ScanMaterialBlacklist, opt => opt.MapFrom(src => src.ScanMaterialBlacklist ?? string.Empty))
                .ForMember(dest => dest.ScanPlaneConstraint, opt => opt.MapFrom(src => src.ScanPlaneConstraint ?? false))
                .ForMember(dest => dest.FallbackMaterialRefId, opt => opt.MapFrom(src => src.FallbackMaterialRefId))
                .ForMember(dest => dest.TileEntityPolicy, opt => opt.MapFrom(src => src.TileEntityPolicy))
                .ForMember(dest => dest.RotationMaxAngleDegrees, opt => opt.MapFrom(src => src.RotationMaxAngleDegrees ?? 90))
                .ForMember(dest => dest.HingeAxisId, opt => opt.MapFrom(src => src.HingeAxisId))
                .ForMember(dest => dest.LeftDoorSeedBlockId, opt => opt.MapFrom(src => src.LeftDoorSeedBlockId))
                .ForMember(dest => dest.RightDoorSeedBlockId, opt => opt.MapFrom(src => src.RightDoorSeedBlockId))
                .ForMember(dest => dest.MirrorRotation, opt => opt.MapFrom(src => src.MirrorRotation ?? true))
                .ForMember(dest => dest.RegionClosedId, opt => opt.MapFrom(src => src.RegionClosedId ?? string.Empty))
                .ForMember(dest => dest.RegionOpenedId, opt => opt.MapFrom(src => src.RegionOpenedId ?? string.Empty))
                .ForMember(dest => dest.AllowPassThrough, opt => opt.MapFrom(src => src.AllowPassThrough ?? false))
                .ForMember(dest => dest.PassThroughDurationSeconds, opt => opt.MapFrom(src => src.PassThroughDurationSeconds ?? 4))
                .ForMember(dest => dest.PassThroughConditionsJson, opt => opt.MapFrom(src => src.PassThroughConditionsJson ?? string.Empty))
                .ForMember(dest => dest.InfoDisplayLocationId, opt => opt.MapFrom(src => src.InfoDisplayLocationId))
                .ForMember(dest => dest.ShowHealthDisplay, opt => opt.MapFrom(src => src.ShowHealthDisplay ?? true))
                .ForMember(dest => dest.HealthDisplayMode, opt => opt.MapFrom(src => src.HealthDisplayMode))
                .ForMember(dest => dest.HealthDisplayYOffset, opt => opt.MapFrom(src => src.HealthDisplayYOffset ?? 2))
                .ForMember(dest => dest.GateNameDisplayMode, opt => opt.MapFrom(src => src.GateNameDisplayMode))
                .ForMember(dest => dest.StatusDisplayMode, opt => opt.MapFrom(src => src.StatusDisplayMode))
                .ForMember(dest => dest.DoorNameDisplayMode, opt => opt.MapFrom(src => src.DoorNameDisplayMode))
                .ForMember(dest => dest.AllowContinuousDamage, opt => opt.MapFrom(src => src.AllowContinuousDamage ?? true))
                .ForMember(dest => dest.ContinuousDamageMultiplier, opt => opt.MapFrom(src => src.ContinuousDamageMultiplier ?? 1.0))
                .ForMember(dest => dest.ContinuousDamageDurationSeconds, opt => opt.MapFrom(src => src.ContinuousDamageDurationSeconds ?? 5))
                // Ignore navigation properties - block snapshots are written exclusively via the
                // world-task scan pipeline (WorldTaskService / IGateDoorService), never through a
                // general door create/update. Carries over item 4's finding to the new owner.
                .ForMember(dest => dest.BlockSnapshots, opt => opt.Ignore())
                .ForMember(dest => dest.OpenedBlockSnapshots, opt => opt.Ignore())
                .ForMember(dest => dest.GateStructure, opt => opt.Ignore())
                .ForMember(dest => dest.AnchorPoint, opt => opt.Ignore())
                .ForMember(dest => dest.OpenAnchorPoint, opt => opt.Ignore())
                .ForMember(dest => dest.ReferencePoint1, opt => opt.Ignore())
                .ForMember(dest => dest.ReferencePoint2, opt => opt.Ignore())
                .ForMember(dest => dest.HingeAxis, opt => opt.Ignore())
                .ForMember(dest => dest.LeftDoorSeedBlock, opt => opt.Ignore())
                .ForMember(dest => dest.RightDoorSeedBlock, opt => opt.Ignore())
                .ForMember(dest => dest.InfoDisplayLocation, opt => opt.Ignore())
                .ForMember(dest => dest.FallbackMaterial, opt => opt.Ignore());

            // GateBlockSnapshot <-> GateBlockSnapshotDto (moved from GateStructureMappingProfile -
            // a scan targets one door, not a whole structure, since item 5's multi-door support)
            CreateMap<GateBlockSnapshot, GateBlockSnapshotDto>();
            CreateMap<GateBlockSnapshotDto, GateBlockSnapshot>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.GateDoor, opt => opt.Ignore());
            CreateMap<GateBlockSnapshotCreateDto, GateBlockSnapshot>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.GateDoor, opt => opt.Ignore());

            // GateOpenedBlockSnapshot <-> GateOpenedBlockSnapshotDto - mirrors the
            // GateBlockSnapshot mappings above exactly. See ROTATION_GAP_FILL_DESIGN.md.
            CreateMap<GateOpenedBlockSnapshot, GateOpenedBlockSnapshotDto>();
            CreateMap<GateOpenedBlockSnapshotDto, GateOpenedBlockSnapshot>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.GateDoor, opt => opt.Ignore());
            CreateMap<GateOpenedBlockSnapshotCreateDto, GateOpenedBlockSnapshot>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.GateDoor, opt => opt.Ignore());
        }
    }
}

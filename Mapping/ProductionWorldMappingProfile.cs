using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    /// <summary>Read mappings of the P2 structure subtypes (PandiO/knk-workspace#91): the Structure part comes from
    /// <see cref="StructureMappingProfile"/>; writes go through the services (StructureBaseWriter), not AutoMapper.</summary>
    public class ProductionWorldMappingProfile : Profile
    {
        public ProductionWorldMappingProfile()
        {
            CreateMap<Warehouse, WarehouseDto>()
                .IncludeBase<Structure, StructureDto>()
                .ForMember(d => d.Storages, o => o.Ignore());

            CreateMap<ProductionStructure, ProductionStructureDto>()
                .IncludeBase<Structure, StructureDto>()
                .ForMember(d => d.WarehousePreference, o => o.MapFrom(s => s.WarehousePreference.ToString()))
                .ForMember(d => d.RoadConnection, o => o.MapFrom(s => s.RoadConnection.ToString()))
                .ForMember(d => d.YieldSource, o => o.MapFrom(s => s.YieldSource.ToString()))
                .ForMember(d => d.PreferredWarehouse, o => o.MapFrom(s => s.PreferredWarehouse == null ? null
                    : new StructureStorageNavDto { Id = s.PreferredWarehouse.Id, Name = s.PreferredWarehouse.Name }))
                .ForMember(d => d.ProductionKind, o => o.MapFrom(s => s.ProductionKind == null ? null
                    : new ProductionKindNavDto { Id = s.ProductionKind.Id, Name = s.ProductionKind.Name }))
                .ForMember(d => d.Warnings, o => o.Ignore())
                .ForMember(d => d.Storages, o => o.Ignore());

            CreateMap<Warehouse, ProductionWorldListDto>()
                .IncludeBase<Structure, StructureListDto>()
                .ForMember(d => d.acceptsDeliveries, o => o.MapFrom(s => s.AcceptsDeliveries))
                .ForMember(d => d.priority, o => o.MapFrom(s => s.Priority))
                .ForMember(d => d.productionKindId, o => o.Ignore())
                .ForMember(d => d.level, o => o.Ignore())
                .ForMember(d => d.productionEnabled, o => o.Ignore())
                .ForMember(d => d.roadConnection, o => o.Ignore());

            CreateMap<ProductionStructure, ProductionWorldListDto>()
                .IncludeBase<Structure, StructureListDto>()
                .ForMember(d => d.acceptsDeliveries, o => o.Ignore())
                .ForMember(d => d.priority, o => o.Ignore())
                .ForMember(d => d.productionKindId, o => o.MapFrom(s => s.ProductionKindId))
                .ForMember(d => d.level, o => o.MapFrom(s => s.Level))
                .ForMember(d => d.productionEnabled, o => o.MapFrom(s => s.ProductionEnabled))
                .ForMember(d => d.roadConnection, o => o.MapFrom(s => s.RoadConnection.ToString()));
        }
    }
}

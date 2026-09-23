namespace knkwebapi_v2.Mapping
{
    using AutoMapper;
    using knkwebapi_v2.Dtos;
    using knkwebapi_v2.Models;

    public class PermissionMappingProfile : Profile
    {
        public PermissionMappingProfile()
        {
            CreateMap<PermissionGroup, PermissionGroupDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Weight, src => src.MapFrom(src => src.Weight))
                .ForMember(dest => dest.ChatPrefix, src => src.MapFrom(src => src.ChatPrefix))
                .ForMember(dest => dest.ChatSuffix, src => src.MapFrom(src => src.ChatSuffix))
                .ForMember(dest => dest.ParentGroupId, src => src.MapFrom(src => src.ParentGroupId.HasValue ? src.ParentGroupId : src.ParentGroup != null ? src.ParentGroup.Id : null))
                .ForMember(dest => dest.ParentGroup, src => src.MapFrom(src => src.ParentGroup))
                .ForMember(dest => dest.ChildGroups, src => src.MapFrom(s => s.ChildGroups));

            CreateMap<PermissionGroupDto, PermissionGroup>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Weight, src => src.MapFrom(src => src.Weight))
                .ForMember(dest => dest.ChatPrefix, src => src.MapFrom(src => src.ChatPrefix))
                .ForMember(dest => dest.ChatSuffix, src => src.MapFrom(src => src.ChatSuffix))
                .ForMember(dest => dest.ParentGroupId, src => src.MapFrom(src => src.ParentGroupId))
                .ForMember(dest => dest.ParentGroup, src => src.Ignore())
                .ForMember(dest => dest.ChildGroups, src => src.Ignore())
                .ForMember(dest => dest.UserMemberships, src => src.Ignore())
                .ForMember(dest => dest.Grants, src => src.Ignore());

            CreateMap<PermissionGroup, RelatedPermissionGroupDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.Weight, src => src.MapFrom(src => src.Weight));

            CreateMap<PermissionGroup, PermissionGroupListDto>()
                .ForMember(dest => dest.id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.name, src => src.MapFrom(src => src.Name))
                .ForMember(dest => dest.weight, src => src.MapFrom(src => src.Weight))
                .ForMember(dest => dest.parentGroupId, src => src.MapFrom(src => src.ParentGroupId))
                .ForMember(dest => dest.parentGroupName, src => src.MapFrom(src => src.ParentGroup != null ? src.ParentGroup.Name : null))
                .ForMember(dest => dest.childrenCount, src => src.MapFrom(src => src.ChildGroups != null ? src.ChildGroups.Count : 0));

            CreateMap<PermissionGrant, PermissionGrantDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.HolderId, src => src.MapFrom(src => src.HolderId))
                .ForMember(dest => dest.HolderType, src => src.MapFrom(src => src.Holder != null ? src.Holder.GetType().Name : null))
                .ForMember(dest => dest.Node, src => src.MapFrom(src => src.Node))
                .ForMember(dest => dest.Value, src => src.MapFrom(src => src.Value))
                .ForMember(dest => dest.ExpiresAt, src => src.MapFrom(src => src.ExpiresAt));

            CreateMap<PermissionGrantDto, PermissionGrant>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id ?? 0))
                .ForMember(dest => dest.HolderId, src => src.MapFrom(src => src.HolderId))
                .ForMember(dest => dest.Node, src => src.MapFrom(src => src.Node))
                .ForMember(dest => dest.Value, src => src.MapFrom(src => src.Value))
                .ForMember(dest => dest.ExpiresAt, src => src.MapFrom(src => src.ExpiresAt))
                .ForMember(dest => dest.Holder, src => src.Ignore());

            CreateMap<PermissionGrant, PermissionGrantListDto>()
                .ForMember(dest => dest.id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.holderId, src => src.MapFrom(src => src.HolderId))
                .ForMember(dest => dest.holderType, src => src.MapFrom(src => src.Holder != null ? src.Holder.GetType().Name : null))
                .ForMember(dest => dest.node, src => src.MapFrom(src => src.Node))
                .ForMember(dest => dest.value, src => src.MapFrom(src => src.Value))
                .ForMember(dest => dest.expiresAt, src => src.MapFrom(src => src.ExpiresAt));
        }
    }
}

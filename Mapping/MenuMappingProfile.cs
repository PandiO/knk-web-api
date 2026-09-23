using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class MenuMappingProfile : Profile
    {
        public MenuMappingProfile()
        {
            // Enum <-> string members match by name, so AutoMapper's default
            // conversion handles Growth/Kind/PositionMode/etc. without a resolver.

            CreateMap<MenuTemplate, MenuTemplateDto>()
                .ForMember(dest => dest.Sections, opt => opt.MapFrom(src => src.Sections.OrderBy(s => s.SortOrder)));

            CreateMap<MenuTemplate, MenuTemplateListDto>()
                .ForMember(dest => dest.SectionCount, opt => opt.MapFrom(src => src.Sections.Count));

            CreateMap<MenuSectionTemplate, MenuSectionTemplateDto>()
                .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.Items.OrderBy(i => i.SortOrder)))
                .ForMember(dest => dest.VariableBindings, opt => opt.MapFrom(src => src.VariableBindings.OrderBy(b => b.SortOrder)));

            CreateMap<MenuItemTemplate, MenuItemTemplateDto>()
                .ForMember(dest => dest.VariableBindings, opt => opt.MapFrom(src => src.VariableBindings.OrderBy(b => b.SortOrder)))
                .ForMember(dest => dest.Actions, opt => opt.MapFrom(src => src.Actions.OrderBy(a => a.SortOrder)))
                .ForMember(dest => dest.Conditions, opt => opt.MapFrom(src => src.Conditions.Where(c => c.ActionBindingId == null).OrderBy(c => c.SortOrder)));

            CreateMap<VariableBinding, VariableBindingDto>();

            CreateMap<ActionBinding, ActionBindingDto>()
                .ForMember(dest => dest.Conditions, opt => opt.MapFrom(src => src.Conditions.OrderBy(c => c.SortOrder)));

            CreateMap<ConditionBinding, ConditionBindingDto>();
        }
    }
}

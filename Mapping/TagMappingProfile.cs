using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class TagMappingProfile : Profile
    {
        public TagMappingProfile()
        {
            CreateMap<Tag, TagReadDto>();
            CreateMap<Tag, TagNavDto>();
            CreateMap<Tag, TagListDto>();

            CreateMap<TagCreateDto, Tag>()
                .ForMember(dest => dest.Id, opt => opt.Ignore());
            CreateMap<TagUpdateDto, Tag>();
        }
    }
}

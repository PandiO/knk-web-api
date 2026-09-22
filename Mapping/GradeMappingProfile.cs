using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class GradeMappingProfile : Profile
    {
        public GradeMappingProfile()
        {
            CreateMap<Grade, GradeReadDto>();
            CreateMap<Grade, GradeNavDto>();
            CreateMap<Grade, GradeListDto>();

            CreateMap<GradeCreateDto, Grade>()
                .ForMember(dest => dest.Id, opt => opt.Ignore());
            CreateMap<GradeUpdateDto, Grade>();
        }
    }
}

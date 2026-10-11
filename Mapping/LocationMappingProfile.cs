using System;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping;

public class LocationMappingProfile : Profile
{
    public LocationMappingProfile()
    {
        CreateMap<Location, LocationDto>().ReverseMap()
            .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
            .ForMember(dest => dest.Name, src => src.MapFrom(src => src.Name))
            .ForMember(dest => dest.X, src => src.MapFrom(src => src.X))
            .ForMember(dest => dest.Y, src => src.MapFrom(src => src.Y))
            .ForMember(dest => dest.Z, src => src.MapFrom(src => src.Z))
            .ForMember(dest => dest.Yaw, src => src.MapFrom(src => src.Yaw))
            .ForMember(dest => dest.Pitch, src => src.MapFrom(src => src.Pitch))
            .ForMember(dest => dest.World, src => src.MapFrom(src => src.World))
            // Set once on creation (Location's initializer); an update must never move it (KNG-80).
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore());
    }
}

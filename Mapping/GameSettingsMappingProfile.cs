using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Mapping;

public class GameSettingsMappingProfile : Profile
{
    public GameSettingsMappingProfile()
    {
        CreateMap<GameSettings, GameSettingsReadDto>()
            .ForMember(dest => dest.JoinSpawnReference,
                opt => opt.MapFrom(src => GameSettingsJson.Deserialize<LocationReferenceDto>(src.JoinSpawnReferenceJson)))
            .ForMember(dest => dest.DefaultRespawnPolicy,
                opt => opt.MapFrom(src => GameSettingsJson.Deserialize<RespawnPolicyDto>(src.DefaultRespawnPolicyJson)))
            .ForMember(dest => dest.WorldSettings,
                opt => opt.MapFrom(src => GameSettingsJson.DeserializeList<WorldGameSettingsDto>(src.WorldSettingsJson)))
            .ForMember(dest => dest.RuntimeWorlds,
                opt => opt.MapFrom(src => GameSettingsJson.DeserializeList<MinecraftWorldRuntimeDto>(src.RuntimeWorldsJson)))
            .ForMember(dest => dest.GroupOverrides,
                opt => opt.MapFrom(src => GameSettingsJson.DeserializeList<PermissionGroupGameSettingsDto>(src.GroupOverridesJson)));
    }
}

using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    // Siege weapons phase 1 (SIEGE_WEAPONS.md § 9, PandiO/knk-workspace#782). Read side only: writes go through
    // SiegeWeaponService, where the rules are. SiegeWeaponSpot -> SiegeWeaponSpotReadDto is also used by the scenario's
    // read DTO (SiegeMappingProfile).
    public class SiegeWeaponMappingProfile : Profile
    {
        public SiegeWeaponMappingProfile()
        {
            CreateMap<SiegeWeaponTemplateAmmoType, SiegeWeaponTemplateAmmoTypeDto>()
                .ForMember(d => d.SiegeAmmoTypeName, o => o.MapFrom(s => s.SiegeAmmoType != null ? s.SiegeAmmoType.Name : null));

            CreateMap<SiegeWeaponTemplate, SiegeWeaponTemplateReadDto>()
                .ForMember(d => d.AllowedAmmoTypes, o => o.MapFrom(s => s.AllowedAmmoTypes.OrderBy(a => a.SiegeAmmoTypeId)));

            CreateMap<SiegeWeaponTemplate, SiegeWeaponTemplateListDto>()
                .ForMember(d => d.AmmoTypeCount, o => o.MapFrom(s => s.AllowedAmmoTypes.Count));

            CreateMap<SiegeAmmoType, SiegeAmmoTypeReadDto>();
            CreateMap<SiegeAmmoType, SiegeAmmoTypeListDto>();

            CreateMap<SiegeWeaponSpot, SiegeWeaponSpotReadDto>()
                .ForMember(d => d.SiegeTeamName, o => o.MapFrom(s => s.SiegeTeam != null ? SiegeTeamIdentity.ResolveName(s.SiegeTeam) : null));

            CreateMap<UserSiegeWeaponUnlock, UserSiegeWeaponUnlockReadDto>()
                .ForMember(d => d.Username, o => o.MapFrom(s => s.User != null ? s.User.Username : null))
                .ForMember(d => d.SiegeWeaponTemplateName, o => o.MapFrom(s => s.SiegeWeaponTemplate != null ? s.SiegeWeaponTemplate.Name : null));
        }
    }
}

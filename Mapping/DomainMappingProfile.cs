using System.Collections.ObjectModel;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping;

public class DomainMappingProfile : Profile
{
    public DomainMappingProfile()
    {
        CreateMap<Domain, DomainDto>();
        CreateMap<DomainDto, Domain>();

        CreateMap<Domain, DomainListDto>()
            .ForMember(dest => dest.DomainType, opt => opt.MapFrom(src => src.GetType().Name))
            // Needs the type defaults: DomainService.SearchAsync fills it (KNG-73).
            .ForMember(dest => dest.NavigationDefault, opt => opt.Ignore())
            .ForMember(dest => dest.RoadAccess, opt => opt.Ignore());

        CreateMap<Domain, DomainRegionDecisionDto>()
            .ForMember(dest => dest.DomainType, opt => opt.MapFrom(src => src.GetType().Name))
            .ForMember(dest => dest.ParentDomainDecisions, opt => opt.MapFrom(src => GetParentDomainDecisions(src)));
    }

    private static Collection<DomainRegionDecisionDto> GetParentDomainDecisions(Domain domain)
    {
        var decisions = new Collection<DomainRegionDecisionDto>();

        Domain? current = domain;
        while (current != null)
        {

            if (current is Town) {
                break;
            } else if (current is District district)
            {
                if (district.TownId <= 0) break;
                current = district.Town;
                // continue;
            }  else if (current is Structure structure) {
                if (structure.DistrictId <= 0) break;
                current = structure.District;
                // continue;
            }
            decisions.Add(new DomainRegionDecisionDto
            {
                Id = current.Id,
                Name = current.Name,
                WgRegionId = current.WgRegionId,
                WorldName = current.WorldName,
                AllowEntry = current.AllowEntry,
                AllowExit = current.AllowExit,
                DomainType = current.GetType().Name
            });
            // break;
        }
        return decisions;
    }

    private static Collection<DomainRegionDecisionDto> GetChildDomainDecisions(Domain domain)
    {
        var decisions = new Collection<DomainRegionDecisionDto>();

        Domain? current = domain;
        while (current != null)
        {

            if (current is Town town) {
                if (town.Districts == null || town.Districts.Count == 0) break;
                current = town.Districts.First();
            } else if (current is District district)
            {
                if (district.TownId <= 0) break;
                current = district.Town;
                // continue;
            }  else if (current is Structure structure) {
                if (structure.DistrictId <= 0) break;
                current = structure.District;
                // continue;
            }
            decisions.Add(new DomainRegionDecisionDto
            {
                Id = current.Id,
                Name = current.Name,
                WgRegionId = current.WgRegionId,
                WorldName = current.WorldName,
                AllowEntry = current.AllowEntry,
                AllowExit = current.AllowExit,
                DomainType = current.GetType().Name
            });
            // break;
        }

        return decisions;
    }
}

/// <summary>
/// The Domain warp settings (teleport DESIGN.md §3.7.1) are never mapped from a Town/District/
/// Structure DTO: services apply them through DomainTeleportSettings.Apply after validation, so a
/// form without the teleport fields leaves them untouched.
/// </summary>
public static class DomainTeleportSettingsMapping
{
    public static IMappingExpression<TSource, TDomain> IgnoreTeleportSettings<TSource, TDomain>(
        this IMappingExpression<TSource, TDomain> map) where TDomain : Domain =>
        map.ForMember(dest => dest.TeleportEnabled, opt => opt.Ignore())
            .ForMember(dest => dest.TeleportPriceGems, opt => opt.Ignore())
            .ForMember(dest => dest.TeleportMinTitleBracketId, opt => opt.Ignore())
            .ForMember(dest => dest.TeleportMinTitleBracket, opt => opt.Ignore())
            .ForMember(dest => dest.TeleportMinPremiumGroupId, opt => opt.Ignore())
            .ForMember(dest => dest.TeleportMinPremiumGroup, opt => opt.Ignore())
            .ForMember(dest => dest.TeleportRequiresDiscovery, opt => opt.Ignore());
}

/// <summary>
/// The /navigate override (KNG-73) is never mapped from a Town/District/Structure/GateStructure DTO:
/// services apply it through DomainNavigationDefaults.Apply, so a form or a game-server update
/// without the field leaves it untouched (the DTO's string is null then).
/// </summary>
public static class DomainNavigationDefaultMapping
{
    public static IMappingExpression<TSource, TDomain> IgnoreNavigationDefault<TSource, TDomain>(
        this IMappingExpression<TSource, TDomain> map) where TDomain : Domain =>
        map.ForMember(dest => dest.NavigationDefaultOverride, opt => opt.Ignore())
            .ForMember(dest => dest.RoadAccessOverride, opt => opt.Ignore());
}

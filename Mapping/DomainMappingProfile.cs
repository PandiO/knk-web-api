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
            .ForMember(dest => dest.DomainType, opt => opt.MapFrom(src => src.GetType().Name));

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
                AllowEntry = current.AllowEntry,
                AllowExit = current.AllowExit,
                DomainType = current.GetType().Name
            });
            // break;
        }

        return decisions;
    }
}

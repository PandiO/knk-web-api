using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    public class DomainService : IDomainService
    {
        private readonly IDomainRepository _repo;
        private readonly IMapper _mapper;
        private readonly IDomainRegionNameFinalizer _regionNames;
        private readonly ILogger<DomainService> _logger;
        private readonly IDomainWorldResolver _worlds;

        public DomainService(IDomainRepository repo, IMapper mapper, IDomainRegionNameFinalizer regionNames, ILogger<DomainService> logger,
            IDomainWorldResolver worlds)
        {
            _repo = repo;
            _mapper = mapper;
            _regionNames = regionNames;
            _logger = logger;
            _worlds = worlds;
        }

        public async Task<IEnumerable<Domain>> GetAllAsync()
        {
            return await _repo.GetAllAsync();
        }

        public async Task<Domain?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            return await _repo.GetByIdAsync(id);
        }

        public async Task<Domain> CreateAsync(Domain domain)
        {
            if (domain == null) throw new ArgumentNullException(nameof(domain));
            if (string.IsNullOrWhiteSpace(domain.Name)) throw new ArgumentException("Domain name is required.", nameof(domain));
            domain.WorldName = await _worlds.ResolveAsync(new DomainWorldRequest
            {
                RequestedWorld = domain.WorldName,
                WgRegionId = domain.WgRegionId,
                LocationId = domain.LocationId
            });

            await _repo.AddDomainAsync(domain);
            
            // A region drawn through a world task gets its final name (domain_<id>) now that the Domain exists
            await _regionNames.FinalizeAsync(domain);
            
            return domain;
        }

        public async Task UpdateAsync(int id, Domain domain)
        {
            if (domain == null) throw new ArgumentNullException(nameof(domain));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(domain.Name)) throw new ArgumentException("Domain name is required.", nameof(domain));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Domain with id {id} not found.");
            // KNG-78: this path can also rename a GateStructure; 'here' is reserved for gates only.
            if (existing is GateStructure)
                GateNameRules.EnsureNotReserved(domain.Name, "gate structure", "/gate", nameof(domain));

            string oldRegionId = existing.WgRegionId;
            string worldName = await _worlds.ResolveAsync(new DomainWorldRequest
            {
                DomainId = id,
                RequestedWorld = domain.WorldName,
                WgRegionId = domain.WgRegionId,
                LocationId = domain.LocationId,
                ParentDomainId = existing switch
                {
                    District district => district.TownId,
                    Structure structure => structure.DistrictId,
                    _ => null
                }
            });
            
            existing.Name = domain.Name;
            existing.Description = domain.Description;
            existing.AllowEntry = domain.AllowEntry;
            existing.AllowExit = domain.AllowExit;
            existing.LocationId = domain.LocationId;
            existing.WgRegionId = domain.WgRegionId;
            existing.WorldName = worldName;

            await _repo.UpdateDomainAsync(existing);
            
            // A region drawn through a world task gets its final name (domain_<id>) now that the Domain exists
            await _regionNames.FinalizeAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Domain with id {id} not found.");

            await _repo.DeleteDomainAsync(id);
        }

        public async Task<DomainRegionDecisionDto?> GetByWgRegionNameAsync(string regionName, string? worldName = null)
        {
            if (string.IsNullOrWhiteSpace(regionName)) return null;
            var domain = await FindByRegionAsync(regionName, worldName);
            if (domain == null) return null;
            return _mapper.Map<DomainRegionDecisionDto>(domain);
        }

        /// <summary>KNG-111: world-qualified when the caller names the world, the old world-blind lookup otherwise.</summary>
        private Task<Domain?> FindByRegionAsync(string regionId, string? worldName) =>
            string.IsNullOrWhiteSpace(worldName)
                ? _repo.GetByWgRegionNameAsync(regionId)
                : _repo.GetByWgRegionNameAsync(regionId, worldName);

        public async Task<PagedResultDto<DomainListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            var dto = _mapper.Map<PagedResultDto<DomainListDto>>(result);

            // KNG-73: the effective /navigate default, so the game server's catalogue needs no extra call.
            if (result?.Items != null)
            {
                var typeDefaults = await _repo.GetNavigationDefaultsAsync();
                var roadAccessDefaults = await _repo.GetRoadAccessDefaultsAsync();
                var byId = result.Items.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
                foreach (var item in dto.Items)
                {
                    if (item.Id is int id && byId.TryGetValue(id, out var domain))
                    {
                        item.NavigationDefault = DomainNavigationDefaults.Effective(domain, typeDefaults).ToString();
                        // Rev. 7 Part C (KNG-92): whether the game server's road router heeds its entry rule.
                        item.RoadAccess = DomainNavigationDefaults.EffectiveRoadAccess(domain, roadAccessDefaults).ToString();
                    }
                }
            }
            return dto;
        }

        /// <summary>
        /// AllowEntry/AllowExit of every domain that has a WorldGuard region, for the game server's
        /// flag sync (KNG-56). Domains without a region can't be entered or left in game and are skipped.
        /// </summary>
        public async Task<IReadOnlyList<DomainAccessRuleDto>> GetAccessRulesAsync()
        {
            var domains = await _repo.GetAllAsync();
            return domains
                .Where(d => !string.IsNullOrWhiteSpace(d.WgRegionId))
                .OrderBy(d => d.Id)
                .Select(d => new DomainAccessRuleDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    WgRegionId = d.WgRegionId,
                    WorldName = d.WorldName,
                    AllowEntry = d.AllowEntry,
                    AllowExit = d.AllowExit,
                    DomainType = d.GetType().Name
                })
                .ToList();
        }

        /// <summary>
        /// Searches for domain region decisions based on the provided query criteria.
        /// </summary>
        /// <param name="queryDto">The query data transfer object containing WgRegionIds to search for.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains the domain region decision 
        /// with the lowest count of ParentDomainDecisions, or null if no decisions are found or queryDto.WgRegionIds is null.
        /// </returns>
        /// <remarks>
        /// This method retrieves domains by region IDs, maps them to DomainRegionDecisionDto objects, and returns 
        /// the one with the minimum number of parent domain decisions. Results are sorted in ascending order by 
        /// ParentDomainDecisions count.
        /// </remarks>
        public async Task<Dictionary<int, DomainRegionDecisionDto>> SearchDomainRegionDecisionAsync(DomainRegionQueryDto queryDto)
        {
            Dictionary<int, DomainRegionDecisionDto> result = new Dictionary<int, DomainRegionDecisionDto>();
            if (queryDto?.WgRegionIds == null) return result;

            var domainDecisions = new List<DomainRegionDecisionDto>();
            foreach (var regionId in queryDto.WgRegionIds)
            {
                var domain = await FindByRegionAsync(regionId, queryDto.WorldName);
                if (domain != null)
                {
                    domainDecisions.Add(_mapper.Map<DomainRegionDecisionDto>(domain));
                }
            }

            if (domainDecisions.Count == 0) return result;

            domainDecisions.Sort((a, b) => Comparer<int>.Default.Compare(a.ParentDomainDecisions.Count, b.ParentDomainDecisions.Count));

            var townDecision = domainDecisions.FirstOrDefault(d => d.DomainType == "Town");
            var districtDecision = domainDecisions.FirstOrDefault(d => d.DomainType == "District");
            // A GateStructure is a Structure (gates, Keep Gate): left out, the game server never saw its entry rule
            // (live test 2026-10-09: the navigator walked players into the Keep Gate the border then refused).
            var structureDecision = domainDecisions.FirstOrDefault(d => d.DomainType == "Structure" || d.DomainType == "GateStructure");
            int hierarchyIndex = 0;
            if (queryDto.TopDownHierarchy == true)
            {

                if (townDecision != null)
                {
                    result.Add(hierarchyIndex++, townDecision);
                }
                if (districtDecision != null)
                {
                    result.Add(hierarchyIndex++, districtDecision);
                }
                if (structureDecision != null)
                {
                    result.Add(hierarchyIndex++, structureDecision);
                }
            }
            else
            {
                hierarchyIndex = 3;
                if (townDecision != null)
                {
                    result.Add(--hierarchyIndex, townDecision);
                }
                if (districtDecision != null)
                {
                    result.Add(--hierarchyIndex, districtDecision);
                }
                if (structureDecision != null)
                {
                    result.Add(--hierarchyIndex, structureDecision);
                }
            }

            return result;
        }
    }
}


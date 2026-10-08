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

        public DomainService(IDomainRepository repo, IMapper mapper, IDomainRegionNameFinalizer regionNames, ILogger<DomainService> logger)
        {
            _repo = repo;
            _mapper = mapper;
            _regionNames = regionNames;
            _logger = logger;
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
            
            existing.Name = domain.Name;
            existing.Description = domain.Description;
            existing.AllowEntry = domain.AllowEntry;
            existing.AllowExit = domain.AllowExit;
            existing.LocationId = domain.LocationId;
            existing.WgRegionId = domain.WgRegionId;

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

        public async Task<DomainRegionDecisionDto?> GetByWgRegionNameAsync(string regionName)
        {
            if (string.IsNullOrWhiteSpace(regionName)) return null;
            var domain = await _repo.GetByWgRegionNameAsync(regionName);
            if (domain == null) return null;
            return _mapper.Map<DomainRegionDecisionDto>(domain);
        }

        public async Task<PagedResultDto<DomainListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<DomainListDto>>(result);
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
                var domain = await _repo.GetByWgRegionNameAsync(regionId);
                if (domain != null)
                {
                    domainDecisions.Add(_mapper.Map<DomainRegionDecisionDto>(domain));
                }
            }

            if (domainDecisions.Count == 0) return result;

            domainDecisions.Sort((a, b) => Comparer<int>.Default.Compare(a.ParentDomainDecisions.Count, b.ParentDomainDecisions.Count));

            var townDecision = domainDecisions.FirstOrDefault(d => d.DomainType == "Town");
            var districtDecision = domainDecisions.FirstOrDefault(d => d.DomainType == "District");
            var structureDecision = domainDecisions.FirstOrDefault(d => d.DomainType == "Structure");
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


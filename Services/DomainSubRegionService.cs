using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// A domain's sub-regions (KNG-254). A sub-region lives in its domain's world, its region id is used by no other
    /// domain or sub-region there, and after every save <see cref="IDomainRegionNameFinalizer.FinalizeSubRegionAsync"/>
    /// gives it its final name. The game server keeps it a copy of the main region.
    /// </summary>
    public class DomainSubRegionService : IDomainSubRegionService
    {
        public const int MaxRegionIdLength = 128;
        public const int MaxNameLength = 64;

        private readonly KnKDbContext _context;
        private readonly IDomainRegionNameFinalizer _regionNames;
        private readonly ILogger<DomainSubRegionService> _logger;

        public DomainSubRegionService(KnKDbContext context, IDomainRegionNameFinalizer regionNames, ILogger<DomainSubRegionService> logger)
        {
            _context = context;
            _regionNames = regionNames;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DomainSubRegionDto>> GetByDomainAsync(int domainId)
        {
            return await _context.DomainSubRegions.AsNoTracking()
                .Where(s => s.DomainId == domainId)
                .OrderBy(s => s.Id)
                .Select(s => ToDto(s))
                .ToListAsync();
        }

        public async Task<DomainSubRegionDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var subRegion = await _context.DomainSubRegions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            return subRegion == null ? null : ToDto(subRegion);
        }

        public async Task<DomainSubRegionDto> CreateAsync(DomainSubRegionDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var domain = await DomainAsync(dto.DomainId);
            string regionId = NormalizeRegionId(dto.WgRegionId);
            await ValidateAsync(domain, regionId, null);

            var subRegion = new DomainSubRegion
            {
                DomainId = domain.Id,
                WgRegionId = regionId,
                Name = NormalizeName(dto.Name),
                CreatedAt = DateTime.Now
            };
            _context.DomainSubRegions.Add(subRegion);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Sub-region {SubRegionId} ({RegionId}) added to Domain {DomainId}", subRegion.Id, regionId, domain.Id);

            await _regionNames.FinalizeSubRegionAsync(subRegion, null);
            return ToDto(subRegion);
        }

        public async Task<DomainSubRegionDto> UpdateAsync(int id, DomainSubRegionDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var subRegion = await _context.DomainSubRegions.FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new KeyNotFoundException($"DomainSubRegion with id {id} not found.");
            if (dto.DomainId > 0 && dto.DomainId != subRegion.DomainId)
            {
                throw new ArgumentException("A sub-region can't move to another domain: remove it and add a new one there.", nameof(dto));
            }
            var domain = await DomainAsync(subRegion.DomainId);
            string regionId = NormalizeRegionId(dto.WgRegionId);
            string previousRegionId = subRegion.WgRegionId;
            if (!regionId.Equals(previousRegionId, StringComparison.OrdinalIgnoreCase))
            {
                await ValidateAsync(domain, regionId, subRegion.Id);
            }

            subRegion.WgRegionId = regionId;
            subRegion.Name = NormalizeName(dto.Name);
            await _context.SaveChangesAsync();

            // A re-run of its world task (KNG-103): the new region replaces the old one under the final name.
            await _regionNames.FinalizeSubRegionAsync(subRegion, previousRegionId);
            return ToDto(subRegion);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var subRegion = await _context.DomainSubRegions.FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new KeyNotFoundException($"DomainSubRegion with id {id} not found.");
            _context.DomainSubRegions.Remove(subRegion);
            await _context.SaveChangesAsync();
            // The WorldGuard region stays: the game server's next access sync sees it is an orphan and deletes it after
            // the grace period (DESIGN.md §8); until then it can be adopted again with "select".
            _logger.LogInformation("Sub-region {SubRegionId} ({RegionId}) removed from Domain {DomainId}; its region is now an orphan",
                subRegion.Id, subRegion.WgRegionId, subRegion.DomainId);
        }

        private async Task<Domain> DomainAsync(int domainId)
        {
            if (domainId <= 0) throw new ArgumentException("The sub-region's domain is required.", nameof(domainId));
            return await _context.Domains.AsNoTracking().FirstOrDefaultAsync(d => d.Id == domainId)
                ?? throw new KeyNotFoundException($"Domain with id {domainId} not found.");
        }

        private async Task ValidateAsync(Domain domain, string regionId, int? subRegionId)
        {
            if (string.IsNullOrWhiteSpace(domain.WgRegionId))
            {
                throw new ArgumentException("Give the domain its main region before adding sub-regions.");
            }
            if (regionId.Equals(domain.WgRegionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainWorldException(DomainWorldException.RegionTaken, $"Region '{regionId}' is the domain's main region.");
            }

            // The world the region was drawn in, when it still has its world task's name.
            string? drawnIn = await RegionTaskWorldAsync(regionId);
            string? world = string.IsNullOrWhiteSpace(domain.WorldName) ? null : domain.WorldName.Trim();
            if (drawnIn != null && world != null && !drawnIn.Equals(world, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainWorldException(DomainWorldException.WorldConflict,
                    $"The region was drawn in world '{drawnIn}', but {domain.Name} is in world '{world}': a sub-region must be in its domain's world.");
            }

            string? takenBy = await DomainRegionOwnership.TakenByAsync(_context, regionId, world ?? drawnIn, exceptSubRegionId: subRegionId);
            if (takenBy != null)
            {
                throw new DomainWorldException(DomainWorldException.RegionTaken, takenBy);
            }
        }

        private async Task<string?> RegionTaskWorldAsync(string regionId)
        {
            if (!DomainRegionNameFinalizer.IsTemporary(regionId)) return null;
            if (!int.TryParse(regionId.Substring(DomainRegionNameFinalizer.TempRegionPrefix.Length), out int taskId) || taskId <= 0) return null;
            string? output = await _context.WorldTasks.AsNoTracking()
                .Where(t => t.Id == taskId)
                .Select(t => t.OutputJson)
                .FirstOrDefaultAsync();
            return DomainWorldResolver.WorldNameFromTaskOutput(output);
        }

        private static string NormalizeRegionId(string? regionId)
        {
            string value = regionId?.Trim() ?? string.Empty;
            if (value.Length == 0) throw new ArgumentException("The sub-region's region is required.");
            if (value.Length > MaxRegionIdLength) throw new ArgumentException($"The region id is longer than {MaxRegionIdLength} characters.");
            return value;
        }

        private static string? NormalizeName(string? name)
        {
            string? value = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            if (value != null && value.Length > MaxNameLength) throw new ArgumentException($"The name is longer than {MaxNameLength} characters.");
            return value;
        }

        private static DomainSubRegionDto ToDto(DomainSubRegion s) => new()
        {
            Id = s.Id,
            DomainId = s.DomainId,
            WgRegionId = s.WgRegionId,
            Name = s.Name,
            CreatedAt = s.CreatedAt
        };
    }
}

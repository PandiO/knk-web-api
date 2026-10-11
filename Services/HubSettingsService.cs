using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The hub's configuration rules (hub DESIGN §13, §16.4): which domains may be the hub, the selected hub and its
    /// spawn, the outage action, and which groups may carry the mode exemptions (KNG-116).
    /// </summary>
    public interface IHubSettingsService
    {
        /// <summary>Every hub-eligible domain, with the selected one marked.</summary>
        Task<List<HubDomainDto>> ListEligibleAsync();

        /// <summary>
        /// Flags or unflags a domain as hub-eligible. Throws <see cref="ArgumentException"/> when the domain would
        /// contain or sit inside another flagged domain (decision 4), or when it is the selected hub and is unflagged.
        /// Null when no domain has this id.
        /// </summary>
        Task<HubDomainDto?> SetEligibleAsync(int domainId, bool isHub);

        /// <summary>Validates and normalizes the hub block (throws <see cref="ArgumentException"/>).</summary>
        Task<HubSettingsDto> NormalizeAsync(HubSettingsDto hub);

        /// <summary>Fills in the read-only fields of the hub block (domain name, type, world, region).</summary>
        Task<HubSettingsDto> DescribeAsync(HubSettingsDto hub);

        /// <summary>Per group, whether it effectively has knk.mode.staff / knk.mode.owner.</summary>
        Task<List<HubExemptionEligibilityDto>> ExemptionEligibilityAsync();

        /// <summary>Throws when an override ticks an exemption its group is not eligible for.</summary>
        Task ValidateExemptionsAsync(IEnumerable<PermissionGroupGameSettingsDto> overrides);
    }

    public class HubSettingsService : IHubSettingsService
    {
        public const string StaffModeNode = "knk.mode.staff";
        public const string OwnerModeNode = "knk.mode.owner";

        /// <summary>The outage actions (§1, §2).</summary>
        public static readonly string[] OutageActions = { "None", "SendToHub", "Kick", "SendToWorldSpawn" };

        /// <summary>An outage delay longer than a day is a mistake, not a policy.</summary>
        public const int MaxOutageDelaySeconds = 24 * 60 * 60;

        private readonly KnKDbContext _context;
        private readonly IPermissionResolutionService _permissions;

        public HubSettingsService(KnKDbContext context, IPermissionResolutionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        public async Task<List<HubDomainDto>> ListEligibleAsync()
        {
            int? selected = (await CurrentHubAsync())?.DomainId;
            var domains = await _context.Domains.AsNoTracking().Where(d => d.IsHub).OrderBy(d => d.Name).ToListAsync();
            return domains.Select(d => ToDto(d, selected)).ToList();
        }

        public async Task<HubDomainDto?> SetEligibleAsync(int domainId, bool isHub)
        {
            var domain = await _context.Domains.FirstOrDefaultAsync(d => d.Id == domainId);
            if (domain == null)
            {
                return null;
            }
            var current = await CurrentHubAsync();
            if (!isHub && current?.DomainId == domainId)
            {
                throw new ArgumentException($"'{domain.Name}' is the selected hub; choose another hub (or none) first");
            }
            if (isHub && !domain.IsHub)
            {
                var related = await RelatedHubAsync(domain);
                if (related != null)
                {
                    throw new ArgumentException($"'{domain.Name}' and '{related.Name}' would be nested hubs; "
                        + "a hub-eligible domain may not contain or sit inside another (decision 4)");
                }
            }
            domain.IsHub = isHub;
            await _context.SaveChangesAsync();
            return ToDto(domain, current?.DomainId);
        }

        public async Task<HubSettingsDto> NormalizeAsync(HubSettingsDto hub)
        {
            hub ??= new HubSettingsDto();
            var action = OutageActions.FirstOrDefault(a => a.Equals(hub.OutageAction?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (action == null)
            {
                throw new ArgumentException($"hub.outageAction must be one of {string.Join(", ", OutageActions)}");
            }
            if (hub.OutageDelaySeconds < 0 || hub.OutageDelaySeconds > MaxOutageDelaySeconds)
            {
                throw new ArgumentException($"hub.outageDelaySeconds must be between 0 (immediately) and {MaxOutageDelaySeconds}");
            }
            var rules = hub.Rules ?? new HubRulesDto();
            if (rules.HealAmount < 0 || rules.FeedAmount < 0 || rules.SaturationAmount < 0)
            {
                throw new ArgumentException("hub.rules: heal, feed and saturation amounts may not be negative");
            }
            if (rules.HealIntervalTicks < 1 || rules.FeedIntervalTicks < 1 || rules.SaturationIntervalTicks < 1)
            {
                throw new ArgumentException("hub.rules: heal, feed and saturation intervals are at least 1 tick");
            }

            var normalized = new HubSettingsDto
            {
                DomainId = hub.DomainId,
                SpawnReference = hub.SpawnReference,
                OutageAction = action,
                OutageDelaySeconds = hub.OutageDelaySeconds,
                Rules = rules
            };
            if (hub.DomainId is not int domainId)
            {
                return normalized;
            }

            var domain = await _context.Domains.AsNoTracking().FirstOrDefaultAsync(d => d.Id == domainId);
            if (domain == null)
            {
                throw new ArgumentException($"hub.domainId: domain {domainId} does not exist");
            }
            if (!domain.IsHub)
            {
                throw new ArgumentException($"hub.domainId: '{domain.Name}' is not hub-eligible; flag it as a hub first");
            }
            if (string.IsNullOrWhiteSpace(domain.WgRegionId))
            {
                throw new ArgumentException($"hub.domainId: '{domain.Name}' has no WorldGuard region; the hub's rules apply inside it");
            }
            if (string.IsNullOrWhiteSpace(domain.WorldName))
            {
                throw new ArgumentException($"hub.domainId: '{domain.Name}' has no world yet; set it in the domain's form first");
            }
            // Flags are checked for nesting when set, but moving a District or Structure to another parent later can
            // still nest two flagged domains: never select one that is nested (decision 4).
            var nested = await RelatedHubAsync(domain);
            if (nested != null)
            {
                throw new ArgumentException($"hub.domainId: '{domain.Name}' and '{nested.Name}' are nested hubs; "
                    + "remove the hub flag from one of them first");
            }
            var spawn = hub.SpawnReference?.Location;
            if (spawn == null)
            {
                throw new ArgumentException("hub.spawnReference: a hub needs a spawn location");
            }
            if (!string.Equals(spawn.World?.Trim(), domain.WorldName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"hub.spawnReference: the spawn is in world '{spawn.World}', but '{domain.Name}' "
                    + $"is in world '{domain.WorldName}'");
            }
            return normalized;
        }

        public async Task<HubSettingsDto> DescribeAsync(HubSettingsDto hub)
        {
            hub ??= new HubSettingsDto();
            hub.Rules ??= new HubRulesDto();
            if (hub.DomainId is int domainId)
            {
                var domain = await _context.Domains.AsNoTracking().FirstOrDefaultAsync(d => d.Id == domainId);
                if (domain == null)
                {
                    // The selected hub was deleted: no hub, rather than a hub nobody can enter (the game server would
                    // refuse every join while its region is missing).
                    hub.DomainId = null;
                    return hub;
                }
                hub.DomainName = domain.Name;
                hub.DomainType = domain.GetType().Name;
                hub.WorldName = domain.WorldName;
                hub.WgRegionId = domain.WgRegionId;
            }
            return hub;
        }

        public async Task<List<HubExemptionEligibilityDto>> ExemptionEligibilityAsync()
        {
            var groups = await _context.PermissionGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
            var result = new List<HubExemptionEligibilityDto>();
            foreach (var group in groups)
            {
                var staff = await _permissions.CheckGroupAsync(group.Id, StaffModeNode);
                var owner = await _permissions.CheckGroupAsync(group.Id, OwnerModeNode);
                result.Add(new HubExemptionEligibilityDto
                {
                    PermissionGroupId = group.Id,
                    GroupName = group.Name,
                    CanExemptStaffMode = staff?.Allowed == true,
                    StaffModeSource = staff?.Allowed == true ? staff.SourceGroupName : null,
                    CanExemptOwnerMode = owner?.Allowed == true,
                    OwnerModeSource = owner?.Allowed == true ? owner.SourceGroupName : null
                });
            }
            return result;
        }

        public async Task ValidateExemptionsAsync(IEnumerable<PermissionGroupGameSettingsDto> overrides)
        {
            foreach (var o in overrides ?? Enumerable.Empty<PermissionGroupGameSettingsDto>())
            {
                if (o == null)
                {
                    continue;
                }
                if (o.ExemptInStaffMode == true && (await _permissions.CheckGroupAsync(o.PermissionGroupId, StaffModeNode))?.Allowed != true)
                {
                    throw new ArgumentException($"groupOverrides[{o.PermissionGroupId}].exemptInStaffMode: the group does not have {StaffModeNode}");
                }
                if (o.ExemptInOwnerMode == true && (await _permissions.CheckGroupAsync(o.PermissionGroupId, OwnerModeNode))?.Allowed != true)
                {
                    throw new ArgumentException($"groupOverrides[{o.PermissionGroupId}].exemptInOwnerMode: the group does not have {OwnerModeNode}");
                }
                if (o.OutageAction != null && !OutageActions.Contains(o.OutageAction.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"groupOverrides[{o.PermissionGroupId}].outageAction must be one of {string.Join(", ", OutageActions)}");
                }
                if (o.OutageDelaySeconds is int delay && (delay < 0 || delay > MaxOutageDelaySeconds))
                {
                    throw new ArgumentException($"groupOverrides[{o.PermissionGroupId}].outageDelaySeconds must be between 0 and {MaxOutageDelaySeconds}");
                }
            }
        }

        /// <summary>A flagged domain that would be nested with this one: an ancestor or a descendant.</summary>
        private async Task<Domain?> RelatedHubAsync(Domain domain)
        {
            var related = new List<int>();
            switch (domain)
            {
                case Structure structure:
                    related.Add(structure.DistrictId);
                    var townId = await _context.Districts.Where(d => d.Id == structure.DistrictId).Select(d => (int?)d.TownId).FirstOrDefaultAsync();
                    if (townId is int t) related.Add(t);
                    break;
                case District district:
                    related.Add(district.TownId);
                    related.AddRange(await _context.Structures.Where(s => s.DistrictId == district.Id).Select(s => s.Id).ToListAsync());
                    break;
                case Town town:
                    var districtIds = await _context.Districts.Where(d => d.TownId == town.Id).Select(d => d.Id).ToListAsync();
                    related.AddRange(districtIds);
                    related.AddRange(await _context.Structures.Where(s => districtIds.Contains(s.DistrictId)).Select(s => s.Id).ToListAsync());
                    break;
            }
            if (related.Count == 0)
            {
                return null;
            }
            return await _context.Domains.AsNoTracking().Where(d => related.Contains(d.Id) && d.IsHub).OrderBy(d => d.Id).FirstOrDefaultAsync();
        }

        private async Task<HubSettingsDto?> CurrentHubAsync()
        {
            var json = await _context.GameSettings.AsNoTracking().Select(g => g.HubSettingsJson).FirstOrDefaultAsync();
            return GameSettingsJson.Deserialize<HubSettingsDto>(json);
        }

        private static HubDomainDto ToDto(Domain domain, int? selected) => new()
        {
            Id = domain.Id,
            Name = domain.Name,
            DomainType = domain.GetType().Name,
            WorldName = domain.WorldName,
            WgRegionId = domain.WgRegionId,
            IsHub = domain.IsHub,
            Selected = selected == domain.Id
        };
    }
}

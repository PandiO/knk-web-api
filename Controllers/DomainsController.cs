using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.world or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("Domain")]
    public class DomainsController : ControllerBase
    {
        private readonly IDomainService _service;
        private readonly IMapper _mapper;
        private readonly IDomainWorldResolver _worlds;
        private readonly IDomainWorldBackfill _worldBackfill;
        private readonly ILandmarkService _landmarks;
        private readonly IPermissionResolutionService _permissions;

        public DomainsController(IDomainService service, IMapper mapper, IDomainWorldResolver worlds, IDomainWorldBackfill worldBackfill,
            ILandmarkService landmarks, IPermissionResolutionService permissions)
        {
            _service = service;
            _mapper = mapper;
            _worlds = worlds;
            _worldBackfill = worldBackfill;
            _landmarks = landmarks;
            _permissions = permissions;
        }

        /// <summary>LD11: the answer to a domain write without a region (every type but a landmark needs one).</summary>
        public const string RegionRequired = "WgRegionId is required.";

        /// <summary>
        /// Landmark-domain LD19 (PandiO/knk-workspace#983): a write on a landmark through this controller also needs the
        /// landmark write node, <c>knk.admin.world.landmark</c> (the game server's key passes, as for api/landmarks).
        /// Null when the caller may; otherwise the 403.
        /// </summary>
        private async Task<IActionResult?> LandmarkWriteDeniedAsync()
        {
            var caller = HttpContext.GetKnkCaller();
            if (caller.IsPluginService) return null;
            if (caller.WebUserId is int userId && (await _permissions.CheckAsync(userId, StaffPermissions.LandmarkManage))?.Allowed == true)
                return null;
            return PluginServiceAuth.Forbidden($"This domain is a landmark: changing it requires the {StaffPermissions.LandmarkManage} permission.");
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:int}", Name = nameof(GetDomainById))]
        public async Task<IActionResult> GetDomainById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [RequireDashboardChange("Domain")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Domain domain)
        {
            if (domain == null) return BadRequest();
            // LD11: Domain.WgRegionId is nullable for landmarks only; this body is a plain domain, which still needs one
            // (the model binding refused a missing region while the property was not nullable).
            if (string.IsNullOrWhiteSpace(domain.WgRegionId)) return BadRequest(RegionRequired);
            try
            {
                var created = await _service.CreateAsync(domain);
                return CreatedAtRoute(nameof(GetDomainById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [RequireDashboardChange("Domain")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Domain domain)
        {
            if (domain == null) return BadRequest();
            try
            {
                // LD19: a landmark needs the landmark rights and is saved through the landmark rules. The answer is the
                // landmark DTO: the tracked entity now has its sub-regions loaded, whose Domain back-reference would make
                // the entity's JSON a cycle.
                if (await _service.GetByIdAsync(id) is Landmark)
                {
                    if (await LandmarkWriteDeniedAsync() is IActionResult denied) return denied;
                    return Ok(await _landmarks.UpdateFromDomainAsync(id, domain));
                }

                if (string.IsNullOrWhiteSpace(domain.WgRegionId)) return BadRequest(RegionRequired);
                await _service.UpdateAsync(id, domain);
                // Return the updated entity instead of 204
                var updated = await _service.GetByIdAsync(id);
                return Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280).
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                // LD19: a landmark needs the landmark rights and is deleted through LandmarkService.
                if (await _service.GetByIdAsync(id) is Landmark)
                {
                    if (await LandmarkWriteDeniedAsync() is IActionResult denied) return denied;
                    await _landmarks.DeleteAsync(id);
                    return NoContent();
                }

                await _service.DeleteAsync(id);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Every domain's AllowEntry/AllowExit by WorldGuard region, for the game server's flag sync
        /// (KNG-56). Read-only, small (one row per domain region).
        /// </summary>
        [HttpGet("access-rules")]
        [DashboardReads(DashboardArea.World)]
        public async Task<ActionResult<IReadOnlyList<DomainAccessRuleDto>>> GetAccessRules()
        {
            return Ok(await _service.GetAccessRulesAsync());
        }

        /// <param name="regionName">The WorldGuard region id.</param>
        /// <param name="world">KNG-111: the world the region is in. Without it the lookup is world-blind.</param>
        [HttpGet("by-region/{regionName}")]
        [DashboardReads(DashboardArea.World)]
        public async Task<ActionResult<DomainRegionDecisionDto>> GetByRegionName(string regionName, [FromQuery] string? world = null)
        {
            if (string.IsNullOrWhiteSpace(regionName)) return BadRequest("regionName is required.");
            var dto = await _service.GetByWgRegionNameAsync(regionName, world);
            if (dto == null) return NotFound();
            return Ok(dto);
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<DomainListDto>>> Search([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        [HttpPost("search-region-decisions")]
        [DashboardReads(DashboardArea.World)]
        public async Task<ActionResult<Dictionary<int, DomainRegionDecisionDto>>> SearchDomainRegionDecision([FromBody] DomainRegionQueryDto queryDto)
        {
            if (queryDto.WgRegionIds == null) return BadRequest("WgRegionIds is required.");

            var result = await _service.SearchDomainRegionDecisionAsync(queryDto);
            return Ok(result);
        }

        /// <summary>
        /// KNG-111: which world a domain being created or edited is in, from the same sources the save uses (region world
        /// task, Location, parent). <c>needsWorld</c> tells the form to ask; <c>error</c> names a conflict the save would
        /// reject.
        /// </summary>
        [HttpPost("world/resolve")]
        [DashboardReads(DashboardArea.World)]
        public async Task<ActionResult<DomainWorldResolutionDto>> ResolveWorld([FromBody] DomainWorldResolveRequestDto request)
        {
            if (request == null) return BadRequest("A request body is required.");
            var resolution = await _worlds.TryResolveAsync(new DomainWorldRequest
            {
                DomainId = request.Id,
                RequestedWorld = request.WorldName,
                WgRegionId = request.WgRegionId,
                LocationWorld = request.LocationWorld,
                LocationId = request.LocationId,
                ParentDomainId = request.TownId is > 0 ? request.TownId : request.DistrictId
            });
            return Ok(new DomainWorldResolutionDto
            {
                WorldName = resolution.WorldName,
                Source = resolution.Source,
                NeedsWorld = resolution.NeedsWorld,
                Error = resolution.Error,
                ErrorCode = resolution.ErrorCode
            });
        }

        /// <summary>KNG-111: domains that still have no world (created before worlds were stored).</summary>
        [HttpGet("world/missing")]
        [DashboardReads(DashboardArea.World)]
        public async Task<ActionResult<List<DomainWorldMissingDto>>> GetDomainsWithoutWorld()
        {
            return Ok(await _worldBackfill.ListMissingAsync());
        }

        /// <summary>
        /// KNG-111: the game server reports which world(s) hold each region; domains without a world get one where the
        /// report, their Location and their parent agree. Returns the domains still left for an admin.
        /// </summary>
        [HttpPost("world/backfill")]
        [RequireServiceOrPermission(StaffPermissions.ServerConfig)]
        public async Task<ActionResult<DomainWorldBackfillResultDto>> BackfillWorlds([FromBody] DomainWorldBackfillRequestDto request)
        {
            return Ok(await _worldBackfill.ApplyAsync(request ?? new DomainWorldBackfillRequestDto()));
        }
    }
}

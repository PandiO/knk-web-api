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

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DomainsController : ControllerBase
    {
        private readonly IDomainService _service;
        private readonly IMapper _mapper;
        private readonly IDomainWorldResolver _worlds;
        private readonly IDomainWorldBackfill _worldBackfill;

        public DomainsController(IDomainService service, IMapper mapper, IDomainWorldResolver worlds, IDomainWorldBackfill worldBackfill)
        {
            _service = service;
            _mapper = mapper;
            _worlds = worlds;
            _worldBackfill = worldBackfill;
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

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Domain domain)
        {
            if (domain == null) return BadRequest();
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

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Domain domain)
        {
            if (domain == null) return BadRequest();
            try
            {
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

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
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
            catch (DbUpdateException ex)
            {
                // Surfaced now that ItemBlueprintOrigin.DomainId (Restrict) is the first FK to ever
                // reference Domains - matches the DbUpdateException handling every other controller with
                // an incoming FK already has (CategoriesController, GradesController, etc.).
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }

        /// <summary>
        /// Every domain's AllowEntry/AllowExit by WorldGuard region, for the game server's flag sync
        /// (KNG-56). Read-only, small (one row per domain region).
        /// </summary>
        [HttpGet("access-rules")]
        public async Task<ActionResult<IReadOnlyList<DomainAccessRuleDto>>> GetAccessRules()
        {
            return Ok(await _service.GetAccessRulesAsync());
        }

        /// <param name="regionName">The WorldGuard region id.</param>
        /// <param name="world">KNG-111: the world the region is in. Without it the lookup is world-blind.</param>
        [HttpGet("by-region/{regionName}")]
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

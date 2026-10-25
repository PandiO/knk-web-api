using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Arenas and their areas (battlegrounds, spectator areas, the lobby) with their regions (arena duels KNG-210,
    /// docs/specs/arena-duels/DESIGN.md §4.1–4.2, rev. 6).
    /// Reads are open to the plugin and logged-in users (the default-deny filter); writes need knk.admin.world or the
    /// game server. POST search is a read and only needs a caller.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ArenasController : ControllerBase
    {
        private readonly IArenaService _service;

        public ArenasController(IArenaService service)
        {
            _service = service;
        }

        // ===================================================================== Arenas

        /// <summary>Every arena with its settings, areas (with their regions), sub-regions and eligible kits (the plugin's
        /// cache-first gateway loads this).</summary>
        [HttpGet]
        public async Task<ActionResult<List<ArenaDto>>> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = nameof(GetArenaById))]
        public async Task<ActionResult<ArenaDto>> GetArenaById(int id)
        {
            var arena = await _service.GetByIdAsync(id);
            return arena == null ? NotFound() : Ok(arena);
        }

        [HttpGet("by-key/{key}")]
        public async Task<ActionResult<ArenaDto>> GetByKey(string key)
        {
            var arena = await _service.GetByKeyAsync(key);
            return arena == null ? NotFound() : Ok(arena);
        }

        [HttpPost("search")]
        [RequireServiceOrLoggedIn]
        public async Task<ActionResult<PagedResultDto<ArenaListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        [HttpPost]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> Create([FromBody] ArenaDto dto) =>
            Run(async () =>
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute(nameof(GetArenaById), new { id = created.Id }, created);
            }, dto != null);

        [HttpPut("{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> Update(int id, [FromBody] ArenaDto dto) =>
            Run(async () =>
            {
                await _service.UpdateAsync(id, dto);
                return NoContent();
            }, dto != null);

        [HttpDelete("{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> Delete(int id) =>
            Run(async () =>
            {
                await _service.DeleteAsync(id);
                return NoContent();
            });

        // ===================================================================== Areas (rev. 6)

        /// <summary>The arena's battlegrounds, spectator areas and lobby, with their regions.</summary>
        [HttpGet("{arenaId:int}/areas")]
        public async Task<ActionResult<List<ArenaAreaDto>>> GetAreasOfArena(int arenaId) =>
            Ok(await _service.GetAreasAsync(arenaId));

        [HttpGet("areas")]
        public async Task<ActionResult<List<ArenaAreaDto>>> GetAreas([FromQuery] int? arenaId) =>
            Ok(await _service.GetAreasAsync(arenaId));

        [HttpGet("areas/{id:int}", Name = nameof(GetAreaById))]
        public async Task<ActionResult<ArenaAreaDto>> GetAreaById(int id)
        {
            var area = await _service.GetAreaAsync(id);
            return area == null ? NotFound() : Ok(area);
        }

        [HttpPost("areas/search")]
        [RequireServiceOrLoggedIn]
        public async Task<ActionResult<PagedResultDto<ArenaAreaDto>>> SearchAreas([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAreasAsync(query));

        /// <summary>Creates an area of <paramref name="arenaId"/> (the body's arenaId is ignored).</summary>
        [HttpPost("{arenaId:int}/areas")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> CreateAreaOfArena(int arenaId, [FromBody] ArenaAreaDto dto) =>
            Run(async () =>
            {
                dto.ArenaId = arenaId;
                var created = await _service.CreateAreaAsync(dto);
                return CreatedAtRoute(nameof(GetAreaById), new { id = created.Id }, created);
            }, dto != null);

        /// <summary>Creates an area of the body's arenaId (the FormWizard's owned-list create).</summary>
        [HttpPost("areas")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> CreateArea([FromBody] ArenaAreaDto dto) =>
            Run(async () =>
            {
                var created = await _service.CreateAreaAsync(dto);
                return CreatedAtRoute(nameof(GetAreaById), new { id = created.Id }, created);
            }, dto != null);

        [HttpPut("areas/{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> UpdateArea(int id, [FromBody] ArenaAreaDto dto) =>
            Run(async () => Ok(await _service.UpdateAreaAsync(id, dto)), dto != null);

        [HttpDelete("areas/{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> DeleteArea(int id) =>
            Run(async () =>
            {
                await _service.DeleteAreaAsync(id);
                return NoContent();
            });

        // ===================================================================== Area regions (rev. 6)

        [HttpGet("areas/{areaId:int}/regions")]
        public async Task<ActionResult<List<ArenaAreaRegionDto>>> GetRegionsOfArea(int areaId) =>
            Ok(await _service.GetAreaRegionsAsync(areaId));

        [HttpGet("areas/regions")]
        public async Task<ActionResult<List<ArenaAreaRegionDto>>> GetAreaRegions([FromQuery] int? arenaAreaId) =>
            Ok(await _service.GetAreaRegionsAsync(arenaAreaId));

        [HttpGet("areas/regions/{id:int}", Name = nameof(GetAreaRegionById))]
        public async Task<ActionResult<ArenaAreaRegionDto>> GetAreaRegionById(int id)
        {
            var region = await _service.GetAreaRegionAsync(id);
            return region == null ? NotFound() : Ok(region);
        }

        /// <summary>The area region using a WorldGuard region id (the game server's orphan check before it deletes an
        /// <c>arena_&lt;a&gt;_&lt;kind&gt;_&lt;n&gt;</c> region no arena lists); 404 when none does.</summary>
        [HttpGet("areas/by-region/{regionId}")]
        public async Task<ActionResult<ArenaAreaRegionDto>> GetAreaRegionByRegionId(string regionId, [FromQuery] string? world)
        {
            var region = await _service.FindAreaRegionAsync(regionId, world);
            return region == null ? NotFound() : Ok(region);
        }

        [HttpPost("areas/regions/search")]
        [RequireServiceOrLoggedIn]
        public async Task<ActionResult<PagedResultDto<ArenaAreaRegionDto>>> SearchAreaRegions([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAreaRegionsAsync(query));

        /// <summary>Adds a region to <paramref name="areaId"/> and renames it to its final name.</summary>
        [HttpPost("areas/{areaId:int}/regions")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> CreateRegionOfArea(int areaId, [FromBody] ArenaAreaRegionDto dto) =>
            Run(async () =>
            {
                dto.ArenaAreaId = areaId;
                var created = await _service.CreateAreaRegionAsync(dto);
                return CreatedAtRoute(nameof(GetAreaRegionById), new { id = created.Id }, created);
            }, dto != null);

        /// <summary>Adds a region to the body's arenaAreaId (the FormWizard's owned-list create).</summary>
        [HttpPost("areas/regions")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> CreateAreaRegion([FromBody] ArenaAreaRegionDto dto) =>
            Run(async () =>
            {
                var created = await _service.CreateAreaRegionAsync(dto);
                return CreatedAtRoute(nameof(GetAreaRegionById), new { id = created.Id }, created);
            }, dto != null);

        [HttpPut("areas/regions/{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> UpdateAreaRegion(int id, [FromBody] ArenaAreaRegionDto dto) =>
            Run(async () => Ok(await _service.UpdateAreaRegionAsync(id, dto)), dto != null);

        [HttpDelete("areas/regions/{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> DeleteAreaRegion(int id) =>
            Run(async () =>
            {
                await _service.DeleteAreaRegionAsync(id);
                return NoContent();
            });

        // ===================================================================== Helpers

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action, bool hasBody = true)
        {
            if (!hasBody) return BadRequest("A request body is required.");
            try
            {
                return await action();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}

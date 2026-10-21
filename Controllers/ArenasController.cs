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
    /// Arenas, battlegrounds and spectator areas (arena duels KNG-210, docs/specs/arena-duels/DESIGN.md §4.1–4.2).
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

        /// <summary>Every arena with its settings, battlegrounds, spectator areas and eligible kits (the plugin's
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

        // ===================================================================== Battlegrounds

        [HttpGet("{arenaId:int}/battlegrounds")]
        public async Task<ActionResult<List<ArenaBattlegroundDto>>> GetBattlegroundsOfArena(int arenaId) =>
            Ok(await _service.GetBattlegroundsAsync(arenaId));

        [HttpGet("battlegrounds")]
        public async Task<ActionResult<List<ArenaBattlegroundDto>>> GetBattlegrounds([FromQuery] int? arenaId) =>
            Ok(await _service.GetBattlegroundsAsync(arenaId));

        [HttpGet("battlegrounds/{id:int}", Name = nameof(GetBattlegroundById))]
        public async Task<ActionResult<ArenaBattlegroundDto>> GetBattlegroundById(int id)
        {
            var battleground = await _service.GetBattlegroundAsync(id);
            return battleground == null ? NotFound() : Ok(battleground);
        }

        [HttpPost("battlegrounds/search")]
        [RequireServiceOrLoggedIn]
        public async Task<ActionResult<PagedResultDto<ArenaBattlegroundDto>>> SearchBattlegrounds([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchBattlegroundsAsync(query));

        [HttpPost("battlegrounds")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> CreateBattleground([FromBody] ArenaBattlegroundDto dto) =>
            Run(async () =>
            {
                var created = await _service.CreateBattlegroundAsync(dto);
                return CreatedAtRoute(nameof(GetBattlegroundById), new { id = created.Id }, created);
            }, dto != null);

        [HttpPut("battlegrounds/{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> UpdateBattleground(int id, [FromBody] ArenaBattlegroundDto dto) =>
            Run(async () =>
            {
                await _service.UpdateBattlegroundAsync(id, dto);
                return NoContent();
            }, dto != null);

        [HttpDelete("battlegrounds/{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> DeleteBattleground(int id) =>
            Run(async () =>
            {
                await _service.DeleteBattlegroundAsync(id);
                return NoContent();
            });

        // ===================================================================== Spectator areas

        [HttpGet("{arenaId:int}/spectator-areas")]
        public async Task<ActionResult<List<ArenaSpectatorAreaDto>>> GetSpectatorAreasOfArena(int arenaId) =>
            Ok(await _service.GetSpectatorAreasAsync(arenaId));

        [HttpGet("spectator-areas")]
        public async Task<ActionResult<List<ArenaSpectatorAreaDto>>> GetSpectatorAreas([FromQuery] int? arenaId) =>
            Ok(await _service.GetSpectatorAreasAsync(arenaId));

        [HttpGet("spectator-areas/{id:int}", Name = nameof(GetSpectatorAreaById))]
        public async Task<ActionResult<ArenaSpectatorAreaDto>> GetSpectatorAreaById(int id)
        {
            var area = await _service.GetSpectatorAreaAsync(id);
            return area == null ? NotFound() : Ok(area);
        }

        [HttpPost("spectator-areas/search")]
        [RequireServiceOrLoggedIn]
        public async Task<ActionResult<PagedResultDto<ArenaSpectatorAreaDto>>> SearchSpectatorAreas([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchSpectatorAreasAsync(query));

        [HttpPost("spectator-areas")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> CreateSpectatorArea([FromBody] ArenaSpectatorAreaDto dto) =>
            Run(async () =>
            {
                var created = await _service.CreateSpectatorAreaAsync(dto);
                return CreatedAtRoute(nameof(GetSpectatorAreaById), new { id = created.Id }, created);
            }, dto != null);

        [HttpPut("spectator-areas/{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> UpdateSpectatorArea(int id, [FromBody] ArenaSpectatorAreaDto dto) =>
            Run(async () =>
            {
                await _service.UpdateSpectatorAreaAsync(id, dto);
                return NoContent();
            }, dto != null);

        [HttpDelete("spectator-areas/{id:int}")]
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        public Task<IActionResult> DeleteSpectatorArea(int id) =>
            Run(async () =>
            {
                await _service.DeleteSpectatorAreaAsync(id);
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

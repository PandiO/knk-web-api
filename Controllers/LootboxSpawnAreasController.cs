using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Lootbox spawn areas (docs/specs/lootboxes/DESIGN.md §3.3, D17): FormWizard CRUD for the web app's admins. A
    /// duplicate name is 409 <c>NameTaken</c>. <c>in-game</c> and <c>{id}/in-game-delete</c> serve knk-plugin's
    /// <c>/knk lootbox area create|delete</c> (game server only; the staff member is the X-Acting-User-Id header).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LootboxSpawnAreasController : ControllerBase
    {
        private readonly ILootboxSpawnAreaService _service;

        public LootboxSpawnAreasController(ILootboxSpawnAreaService service)
        {
            _service = service;
        }

        [HttpGet]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}", Name = "GetLootboxSpawnAreaById")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> Create([FromBody] LootboxSpawnAreaDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetLootboxSpawnAreaById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (LootboxConflictException ex)
            {
                return Conflict(new { code = ex.Code, message = ex.Message });
            }
        }

        [HttpPut("{id:int}")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> Update(int id, [FromBody] LootboxSpawnAreaDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, dto);
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
            catch (LootboxConflictException ex)
            {
                return Conflict(new { code = ex.Code, message = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
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
            catch (LootboxConflictException ex)
            {
                return Conflict(new { code = ex.Code, message = ex.Message });
            }
            catch (DbUpdateException ex)
            {
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }

        /// <summary>201 area; 409 <c>{code: NameTaken|RegionInUse}</c>; 400 invalid name.</summary>
        [HttpPost("in-game")]
        [RequirePluginService]
        public async Task<IActionResult> CreateInGame([FromBody] LootboxInGameAreaCreateDto dto)
        {
            if (dto == null) return BadRequest();
            return await LootboxResults.Run(this, async () =>
            {
                var created = await _service.CreateInGameAsync(dto, HttpContext.GetKnkCaller().ActorUserId);
                return CreatedAtRoute("GetLootboxSpawnAreaById", new { id = created.Id }, created);
            });
        }

        /// <summary>200 with the region id and the ids of the boxes that were removed.</summary>
        [HttpPost("{id:int}/in-game-delete")]
        [RequirePluginService]
        public async Task<IActionResult> DeleteInGame(int id)
        {
            return await LootboxResults.Run(this, async () =>
                Ok(await _service.DeleteInGameAsync(id, HttpContext.GetKnkCaller().ActorUserId)));
        }

        [HttpPost("search")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<ActionResult<PagedResultDto<LootboxSpawnAreaDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchAsync(query));
        }
    }
}

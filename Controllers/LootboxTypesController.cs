using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Lootbox types (docs/specs/lootboxes/DESIGN.md §3.3): FormWizard CRUD for the web app's admins, plus the odds
    /// preview, which knk-plugin's player command <c>/lootbox odds</c> reads with its service key and the web app's
    /// admins with the lootbox node (KNG-22).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LootboxTypesController : ControllerBase
    {
        private readonly ILootboxTypeService _service;

        public LootboxTypesController(ILootboxTypeService service)
        {
            _service = service;
        }

        [HttpGet]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}", Name = "GetLootboxTypeById")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> Create([FromBody] LootboxTypeDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetLootboxTypeById", new { id = created.Id }, created);
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
        public async Task<IActionResult> Update(int id, [FromBody] LootboxTypeDto dto)
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

        [HttpPost("search")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<ActionResult<PagedResultDto<LootboxTypeDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchAsync(query));
        }

        /// <summary>
        /// Odds of one box grade (default: the type's highest): box-grade distribution, specials, per-grade and per-item
        /// percentages and each enchant roll's hit chance and capped level range - the same rules the claim rolls with.
        /// </summary>
        [HttpGet("{id:int}/odds")]
        [RequireServiceOrPermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetOdds(int id, [FromQuery] int? boxStars)
        {
            try
            {
                var odds = await _service.GetOddsAsync(id, boxStars);
                if (odds == null) return NotFound();
                return Ok(odds);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}

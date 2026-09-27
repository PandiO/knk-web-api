using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Lootbox special (jackpot) entries (docs/specs/lootboxes/DESIGN.md §3.3, §3.5): FormWizard CRUD for the web
    /// app's admins.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LootboxSpecialEntriesController : ControllerBase
    {
        private readonly ILootboxSpecialEntryService _service;

        public LootboxSpecialEntriesController(ILootboxSpecialEntryService service)
        {
            _service = service;
        }

        [HttpGet]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}", Name = "GetLootboxSpecialEntryById")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> Create([FromBody] LootboxSpecialEntryDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetLootboxSpecialEntryById", new { id = created.Id }, created);
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
        public async Task<IActionResult> Update(int id, [FromBody] LootboxSpecialEntryDto dto)
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
        public async Task<ActionResult<PagedResultDto<LootboxSpecialEntryDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchAsync(query));
        }
    }
}

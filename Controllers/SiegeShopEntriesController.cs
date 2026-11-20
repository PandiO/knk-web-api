using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege shop (knk-workspace docs/specs/siege-shop/DESIGN.md § 6.1.2, § 6.2.1; PandiO/knk-workspace#867): shop entry
    // CRUD, the modes' default lists and scenario-only entries (siegeScenarioId set). An entry whose target doesn't
    // match its kind is a 400 (A14). Deleting an entry also removes the scenarios' overrides of it.
    [ApiController]
    [Route("api/[controller]")]
    [DashboardReads("SiegeShopEntry")]
    public class SiegeShopEntriesController : ControllerBase
    {
        private readonly ISiegeShopService _service;

        public SiegeShopEntriesController(ISiegeShopService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetEntriesAsync());

        [HttpGet("{id:int}", Name = "GetSiegeShopEntryById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetEntryByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("SiegeShopEntry")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SiegeShopEntryUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateEntryAsync(dto);
                return CreatedAtRoute("GetSiegeShopEntryById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }

        [RequireDashboardChange("SiegeShopEntry")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeShopEntryUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateEntryAsync(id, dto);
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }

        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280).
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _service.DeleteEntryAsync(id);
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
            catch (InvalidOperationException ex) when (!DeleteConstraintExceptionFilter.IsConstraintFailure(ex))
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }

        [RequireServiceOrLoggedIn] // a read sent as POST, like the GET list
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<SiegeShopEntryListDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchEntriesAsync(query));
        }
    }
}

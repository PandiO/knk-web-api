using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege weapons phase 1 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 9.1; PandiO/knk-workspace#782):
    // weapon template CRUD; the allowed ammo types travel in the payload as an M2M set (null keeps it, [] clears it).
    [ApiController]
    [Route("api/[controller]")]
    [DashboardReads("SiegeWeaponTemplate")]
    public class SiegeWeaponTemplatesController : ControllerBase
    {
        private readonly ISiegeWeaponService _service;

        public SiegeWeaponTemplatesController(ISiegeWeaponService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetTemplatesAsync());

        [HttpGet("{id:int}", Name = "GetSiegeWeaponTemplateById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetTemplateByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("SiegeWeaponTemplate")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SiegeWeaponTemplateUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateTemplateAsync(dto);
                return CreatedAtRoute("GetSiegeWeaponTemplateById", new { id = created.Id }, created);
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

        [RequireDashboardChange("SiegeWeaponTemplate")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeWeaponTemplateUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateTemplateAsync(id, dto);
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
                await _service.DeleteTemplateAsync(id);
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
        public async Task<ActionResult<PagedResultDto<SiegeWeaponTemplateListDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchTemplatesAsync(query));
        }
    }
}

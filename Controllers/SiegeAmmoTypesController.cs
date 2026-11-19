using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege weapons phase 1 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 9.2; PandiO/knk-workspace#782):
    // ammo type CRUD. An ammo type a template still allows can't be deleted (409).
    [ApiController]
    [Route("api/[controller]")]
    [DashboardReads("SiegeAmmoType")]
    public class SiegeAmmoTypesController : ControllerBase
    {
        private readonly ISiegeWeaponService _service;

        public SiegeAmmoTypesController(ISiegeWeaponService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAmmoTypesAsync());

        [HttpGet("{id:int}", Name = "GetSiegeAmmoTypeById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetAmmoTypeByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("SiegeAmmoType")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SiegeAmmoTypeUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAmmoTypeAsync(dto);
                return CreatedAtRoute("GetSiegeAmmoTypeById", new { id = created.Id }, created);
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

        [RequireDashboardChange("SiegeAmmoType")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeAmmoTypeUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateAmmoTypeAsync(id, dto);
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
                await _service.DeleteAmmoTypeAsync(id);
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
        public async Task<ActionResult<PagedResultDto<SiegeAmmoTypeListDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchAmmoTypesAsync(query));
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege weapons phase 1 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 7.1, § 9.6; W13, W32;
    // PandiO/knk-workspace#782): a player's permanent weapon type unlocks. Staff with the siege node grant and revoke
    // them in the web app; the plugin key may write them too, for the coin purchase a later link builds. Reads are the
    // dashboard's (siege node or plugin key), including GET by-user/{userId} for the plugin's "may buy" check.
    [ApiController]
    [Route("api/[controller]")]
    [DashboardReads("UserSiegeWeaponUnlock")]
    public class UserSiegeWeaponUnlocksController : ControllerBase
    {
        private readonly ISiegeWeaponService _service;

        public UserSiegeWeaponUnlocksController(ISiegeWeaponService service)
        {
            _service = service;
        }

        [HttpGet("by-user/{userId:int}")]
        public async Task<IActionResult> GetByUser(int userId)
        {
            try
            {
                return Ok(await _service.GetUnlocksForUserAsync(userId));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpGet("{id:int}", Name = "GetUserSiegeWeaponUnlockById")]
        public async Task<IActionResult> GetById(int id)
        {
            var unlock = await _service.GetUnlockByIdAsync(id);
            return unlock == null ? NotFound() : Ok(unlock);
        }

        [RequireDashboardChange("UserSiegeWeaponUnlock")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserSiegeWeaponUnlockUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateUnlockAsync(dto);
                return CreatedAtRoute("GetUserSiegeWeaponUnlockById", new { id = created.Id }, created);
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

        [RequireDashboardChange("UserSiegeWeaponUnlock")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UserSiegeWeaponUnlockUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateUnlockAsync(id, dto);
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
                await _service.DeleteUnlockAsync(id);
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

        [RequireServiceOrLoggedIn] // a read sent as POST, like the GET list
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<UserSiegeWeaponUnlockReadDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchUnlocksAsync(query));
        }
    }
}

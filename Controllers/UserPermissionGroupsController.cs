using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// User ↔ PermissionGroup memberships — the authoring path for assigning groups, including
    /// premium tiers with an optional expiry (docs/specs/user-features/IMPLEMENTATION_PLAN.md §5).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UserPermissionGroupsController : ControllerBase
    {
        private readonly IUserPermissionGroupService _service;

        public UserPermissionGroupsController(IUserPermissionGroupService service)
        {
            _service = service;
        }

        /// <summary>List memberships by user or by group (exactly one of the two).</summary>
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int? userId, [FromQuery] int? permissionGroupId)
        {
            if (userId.HasValue == permissionGroupId.HasValue)
                return BadRequest("Specify exactly one of userId or permissionGroupId.");

            var items = userId.HasValue
                ? await _service.GetByUserAsync(userId.Value)
                : await _service.GetByGroupAsync(permissionGroupId!.Value);
            return Ok(items);
        }

        /// <summary>
        /// Assign a user to a group, or change the expiry of an existing membership. Omit
        /// expiresAt for a permanent membership.
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> Upsert([FromBody] UpsertUserPermissionGroupDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await _service.UpsertAsync(dto));
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

        [HttpDelete("{userId:int}/{permissionGroupId:int}")]
        public async Task<IActionResult> Delete(int userId, int permissionGroupId)
        {
            try
            {
                await _service.DeleteAsync(userId, permissionGroupId);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>The user's current premium tier, or 204 if they hold none.</summary>
        [HttpGet("premium-tier/{userId:int}")]
        public async Task<IActionResult> GetPremiumTier(int userId)
        {
            var tier = await _service.GetActivePremiumTierAsync(userId);
            if (tier == null) return NoContent();
            return Ok(tier);
        }
    }
}

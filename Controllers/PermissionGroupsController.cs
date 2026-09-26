using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Attributes;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PermissionGroupsController : ControllerBase
    {
        private readonly IPermissionGroupService _service;

        public PermissionGroupsController(IPermissionGroupService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:int}", Name = "GetPermissionGroupById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        // Group definitions carry nodes and reward multipliers, so editing them is as strong as
        // granting nodes directly (knk.admin.user.perm) — no longer anonymous (KNG-22).
        [RequireServiceOrPermission(StaffPermissions.UserPermissions)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PermissionGroupDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetPermissionGroupById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [RequireServiceOrPermission(StaffPermissions.UserPermissions)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PermissionGroupDto dto)
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
        }

        [RequireServiceOrPermission(StaffPermissions.UserPermissions)]
        [HttpDelete("{id:int}")]
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "PermissionGroupHasChildren", message = ex.Message });
            }
            catch (DbUpdateException ex)
            {
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<PermissionGroupListDto>>> Search([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        /// <summary>
        /// "Premium expiring soon" moderation view (docs/specs/user-management/IMPLEMENTATION_PLAN.md
        /// Phase 3) — memberships in this group expiring within the next withinDays days.
        /// </summary>
        [HttpGet("{id:int}/expiring-memberships")]
        public async Task<ActionResult<IEnumerable<ExpiringMembershipDto>>> GetExpiringMemberships(int id, [FromQuery] int withinDays = 7)
        {
            try
            {
                var result = await _service.GetExpiringMembershipsAsync(id, withinDays);
                return Ok(result);
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
    }
}

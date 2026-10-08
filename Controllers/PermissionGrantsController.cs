using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Services;
using knkwebapi_v2.Attributes;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PermissionGrantsController : ControllerBase
    {
        private readonly IPermissionGrantService _service;
        private readonly IPermissionEscalationGuard _escalationGuard;

        public PermissionGrantsController(IPermissionGrantService service, IPermissionEscalationGuard escalationGuard)
        {
            _service = service;
            _escalationGuard = escalationGuard;
        }

        // Closed-alpha WP3: a web user may only grant, deny or revoke a node they hold, never on
        // themselves (unless they hold *). The plugin does its own in-game checks.
        private Task<IActionResult?> RefuseIfEscalationAsync(int holderId, string? node) =>
            EscalationGuardHttp.RefuseIfEscalationAsync(HttpContext, actor => _escalationGuard.CanGrantNodeAsync(actor, holderId, node ?? string.Empty));

        [RequireServiceOrPermission(StaffPermissions.ManageUsers)]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [RequireServiceOrPermission(StaffPermissions.ManageUsers)]
        [HttpGet("{id:int}", Name = "GetPermissionGrantById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        // Every write here hands out or takes away nodes, including the ones the currency and
        // admin routes check, so none of them is anonymous any more (KNG-22). The plugin's
        // /knk user perm calls the by-node routes with its key and names the staff member.
        [RequireServiceOrPermission(StaffPermissions.UserPermissions)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PermissionGrantDto dto)
        {
            if (dto == null) return BadRequest();
            var refused = await RefuseIfEscalationAsync(dto.HolderId, dto.Node);
            if (refused != null) return refused;
            try
            {
                // Upserts by (holder, node) since KNG-59, so this may return an existing row updated.
                var created = await _service.CreateAsync(dto, HttpContext.GetKnkCaller().ActorUserId);
                return CreatedAtRoute("GetPermissionGrantById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [RequireServiceOrPermission(StaffPermissions.UserPermissions)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PermissionGrantDto dto)
        {
            if (dto == null) return BadRequest();
            // Both the row as it is (changing it) and as it will be (granting it).
            var existing = await _service.GetByIdAsync(id);
            if (existing != null)
            {
                var refusedExisting = await RefuseIfEscalationAsync(existing.HolderId, existing.Node);
                if (refusedExisting != null) return refusedExisting;
            }
            var refused = await RefuseIfEscalationAsync(dto.HolderId, dto.Node);
            if (refused != null) return refused;
            try
            {
                await _service.UpdateAsync(id, dto, HttpContext.GetKnkCaller().ActorUserId);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (PermissionGrantConflictException ex)
            {
                return Conflict(new { code = "NodeTaken", message = ex.Message, existingGrantId = ex.ExistingGrantId });
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
            var existing = await _service.GetByIdAsync(id);
            if (existing != null)
            {
                var refused = await RefuseIfEscalationAsync(existing.HolderId, existing.Node);
                if (refused != null) return refused;
            }
            try
            {
                await _service.DeleteAsync(id, HttpContext.GetKnkCaller().ActorUserId);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (DbUpdateException ex)
            {
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }

        [RequireServiceOrPermission(StaffPermissions.UserPermissions)]
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<PermissionGrantListDto>>> Search([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        [RequireServiceOrPermission(StaffPermissions.UserPermissions)]
        [HttpPut("by-node")]
        public async Task<IActionResult> UpsertByNode([FromBody] UpsertPermissionGrantByNodeDto dto)
        {
            if (dto == null) return BadRequest();
            var refused = await RefuseIfEscalationAsync(dto.HolderId, dto.Node);
            if (refused != null) return refused;
            try
            {
                var result = await _service.UpsertByNodeAsync(dto.HolderId, dto.Node, dto.Value, dto.ExpiresAt, HttpContext.GetKnkCaller().ActorUserId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [RequireServiceOrPermission(StaffPermissions.UserPermissions)]
        [HttpDelete("by-node")]
        public async Task<IActionResult> RevokeByNode([FromQuery] int holderId, [FromQuery] string node)
        {
            var refused = await RefuseIfEscalationAsync(holderId, node);
            if (refused != null) return refused;
            try
            {
                await _service.RevokeByNodeAsync(holderId, node, HttpContext.GetKnkCaller().ActorUserId);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}

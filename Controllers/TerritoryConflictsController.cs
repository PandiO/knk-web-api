using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// Territory conflicts (territory DESIGN § 3, PandiO/knk-workspace#326): written by the game server's report
    /// (<c>PUT api/Territory/report</c>), resolved by admins only. No create or edit form (G17: a read-only form).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("TerritoryConflict")]
    public class TerritoryConflictsController : ControllerBase
    {
        private readonly ITerritoryConflictService _service;

        public TerritoryConflictsController(ITerritoryConflictService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool openOnly = false) => Ok(await _service.GetAllAsync(openOnly));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<TerritoryConflictListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        /// <summary>Award the area to one of the two holders, or mark the lines redrawn.</summary>
        [HttpPost("{id:int}/resolve")]
        public async Task<IActionResult> Resolve(int id, [FromBody] TerritoryConflictResolveDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await _service.ResolveAsync(id, dto, User.GetUserId()));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("{id:int}/reopen")]
        public async Task<IActionResult> Reopen(int id)
        {
            try
            {
                return Ok(await _service.ReopenAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280).
        [RequireServiceOrPermission(StaffPermissions.Delete)]
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
        }
    }
}

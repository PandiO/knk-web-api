using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// Transport lanes (P5, PandiO/knk-workspace#79, production DESIGN § 7.6): the measured route between two structures.
    /// The game server measures them (P5b #681: <c>GET due</c>, <c>POST report</c>); staff can type one in. Reads: the
    /// World area (or the plugin key); writes: knk.admin.world (+ the dashboard change rule); delete also knk.admin.delete.
    /// </summary>
    [ApiController]
    [Route("api/TransportLanes")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("TransportLane")]
    public class TransportLanesController : ControllerBase
    {
        private readonly ITransportLaneService _service;

        public TransportLanesController(ITransportLaneService service)
        {
            _service = service;
        }

        [HttpGet("{id:int}", Name = "GetTransportLaneById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Newest measurement first; filters fromStructureId, toStructureId, structureId (either end), source.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<TransportLaneListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        [RequireDashboardChange("TransportLane")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TransportLaneDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetTransportLaneById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.PlainMessage() });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        [RequireDashboardChange("TransportLane")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] TransportLaneDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await _service.UpdateAsync(id, dto));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.PlainMessage() });
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

        /// <summary>The game server's lane measurements (P5b): upserted by (from, to) with source Plugin. Plugin key or
        /// knk.admin.world.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("report")]
        public async Task<ActionResult<TransportLaneReportResultDto>> Report([FromBody] TransportLaneReportDto dto, CancellationToken ct)
        {
            try
            {
                return Ok(await _service.ReportAsync(dto, ct));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.PlainMessage() });
            }
        }

        /// <summary>The pairs the game server should measure (missing or stale), with both structures' positions. Empty while
        /// Production:Enabled is off.</summary>
        [HttpGet("due")]
        public async Task<ActionResult<List<TransportLaneDueDto>>> Due([FromQuery] int limit = 100, CancellationToken ct = default) =>
            Ok(await _service.DueAsync(limit, ct));
    }
}

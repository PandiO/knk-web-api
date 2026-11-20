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
    /// Transport orders (P5, PandiO/knk-workspace#79, production DESIGN § 7). Reads: the World area (or the plugin key).
    /// Manual orders, dry runs, cancel and run-now: plugin key or knk.admin.world. Orders are written by the transport
    /// service only (no edit or delete): a manual order is created, an order is cancelled. Manual orders, dry runs and
    /// cancel work while Production:Enabled is off; orders move only while it is on.
    /// </summary>
    [ApiController]
    [Route("api/TransportOrders")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("TransportOrder")]
    public class TransportOrdersController : ControllerBase
    {
        private readonly ITransportService _service;

        public TransportOrdersController(ITransportService service)
        {
            _service = service;
        }

        [HttpGet("{id:int}", Name = "GetTransportOrderById")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Newest first; filters status (comma list), origin, open (true/false), sourceStructureId,
        /// sourceStorageId, storageId (source or any stop), structureId (source, stop or carrier); search term = order id.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<TransportOrderListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        /// <summary>A manual (or quest) order. 400 with the plan's errors; a repeated clientKey returns the first order.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TransportOrderCreateDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto, HttpContext.GetKnkCaller().ActorUserId, ct);
                return CreatedAtRoute("GetTransportOrderById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.PlainMessage() });
            }
        }

        /// <summary>What a manual order would do (lines, availability, legs with distances and ETA, carrier, warnings),
        /// without writing anything.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("dry-run")]
        public async Task<ActionResult<TransportPlanDto>> DryRun([FromBody] TransportOrderCreateDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest();
            return Ok(await _service.DryRunAsync(dto, ct));
        }

        /// <summary>Cancels: before loading the order ends Cancelled; a loaded order turns back and returns its cargo.
        /// 409 when it is final or already returning.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] TransportCancelDto? dto, CancellationToken ct)
        {
            try
            {
                return Ok(await _service.CancelAsync(id, HttpContext.GetKnkCaller().ActorUserId, dto?.Reason, ct));
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

        /// <summary>Runs the transport sweep now over every open order (and the automatic trigger). Plugin key or
        /// knk.admin.world. 409 while Production:Enabled is off (A21).</summary>
        /// <summary>Staff retry (P6a #683, M4): a waiting order tries its next step now; a Returned or Cancelled one is sent
        /// again as a new manual order. 409 while Production:Enabled is off, while on the road, or once delivered.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("{id:int}/retry")]
        public async Task<IActionResult> Retry(int id, CancellationToken ct)
        {
            try
            {
                return Ok(await _service.RetryAsync(id, HttpContext.GetKnkCaller().ActorUserId, ct));
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

        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("run-now")]
        public async Task<ActionResult<TransportSweepResultDto>> RunNow(CancellationToken ct)
        {
            var result = await _service.SweepAsync(all: true, ct);
            if (!result.FeatureEnabled) return Conflict(new { error = "Production is turned off (Production:Enabled)." });
            return Ok(result);
        }
    }
}

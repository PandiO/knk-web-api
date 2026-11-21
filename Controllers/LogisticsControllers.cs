using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Services.Logistics;
using knkwebapi_v2.Services.Stock;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    // Town logistics P9 (PandiO/knk-workspace#127, TOWN_LOGISTICS.md § 2-5). Reads: the World area (or the plugin key).
    // Writes: knk.admin.world or the plugin key (+ the dashboard change rule on create/update); deletes also
    // knk.admin.delete. Policies, rules, settings and dry runs work while Production:Enabled is off (DESIGN § 18.2); a
    // run now is 409 ProductionDisabled while it is off (A21).

    /// <summary>Versioned logistics policies: a town's, a warehouse's, and the default templates (D-L3). A save
    /// (<c>PUT {id}</c>) makes a new version and returns it; <c>GET {id}/versions</c> is the history.</summary>
    [ApiController]
    [Route("api/LogisticsPolicies")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("LogisticsPolicy")]
    public class LogisticsPoliciesController : ControllerBase
    {
        private readonly ILogisticsPolicyService _service;

        public LogisticsPoliciesController(ILogisticsPolicyService service) => _service = service;

        [HttpGet("{id:int}", Name = "GetLogisticsPolicyById")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Current versions by default; filters scope, townId, warehouseId, lineageId, current (true, false, all),
        /// specialization; search term = name.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<LogisticsPolicyListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        [HttpGet("{id:int}/versions")]
        public Task<IActionResult> Versions(int id, CancellationToken ct) => LogisticsErrors.Run(async () => Ok(await _service.VersionsAsync(id, ct)));

        [RequireDashboardChange("LogisticsPolicy")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] LogisticsPolicyDto dto, CancellationToken ct) => LogisticsErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateAsync(dto, HttpContext.GetKnkCaller().ActorUserId, ct);
            return CreatedAtRoute("GetLogisticsPolicyById", new { id = created.Id }, created);
        });

        /// <summary>Saves version n + 1 (the given rules replace the list) and returns it; 409 unless <paramref name="id"/>
        /// is the current version.</summary>
        [RequireDashboardChange("LogisticsPolicy")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] LogisticsPolicyDto dto, CancellationToken ct) => LogisticsErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            return Ok(await _service.UpdateAsync(id, dto, HttpContext.GetKnkCaller().ActorUserId, ct));
        });

        /// <summary>Deletes the policy with every version.</summary>
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id, CancellationToken ct) => LogisticsErrors.Run(async () =>
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        });

        /// <summary>The town's size and specialization (the labels the templates key on) and the template that fits them
        /// best (#928).</summary>
        [HttpGet("towns/{townId:int}/profile")]
        public Task<IActionResult> TownProfile(int townId, CancellationToken ct) =>
            LogisticsErrors.Run(async () => Ok(await _service.GetTownProfileAsync(townId, ct)));

        /// <summary>Sets the town's size and specialization: a null field is left as it is, an empty string clears it.
        /// Same rule as applying a template: knk.admin.world or the plugin key.</summary>
        [HttpPut("towns/{townId:int}/profile")]
        public Task<IActionResult> UpdateTownProfile(int townId, [FromBody] TownLogisticsProfileDto dto, CancellationToken ct) => LogisticsErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            return Ok(await _service.UpdateTownProfileAsync(townId, dto, ct));
        });

        /// <summary>Copies a template's rules into a town's policy (a new version of it, or its version 1).</summary>
        [HttpPost("{id:int}/apply")]
        public Task<IActionResult> ApplyTemplate(int id, [FromBody] LogisticsApplyTemplateDto dto, CancellationToken ct) => LogisticsErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            return Ok(await _service.ApplyTemplateAsync(id, dto, HttpContext.GetKnkCaller().ActorUserId, ct));
        });
    }

    /// <summary>Logistics rules, read only: they are edited inside their policy (a save is a new version).</summary>
    [ApiController]
    [Route("api/LogisticsRules")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("LogisticsRule")]
    public class LogisticsRulesController : ControllerBase
    {
        private readonly ILogisticsPolicyService _service;

        public LogisticsRulesController(ILogisticsPolicyService service) => _service = service;

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetRuleAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Rules of current versions by default; filters logisticsPolicyId, townId, type, current.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<LogisticsRuleListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchRulesAsync(query, ct));
    }

    /// <summary>Consumer replenishment settings (min/max per Input or Merchandise storage and blueprint, priority class).</summary>
    [ApiController]
    [Route("api/ConsumerReplenishments")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ConsumerReplenishment")]
    public class ConsumerReplenishmentsController : ControllerBase
    {
        private readonly IConsumerReplenishmentService _service;

        public ConsumerReplenishmentsController(IConsumerReplenishmentService service) => _service = service;

        [HttpGet("{id:int}", Name = "GetConsumerReplenishmentById")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Filters storageId, structureId, townId, itemBlueprintId, enabled; search term = structure or blueprint.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ConsumerReplenishmentListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        [RequireDashboardChange("ConsumerReplenishment")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] ConsumerReplenishmentDto dto, CancellationToken ct) => LogisticsErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateAsync(dto, ct);
            return CreatedAtRoute("GetConsumerReplenishmentById", new { id = created.Id }, created);
        });

        [RequireDashboardChange("ConsumerReplenishment")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] ConsumerReplenishmentDto dto, CancellationToken ct) => LogisticsErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            return Ok(await _service.UpdateAsync(id, dto, ct));
        });

        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id, CancellationToken ct) => LogisticsErrors.Run(async () =>
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        });
    }

    /// <summary>Planner reservations (written by the planner); staff can release an open one.</summary>
    [ApiController]
    [Route("api/LogisticsReservations")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("LogisticsReservation")]
    public class LogisticsReservationsController : ControllerBase
    {
        private readonly ILogisticsReservationService _service;

        public LogisticsReservationsController(ILogisticsReservationService service) => _service = service;

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Newest first; filters townId, warehouseId, consumerStructureId, consumerStorageId, itemBlueprintId,
        /// status, open, transportOrderId, plannerRunId.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<LogisticsReservationListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        /// <summary>Releases an open reservation (its collection order is cancelled before loading); 409 otherwise.</summary>
        [HttpPost("{id:int}/release")]
        public Task<IActionResult> Release(int id, [FromBody] LogisticsReleaseDto? dto, CancellationToken ct) => LogisticsErrors.Run(async () =>
            Ok(await _service.ReleaseAsync(id, HttpContext.GetKnkCaller().ActorUserId, dto?.Reason, ct)));
    }

    /// <summary>Planner runs with their plan explanation (read only).</summary>
    [ApiController]
    [Route("api/LogisticsPlannerRuns")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("LogisticsPlannerRun")]
    public class LogisticsPlannerRunsController : ControllerBase
    {
        private readonly ILogisticsPlannerRunService _service;

        public LogisticsPlannerRunsController(ILogisticsPlannerRunService service) => _service = service;

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<LogisticsPlannerRunListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));
    }

    /// <summary>The planner and the town views: supply, service areas, a consumer, a dry run and a run now.</summary>
    [ApiController]
    [Route("api/Logistics")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("LogisticsPlannerRun")]
    public class LogisticsController : ControllerBase
    {
        private readonly ILogisticsPlanner _planner;

        public LogisticsController(ILogisticsPlanner planner) => _planner = planner;

        /// <summary>The town supply view: stock per blueprint across its warehouses, reserves, reservations, available,
        /// flows and days of cover; the last run and the next.</summary>
        [HttpGet("towns/{townId:int}/supply")]
        public Task<IActionResult> Supply(int townId, CancellationToken ct) => LogisticsErrors.Run(async () => Ok(await _planner.SupplyAsync(townId, ct)));

        [HttpGet("towns/{townId:int}/service-areas")]
        public Task<IActionResult> ServiceAreas(int townId, CancellationToken ct) =>
            LogisticsErrors.Run(async () => Ok(await _planner.ServiceAreasAsync(townId, ct)));

        /// <summary>What a run would do now (reservations, collection orders, releases and the explanation per consumer
        /// line), without writing anything. Works while Production:Enabled is off.</summary>
        [HttpPost("towns/{townId:int}/dry-run")]
        public Task<IActionResult> DryRun(int townId, CancellationToken ct) => LogisticsErrors.Run(async () => Ok(await _planner.DryRunAsync(townId, ct)));

        /// <summary>Runs the town's planner now. 409 ProductionDisabled while Production:Enabled is off.</summary>
        [HttpPost("towns/{townId:int}/run-now")]
        public Task<IActionResult> RunNow(int townId, CancellationToken ct) =>
            LogisticsErrors.Run(async () => Ok(await _planner.RunAsync(townId, HttpContext.GetKnkCaller().ActorUserId, ct)));

        [HttpGet("consumers/{structureId:int}")]
        public async Task<IActionResult> Consumer(int structureId, CancellationToken ct)
        {
            var view = await _planner.ConsumerAsync(structureId, ct);
            return view == null ? NotFound() : Ok(view);
        }
    }

    internal static class LogisticsErrors
    {
        public static async Task<IActionResult> Run(Func<Task<IActionResult>> work)
        {
            try
            {
                return await work();
            }
            catch (KeyNotFoundException ex)
            {
                return new NotFoundObjectResult(new { error = ex.Message });
            }
            catch (ProductionOffException ex)
            {
                return new ConflictObjectResult(new { code = "ProductionDisabled", error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return new ConflictObjectResult(new { error = ex.Message });
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>Production structures (P2, PandiO/knk-workspace#91): in a district or rural, each with its default storages; production itself comes with P3. Reads: the World area (or the plugin key); writes: knk.admin.world (+ the dashboard change
    /// rule); delete also knk.admin.delete. Admin CRUD works while Production:Enabled is off.</summary>
    [ApiController]
    [Route("api/ProductionStructures")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ProductionStructure")]
    public class ProductionStructuresController : ControllerBase
    {
        private readonly IProductionStructureService _service;

        public ProductionStructuresController(IProductionStructureService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetProductionStructureById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("ProductionStructure")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductionStructureDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetProductionStructureById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.PlainMessage() });
            }
        }

        [RequireDashboardChange("ProductionStructure")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProductionStructureDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                // 200 with the saved structure and its placement warnings, as create (#831).
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
        }

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280). 409 while one of its
        // storages holds stock or has ledger history.
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ProductionWorldListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        /// <summary>The structure's production (P3, #86): lines with their current rate and modifiers, "why not
        /// producing" reasons and the latest cycles. Settles the structure first while Production:Enabled is on.</summary>
        [HttpGet("{id:int}/production")]
        public async Task<IActionResult> GetProduction(int id, [FromServices] IProductionEngine engine)
        {
            var overview = await engine.OverviewAsync(id);
            return overview == null ? NotFound() : Ok(overview);
        }

        /// <summary>The workers at the structure now (P3; P5 and the game server send workers away): plugin key or
        /// knk.admin.world. Settles first, so the time before counts at the old worker factor.</summary>
        [HttpPut("{id:int}/workers")]
        public async Task<IActionResult> SetWorkers(int id, [FromBody] WorkersPresentUpdateDto dto, [FromServices] IProductionEngine engine)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await engine.SetWorkersPresentAsync(id, dto.WorkersPresent));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.PlainMessage() });
            }
        }

        /// <summary>Raises the level by one (PRODUCTION_KINDS § 5): the next level's resources move from a storage to
        /// SYS_CONSUMPTION and its coins are spent from the payer, in one transaction. Area node AND knk.economy.admin
        /// (it spends stock and coins). 409 when the cost can't be paid or the structure is at level 10.</summary>
        [RequireServiceOrPermission(StaffPermissions.EconomyAdmin)]
        [HttpPost("{id:int}/upgrade")]
        public async Task<IActionResult> Upgrade(int id, [FromBody] ProductionUpgradeRequestDto? dto, [FromServices] IProductionEngine engine)
        {
            try
            {
                var caller = HttpContext.GetKnkCaller();
                return Ok(await engine.UpgradeAsync(id, dto ?? new ProductionUpgradeRequestDto(), caller.ActorUserId));
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

        /// <summary>The structure's yield (P4, #138): its yield source, areas, and every output with the survey units,
        /// shares, yield rows and Hybrid override behind its base rate (the web app's rate explanation).</summary>
        [HttpGet("{id:int}/yield")]
        public async Task<IActionResult> GetYield(int id, [FromServices] IProductionYieldService yield)
        {
            var dto = await yield.ExplainAsync(id);
            return dto == null ? NotFound() : Ok(dto);
        }

        /// <summary>Asks the game server to survey the structure's enabled areas (or one, <c>productionAreaId</c>) at its
        /// next poll (A10, "survey now"): plugin key or knk.admin.world. 409 for a Configured structure (G5) or one
        /// without an enabled area.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("{id:int}/survey")]
        public async Task<IActionResult> RequestSurvey(int id, [FromBody] SurveyRequestDto? dto, [FromServices] IProductionAreaService areas)
        {
            try
            {
                return Ok(await areas.RequestSurveyAsync(id, dto?.ProductionAreaId));
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

        /// <summary>Makes a line for every output the latest surveys (and Hybrid pins) yield (staff tool; it also runs
        /// after every survey, override and yield source change): plugin key or knk.admin.world.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("{id:int}/materialize")]
        public async Task<IActionResult> Materialize(int id, [FromServices] IProductionYieldService yield)
        {
            try
            {
                return Ok(await yield.MaterializeAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>The road connection the game server measured (DESIGN § 7.6, P5b): plugin key or knk.admin.world.</summary>
        [HttpPut("{id:int}/road-connection")]
        public async Task<IActionResult> SetRoadConnection(int id, [FromBody] RoadConnectionUpdateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await _service.SetRoadConnectionAsync(id, dto));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.PlainMessage() });
            }
        }
    }
}

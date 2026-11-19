using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>Production lines: one output of a production structure with its configured rate and inputs (production engine P3, PandiO/knk-workspace#86). Reads: the World area (or the plugin key);
    /// writes: knk.admin.world (+ the dashboard change rule); delete also knk.admin.delete. Works while
    /// Production:Enabled is off (DESIGN § 18.2).</summary>
    [ApiController]
    [Route("api/ProductionLines")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ProductionLine")]
    public class ProductionLinesController : ControllerBase
    {
        private readonly IProductionLineService _service;

        public ProductionLinesController(IProductionLineService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetProductionLineById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("ProductionLine")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductionLineDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetProductionLineById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [RequireDashboardChange("ProductionLine")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProductionLineDto dto)
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
                return BadRequest(new { error = ex.Message });
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ProductionLineListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        /// <summary>Settles the line up to now (staff tools): 409 while Production:Enabled is off.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("{id:int}/settle")]
        public async Task<IActionResult> Settle(int id, [FromServices] IProductionEngine engine,
            [FromServices] knkwebapi_v2.Services.Territory.IProductionSettingsReader settings)
        {
            if (!(await settings.GetAsync()).Enabled) return Conflict(new { error = "Production is turned off (Production:Enabled)." });
            if (await _service.GetByIdAsync(id) == null) return NotFound();
            var record = await engine.SettleLineAsync(id);
            return Ok(new { settled = record != null, cycle = record });
        }
    }
}

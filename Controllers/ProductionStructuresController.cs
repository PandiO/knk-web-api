using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
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
                return BadRequest(new { error = ex.Message });
            }
        }

        [RequireDashboardChange("ProductionStructure")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProductionStructureDto dto)
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

        /// <summary>The road connection the game server measured (DESIGN § 7.6, P5b): plugin key or knk.admin.world.</summary>
        [RequireDashboardChange("ProductionStructure")]
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
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}

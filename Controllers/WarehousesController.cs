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
    /// <summary>Warehouses (P2, PandiO/knk-workspace#91): a town's storage hubs, in a district or rural, each with a default delivery storage. Reads: the World area (or the plugin key); writes: knk.admin.world (+ the dashboard change
    /// rule); delete also knk.admin.delete. Admin CRUD works while Production:Enabled is off.</summary>
    [ApiController]
    [Route("api/Warehouses")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("Warehouse")]
    public class WarehousesController : ControllerBase
    {
        private readonly IWarehouseService _service;

        public WarehousesController(IWarehouseService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetWarehouseById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("Warehouse")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WarehouseDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetWarehouseById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.PlainMessage() });
            }
        }

        [RequireDashboardChange("Warehouse")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] WarehouseDto dto)
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
    }
}

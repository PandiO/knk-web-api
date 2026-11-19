using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>Provinces (optional, inside one kingdom) (territory V1, PandiO/knk-workspace#326). Reads: the World area (or the plugin key); writes:
    /// knk.admin.world (+ the dashboard change rule); delete also knk.admin.delete.</summary>
    [ApiController]
    [Route("api/Provinces")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("Province")]
    public class ProvincesController : ControllerBase
    {
        private readonly IProvinceService _service;

        public ProvincesController(IProvinceService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetProvinceById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("Province")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProvinceDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetProvinceById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [RequireDashboardChange("Province")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProvinceDto dto)
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
        public async Task<ActionResult<PagedResultDto<ProvinceListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));
    }
}

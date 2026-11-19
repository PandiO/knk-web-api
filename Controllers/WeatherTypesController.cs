using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>Weather types with their modifiers per resource category (production engine P3, PandiO/knk-workspace#86). Reads: the World area (or the plugin key);
    /// writes: knk.admin.world (+ the dashboard change rule); delete also knk.admin.delete. Works while
    /// Production:Enabled is off (DESIGN § 18.2).</summary>
    [ApiController]
    [Route("api/WeatherTypes")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("WeatherType")]
    public class WeatherTypesController : ControllerBase
    {
        private readonly IWeatherTypeService _service;

        public WeatherTypesController(IWeatherTypeService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetWeatherTypeById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("WeatherType")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WeatherTypeDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetWeatherTypeById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [RequireDashboardChange("WeatherType")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] WeatherTypeDto dto)
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

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280). 409 while a zone has odds for it.
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
        public async Task<ActionResult<PagedResultDto<WeatherTypeListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));
    }
}

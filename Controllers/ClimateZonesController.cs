using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>Climate zones: weather odds and the towns they cover; the weather is rolled per in-game day (production engine P3, PandiO/knk-workspace#86). Reads: the World area (or the plugin key);
    /// writes: knk.admin.world (+ the dashboard change rule); delete also knk.admin.delete. Works while
    /// Production:Enabled is off (DESIGN § 18.2).</summary>
    [ApiController]
    [Route("api/ClimateZones")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ClimateZone")]
    public class ClimateZonesController : ControllerBase
    {
        private readonly IClimateZoneService _service;

        public ClimateZonesController(IClimateZoneService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetClimateZoneById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("ClimateZone")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ClimateZoneDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetClimateZoneById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [RequireDashboardChange("ClimateZone")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ClimateZoneDto dto)
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

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280). 409 for the default zone.
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
        public async Task<ActionResult<PagedResultDto<ClimateZoneListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        /// <summary>The stored weather rolls, newest first (filter climateZoneId).</summary>
        [HttpPost("rolls/search")]
        public async Task<ActionResult<PagedResultDto<WeatherRollDto>>> SearchRolls([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchRollsAsync(query));
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>Production areas: a structure's yield areas with their shapes, the survey queue the game server reads and
    /// the surveys it reports (yield areas P4, PandiO/knk-workspace#138). Reads: the World area (or the plugin key);
    /// writes: knk.admin.world (+ the dashboard change rule); delete also knk.admin.delete. Admin CRUD works while
    /// Production:Enabled is off (DESIGN § 18.2); the survey queue is empty and reports are 409 while it is off (A21).</summary>
    [ApiController]
    [Route("api/ProductionAreas")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ProductionArea")]
    public class ProductionAreasController : ControllerBase
    {
        private readonly IProductionAreaService _service;

        public ProductionAreasController(IProductionAreaService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetProductionAreaById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>400 for a bad shape or an area over the level's cap (G6, A11); 409 when an exclusive area overlaps
        /// another exclusive area of the same model.</summary>
        [RequireDashboardChange("ProductionArea")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductionAreaDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetProductionAreaById", new { id = created.Id }, created);
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

        [RequireDashboardChange("ProductionArea")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProductionAreaDto dto)
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
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280). Its surveys go with it.
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

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ProductionAreaListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        /// <summary>Every other area whose bounding box overlaps this one (shared areas split their units, G7).</summary>
        [HttpGet("{id:int}/overlaps")]
        public async Task<IActionResult> Overlaps(int id)
        {
            try
            {
                return Ok(await _service.OverlapsAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>The areas the game server should survey now: requested ones first, then those never surveyed or
        /// surveyed more than a day ago, of Derived and Hybrid structures only (G5). Empty while Production:Enabled is off.</summary>
        [HttpGet("survey-due")]
        public async Task<IActionResult> SurveyDue([FromQuery] int limit = 50) => Ok(await _service.SurveyDueAsync(limit));

        /// <summary>The game server reports a survey (plugin key or knk.admin.world): stored in the history, the area's
        /// latest survey, and the structure's lines are materialized. 409 while Production:Enabled is off or for a
        /// Configured structure (G5).</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("{id:int}/surveys")]
        public async Task<IActionResult> ReportSurvey(int id, [FromBody] AreaSurveyDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await _service.ReportSurveyAsync(id, dto));
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
    }
}

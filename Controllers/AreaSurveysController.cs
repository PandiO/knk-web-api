using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>The survey history (yield areas P4, PandiO/knk-workspace#138): read only; surveys are reported through
    /// <c>POST api/ProductionAreas/{id}/surveys</c>. Reads: the World area (or the plugin key).</summary>
    [ApiController]
    [Route("api/AreaSurveys")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("AreaSurvey")]
    public class AreaSurveysController : ControllerBase
    {
        private readonly IAreaSurveyService _service;

        public AreaSurveysController(IAreaSurveyService service)
        {
            _service = service;
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Newest first by default; filters productionAreaId, productionStructureId, trigger.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<AreaSurveyListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        /// <summary>An area's latest surveys (oldest first) with each unit key's count per survey (the trend).</summary>
        [HttpGet("trend")]
        public async Task<IActionResult> Trend([FromQuery] int productionAreaId, [FromQuery] int take = 30)
        {
            try
            {
                return Ok(await _service.TrendAsync(productionAreaId, take));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}

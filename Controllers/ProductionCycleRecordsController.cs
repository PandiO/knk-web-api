using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>The production cycle trace (production engine P3, PandiO/knk-workspace#86): one record per settle with
    /// each applied modifier. Read only; reads need the World area (or the plugin key).</summary>
    [ApiController]
    [Route("api/ProductionCycleRecords")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ProductionCycleRecord")]
    public class ProductionCycleRecordsController : ControllerBase
    {
        private readonly IProductionCycleRecordService _service;

        public ProductionCycleRecordsController(IProductionCycleRecordService service)
        {
            _service = service;
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Newest first; filters productionStructureId, productionLineId.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ProductionCycleRecordDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));
    }
}

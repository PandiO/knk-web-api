using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>Production engine staff tools (P3, PandiO/knk-workspace#86).</summary>
    [ApiController]
    [Route("api/Production")]
    public class ProductionController : ControllerBase
    {
        private readonly IProductionEngine _engine;

        public ProductionController(IProductionEngine engine)
        {
            _engine = engine;
        }

        /// <summary>Runs the sweep now: rolls today's weather and settles every line up to now (not only the due ones).
        /// Plugin key or knk.admin.world. 409 while Production:Enabled is off (A21: nothing runs then).</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageWorld)]
        [HttpPost("run-now")]
        public async Task<ActionResult<ProductionSweepResultDto>> RunNow(CancellationToken ct)
        {
            var result = await _engine.SweepAsync(all: true, ct);
            if (!result.FeatureEnabled) return Conflict(new { error = "Production is turned off (Production:Enabled)." });
            return Ok(result);
        }
    }
}

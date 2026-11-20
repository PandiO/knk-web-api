using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege shop (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.3, § 6.2.2; P25; PandiO/knk-workspace#867): the
    // effective catalogue of a match, which the plugin fetches once at match start (phase 3, #868). Read by the plugin
    // key or staff who may read shop entries (the SiegeShopEntry type's read nodes).
    [ApiController]
    [Route("api/[controller]")]
    [DashboardReads("SiegeShopEntry")]
    public class SiegeShopController : ControllerBase
    {
        private readonly ISiegeShopService _service;

        public SiegeShopController(ISiegeShopService service)
        {
            _service = service;
        }

        // GET api/SiegeShop/catalogue?mode=Siege&scenarioId=3. mode is required (Siege or SiegeSurvival); without a
        // scenario the mode's enabled default entries are returned.
        [HttpGet("catalogue")]
        public async Task<ActionResult<SiegeShopCatalogueDto>> GetCatalogue([FromQuery] SiegeGameMode? mode, [FromQuery] int? scenarioId)
        {
            if (mode == null) return BadRequest("mode is required (Siege or SiegeSurvival).");
            try
            {
                return Ok(await _service.GetCatalogueAsync(mode.Value, scenarioId));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}

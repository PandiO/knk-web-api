using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// Reads for the in-game structure menus (P6a, PandiO/knk-workspace#683, production DESIGN § 11.2, decision record
    /// § 15.1 M1-M5): the structures overview, the structure menu and its transport orders list. Any logged-in user or the
    /// game server may call them; the API filters by the viewer (the web user, or the player the game server names in
    /// <c>X-Acting-User-Id</c>): every viewer gets the public fields, members of the structure's town and staff
    /// (<c>knk.admin.world</c>) also the detail (M3, A25). A game-server call without a player is a visitor. Reads stay on
    /// while <c>Production:Enabled</c> is off (G16). The staff buttons (M4) call the existing staff routes: survey now
    /// (<c>POST api/ProductionStructures/{id}/survey</c>), dry run (<c>GET api/ProductionStructures/{id}/transport</c>),
    /// cancel and retry (<c>POST api/TransportOrders/{id}/cancel</c>, <c>/retry</c>).
    /// </summary>
    [ApiController]
    [Route("api/StructureMenus")]
    public class StructureMenusController : ControllerBase
    {
        private readonly IStructureMenuService _service;
        private readonly IProductionViewerResolver _viewers;

        public StructureMenusController(IStructureMenuService service, IProductionViewerResolver viewers)
        {
            _service = service;
            _viewers = viewers;
        }

        /// <summary>The structures overview: public rows, filterable by town, kind, type and status (M2).</summary>
        [RequireServiceOrLoggedIn]
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<StructureMenuRowDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        /// <summary>The structure menu, filtered by the viewer (M3).</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var viewer = await _viewers.ResolveAsync(HttpContext.GetKnkCaller(), ct);
            var dto = await _service.GetAsync(id, viewer, ct);
            return dto == null ? NotFound() : Ok(dto);
        }

        /// <summary>The structure's transport orders, newest first: members of its town and staff only (403 otherwise).</summary>
        [HttpGet("{id:int}/orders")]
        public async Task<IActionResult> Orders(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 28, CancellationToken ct = default)
        {
            var viewer = await _viewers.ResolveAsync(HttpContext.GetKnkCaller(), ct);
            try
            {
                var result = await _service.OrdersAsync(id, viewer, new PagedQueryDto { PageNumber = page, PageSize = pageSize }, ct);
                return result == null ? NotFound() : Ok(result);
            }
            catch (StorageAccessDeniedException ex)
            {
                return StatusCode(403, new { error = "Forbidden", message = ex.Message });
            }
        }
    }
}

using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Map;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// The public overview map (PandiO/knk-workspace#736; OVERVIEW_MAP_DESIGN § 6.2.1-6.2.3, O02, O03, O16, O27, O29).
    /// Anonymous by design (O02): the map is public and every response is an allow-list DTO (O32). Each route is in
    /// <c>EndpointAccessRulesTests.AnonymousAllowList</c>, under the per-IP <c>map</c> rate limit, and answers 404 while
    /// <c>Map:Enabled</c> is off (the default; spec § 5 rule 15, A03).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting(RateLimitingSetup.MapPolicy)]
    [MapEnabled]
    public class MapController : ControllerBase
    {
        private readonly IMapPublicService _map;

        public MapController(IMapPublicService map)
        {
            _map = map;
        }

        /// <summary>Enabled parts, label distances, flash settings, polling interval and the marker version.</summary>
        [AllowAnonymous]
        [HttpGet("config")]
        [ProducesResponseType(typeof(MapConfigDto), 200)]
        public async Task<IActionResult> GetConfig(CancellationToken ct) => Ok(await _map.GetConfigAsync(ct));

        /// <summary>Pins and outlines of every town, district and structure with a pin position (O18).</summary>
        [AllowAnonymous]
        [HttpGet("markers")]
        [ProducesResponseType(typeof(MapMarkersDto), 200)]
        public async Task<IActionResult> GetMarkers(CancellationToken ct) => Ok(await _map.GetMarkersAsync(ct));

        /// <summary>The public town panel (O32).</summary>
        [AllowAnonymous]
        [HttpGet("towns/{id:int}")]
        [ProducesResponseType(typeof(MapTownPanelDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetTown(int id, CancellationToken ct)
        {
            var dto = await _map.GetTownAsync(id, ct);
            return dto == null ? NotFound() : Ok(dto);
        }

        /// <summary>The public district panel (O32).</summary>
        [AllowAnonymous]
        [HttpGet("districts/{id:int}")]
        [ProducesResponseType(typeof(MapDistrictPanelDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetDistrict(int id, CancellationToken ct)
        {
            var dto = await _map.GetDistrictAsync(id, ct);
            return dto == null ? NotFound() : Ok(dto);
        }

        /// <summary>The public structure panel (O32); a gate adds its doors and "under siege".</summary>
        [AllowAnonymous]
        [HttpGet("structures/{id:int}")]
        [ProducesResponseType(typeof(MapStructurePanelDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetStructure(int id, CancellationToken ct)
        {
            var dto = await _map.GetStructureAsync(id, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
    }

    /// <summary>
    /// Answers 404 before the action runs while <c>Map:Enabled</c> is off (O27, spec § 5 rule 15), so a disabled map
    /// looks like an absent route and reads nothing. Read live, so a settings reload takes effect without a restart.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class MapEnabledAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var options = context.HttpContext.RequestServices.GetService<IOptionsMonitor<MapOptions>>();
            if (options?.CurrentValue.Enabled != true)
            {
                context.Result = new NotFoundResult();
            }
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}

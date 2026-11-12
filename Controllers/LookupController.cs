using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Pickers and relation labels for a type the caller can't read in full (PandiO/knk-workspace#280 AM2, knk-workspace
    /// docs/specs/dashboard-access-model/DESIGN.md § 4.6.3, category L): id, label and one detail line, never an email.
    /// Open to anyone holding any area node, and to the plugin key. Unknown types answer 404, a registry type without a
    /// lookup (link rows, snapshots) 400 <c>LookupNotAvailable</c>.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LookupController : ControllerBase
    {
        private readonly ILookupService _lookups;

        public LookupController(ILookupService lookups)
        {
            _lookups = lookups;
        }

        /// <summary>One page of matches: <c>{ items: [{ id, label, detail? }], total }</c>; at most 50 per page.</summary>
        [RequireServiceOrAnyAreaNode(ReadCategory.Lookup)]
        [HttpPost("{entityType}/search")]
        public async Task<IActionResult> Search(string entityType, [FromBody] LookupSearchRequestDto? request)
        {
            var type = DashboardAccess.Find(entityType);
            if (type == null) return UnknownType();
            if (!type.LookupSearchable) return NotAvailable(type);
            return Ok(await _lookups.SearchAsync(type, request ?? new LookupSearchRequestDto()));
        }

        /// <summary>One record as a label: <c>{ id, label, detail? }</c>.</summary>
        [RequireServiceOrAnyAreaNode(ReadCategory.Lookup)]
        [HttpGet("{entityType}/{id:int}")]
        public async Task<IActionResult> Get(string entityType, int id)
        {
            var type = DashboardAccess.Find(entityType);
            if (type == null) return UnknownType();
            if (!type.LookupGettable) return NotAvailable(type);
            var item = await _lookups.GetAsync(type, id);
            return item == null ? NotFound(new { error = "NotFound", message = $"{type.TypeName} {id} not found." }) : Ok(item);
        }

        private IActionResult UnknownType() =>
            NotFound(new { error = "UnknownEntityType", message = "Not a dashboard entity type." });

        private IActionResult NotAvailable(DashboardTypeAccess type) =>
            BadRequest(new { error = "LookupNotAvailable", message = $"{type.TypeName} has no lookup." });
    }
}

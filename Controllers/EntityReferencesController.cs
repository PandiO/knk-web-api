using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// "Show details" of a refused delete (PandiO/knk-workspace#44): the rows that still use a record, fetched only when the
    /// admin asks. Read-only. Access: <see cref="RequireReferenceReadAttribute"/> (one of the record type's read nodes, web
    /// users only); each group's rows are trimmed to what the caller may see (<see cref="IEntityReferenceRowsService"/>).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class EntityReferencesController : ControllerBase
    {
        private readonly IEntityReferenceRowsService _rows;

        public EntityReferencesController(IEntityReferenceRowsService rows)
        {
            _rows = rows;
        }

        /// <summary>
        /// The references that stop a delete of <c>{entityType} {id}</c>, each with up to <c>take</c> rows (default 20,
        /// at most 50) as <c>{ id, label, detail? }</c>; <c>referencedBy</c> ("Structure.StreetId") narrows to one group.
        /// GET api/EntityReferences/Street/12/rows?referencedBy=Structure.StreetId&amp;take=20
        /// </summary>
        /// <response code="200">{ entityTypeName, id, groups: [{ referencedBy, entityType, count, listed, readable, nodes, reason?, rows, more }] }</response>
        /// <response code="403">No read node of the type (<c>NoReadAccess</c>), or the plugin key</response>
        /// <response code="404">Not a dashboard type</response>
        [RequireReferenceRead]
        [HttpGet("{entityType}/{id:int}/rows")]
        public async Task<IActionResult> GetRows(string entityType, int id, [FromQuery] string? referencedBy, [FromQuery] int? take)
        {
            var type = DashboardAccess.Find(entityType);
            if (type == null) return NotFound(new { error = "UnknownEntityType", message = "Not a dashboard entity type." });
            if (HttpContext.GetKnkCaller().WebUserId is not { } userId) return Unauthorized();

            var result = await _rows.GetAsync(type, id, userId, referencedBy, take ?? EntityReferenceRowsService.DefaultTake, HttpContext);
            return result == null
                ? NotFound(new { error = "UnknownEntityType", message = $"{type.TypeName} has no references to list." })
                : Ok(result);
        }
    }
}

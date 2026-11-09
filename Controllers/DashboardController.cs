using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// The dashboard's actions on many rows at once (PandiO/knk-workspace#280, knk-workspace
    /// docs/specs/dashboard-access-model/DESIGN.md § 4.7, § 4.8). Web users only: the nodes are checked with
    /// <c>RequirePermission</c> (no plugin key), and the type's area node from <see cref="DashboardAccess"/> in the action.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardActionService _actions;
        private readonly IPermissionResolutionService _permissions;

        public DashboardController(IDashboardActionService actions, IPermissionResolutionService permissions)
        {
            _actions = actions;
            _permissions = permissions;
        }

        /// <summary>
        /// Deletes up to 100 records of one type, each through the type's own delete in its own save, and answers
        /// which were deleted and why the others weren't. Needs knk.admin.delete.bulk, knk.admin.delete and the type's
        /// area node; not for users and permission groups (one at a time only, decision 6). One audit entry.
        /// </summary>
        /// <response code="200">{ deleted: [id], refused: [{ id, reason }] }</response>
        /// <response code="400">No ids, more than 100, or a type that can't be bulk deleted</response>
        /// <response code="403">A node is missing</response>
        /// <response code="404">Not a dashboard type</response>
        [RequirePermission(StaffPermissions.DeleteBulk)]
        [RequirePermission(StaffPermissions.Delete)]
        [EnableRateLimiting(RateLimitingSetup.LookupPolicy)]
        [HttpPost("bulk-delete")]
        public async Task<IActionResult> BulkDelete([FromBody] DashboardBulkDeleteRequestDto? request)
        {
            var (type, refused) = await ResolveTypeAsync(request?.EntityType);
            if (refused != null) return refused;

            if (!type!.BulkDeletable)
            {
                return BadRequest(new
                {
                    error = "BulkDeleteNotAllowed",
                    message = type.Deletable
                        ? $"{type.TypeName} records can only be deleted one at a time."
                        : $"{type.TypeName} records can't be deleted from the dashboard."
                });
            }

            var ids = (request!.Ids ?? new List<int>()).Distinct().ToList();
            if (ids.Count == 0 || ids.Any(id => id <= 0))
            {
                return BadRequest(new { error = "InvalidIds", message = "Send the ids to delete (positive numbers)." });
            }
            if (ids.Count > DashboardActionService.MaxBulkDeleteIds)
            {
                return BadRequest(new
                {
                    error = "TooManyIds",
                    message = $"At most {DashboardActionService.MaxBulkDeleteIds} records per bulk delete."
                });
            }

            return Ok(await _actions.BulkDeleteAsync(type, ids, ActorId()));
        }

        /// <summary>
        /// The rows of one type for a CSV export: the selected ids, or with none the whole search (all pages, in its
        /// sort), as the type's own search returns them; at most 10 000 rows (<c>truncated</c> beyond). Needs
        /// knk.admin.export and the type's area node. One audit entry.
        /// </summary>
        /// <response code="200">{ items, total, truncated }</response>
        /// <response code="400">More than 10 000 ids, or a type without a list</response>
        /// <response code="403">A node is missing</response>
        /// <response code="404">Not a dashboard type</response>
        [RequirePermission(StaffPermissions.Export)]
        [EnableRateLimiting(RateLimitingSetup.LookupPolicy)]
        [HttpPost("export")]
        public async Task<IActionResult> Export([FromBody] DashboardExportRequestDto? request)
        {
            var (type, refused) = await ResolveTypeAsync(request?.EntityType);
            if (refused != null) return refused;

            if (!type!.Exportable)
            {
                return BadRequest(new { error = "ExportNotAvailable", message = $"{type.TypeName} has no list to export." });
            }

            var ids = request!.Ids?.Distinct().ToList();
            if (ids != null && ids.Count > DashboardActionService.MaxExportRows)
            {
                return BadRequest(new
                {
                    error = "TooManyIds",
                    message = $"At most {DashboardActionService.MaxExportRows} rows per export."
                });
            }

            return Ok(await _actions.ExportAsync(type, ids, request.Query, ActorId()));
        }

        /// <summary>The registry entry, or the answer: 400 without a type, 404 for an unknown one, 403 without its area node.</summary>
        private async Task<(DashboardTypeAccess? Type, IActionResult? Refused)> ResolveTypeAsync(string? entityType)
        {
            if (string.IsNullOrWhiteSpace(entityType))
            {
                return (null, BadRequest(new { error = "EntityTypeRequired", message = "Name the entity type." }));
            }
            var type = DashboardAccess.Find(entityType);
            if (type == null)
            {
                return (null, NotFound(new { error = "UnknownEntityType", message = "Not a dashboard entity type." }));
            }
            var check = await _permissions.CheckAsync(ActorId(), type.AreaNode);
            if (check?.Allowed != true)
            {
                return (null, PluginServiceAuth.Forbidden($"Requires the {type.AreaNode} permission."));
            }
            return (type, null);
        }

        // RequirePermission has already refused a caller without a web login.
        private int ActorId() => RequirePermissionFilter.UserIdFrom(User)!.Value;
    }
}

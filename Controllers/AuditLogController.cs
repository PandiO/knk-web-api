using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Read path for AuditLogEntry (docs/specs/user-management/IMPLEMENTATION_PLAN.md Phase 2).
    /// Explicit route rather than the usual api/[controller] convention, since the plan's own
    /// spelling is "audit-log" (kebab), not the controller name's PascalCase "AuditLog".
    /// </summary>
    [ApiController]
    [Route("api/audit-log")]
    // Staff only (developer request 2026-09-26): the log lists every player's moderation history.
    [RequirePermission(StaffPermissions.ManageUsers)]
    public class AuditLogController : ControllerBase
    {
        private readonly IAuditLogService _service;

        public AuditLogController(IAuditLogService service)
        {
            _service = service;
        }

        /// <param name="targetUserId">Filter to entries about this player.</param>
        /// <param name="actorUserId">Filter to entries made by this actor.</param>
        /// <param name="action">Filter to one AuditAction (e.g. TitleChanged).</param>
        /// <param name="direction">
        /// For action=TitleChanged only: "promotion" or "demotion" — the moderation view's
        /// "recently demoted players" filter (IMPLEMENTATION_PLAN.md Phase 3).
        /// </param>
        /// <param name="subjectType">
        /// Filter to entries about one kind of subject that is not a player (PandiO/knk-workspace#50), e.g.
        /// "PermissionGroup"; with subjectId, one record's history (a group's History list).
        /// </param>
        /// <param name="subjectId">Filter to entries about the subject with this id.</param>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Page size, capped at 100.</param>
        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] int? targetUserId,
            [FromQuery] int? actorUserId,
            [FromQuery] AuditAction? action,
            [FromQuery] string? direction,
            [FromQuery] string? subjectType,
            [FromQuery] int? subjectId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            if (subjectType != null && (string.IsNullOrWhiteSpace(subjectType) || subjectType.Length > AuditSubjectTypes.MaxLength))
                return BadRequest(new { message = "subjectType is invalid." });

            var result = subjectType == null && subjectId == null
                ? await _service.SearchAsync(targetUserId, actorUserId, action, direction, pageNumber, pageSize)
                : await _service.SearchAsync(targetUserId, actorUserId, action, direction, subjectType?.Trim(), subjectId, pageNumber, pageSize);
            return Ok(result);
        }
    }
}

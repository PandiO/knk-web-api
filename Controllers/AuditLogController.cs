using Microsoft.AspNetCore.Mvc;
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
    public class AuditLogController : ControllerBase
    {
        private readonly IAuditLogService _service;

        public AuditLogController(IAuditLogService service)
        {
            _service = service;
        }

        /// <param name="targetUserId">Filter to entries about this player.</param>
        /// <param name="actorUserId">Filter to entries made by this actor.</param>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Page size, capped at 100.</param>
        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] int? targetUserId,
            [FromQuery] int? actorUserId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var result = await _service.SearchAsync(targetUserId, actorUserId, pageNumber, pageSize);
            return Ok(result);
        }
    }
}

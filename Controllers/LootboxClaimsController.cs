using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// The lootbox drop log (docs/specs/lootboxes/DESIGN.md §3.3, IMPLEMENTATION_PLAN.md Phase 2): delivery
    /// confirmation and pending redelivery for the plugin, staff gives, and paged reads for the web app's admin page.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LootboxClaimsController : ControllerBase
    {
        private readonly ILootboxRuntimeService _service;

        public LootboxClaimsController(ILootboxRuntimeService service)
        {
            _service = service;
        }

        /// <summary>The plugin handed the item over. Idempotent: a repeat keeps the first confirmation.</summary>
        [HttpPost("{id:int}/delivered")]
        [RequirePluginService]
        public async Task<IActionResult> Delivered(int id, [FromBody] LootboxDeliveredRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () => Ok(await _service.MarkDeliveredAsync(id, request)));
        }

        /// <summary>Undelivered claims older than 30 s, for delivery on join (same payload as the claim result).</summary>
        [HttpGet("pending")]
        [RequirePluginService]
        public async Task<IActionResult> Pending([FromQuery] int userId)
        {
            return await LootboxResults.Run(this, async () => Ok(await _service.GetPendingAsync(userId)));
        }

        /// <summary><c>/knk lootbox give</c>: roll and mint without a world box; audited LootboxGranted; not counted
        /// against the daily cap.</summary>
        [HttpPost("admin-give")]
        [RequirePluginService]
        public async Task<IActionResult> AdminGive([FromBody] LootboxAdminGiveRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () =>
                Ok(await _service.AdminGiveAsync(request, HttpContext.GetKnkCaller().ActorUserId)));
        }

        [HttpGet("{id:int}")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetById(int id)
        {
            var claim = await _service.GetClaimAsync(id);
            if (claim == null) return NotFound();
            return Ok(claim);
        }

        /// <summary>The paged drop log. Filters: userId, lootboxTypeId, itemGradeId, boxGradeId, isSpecial, delivered,
        /// adminGive, from, to; searchTerm matches the player or the item name. Newest first by default.</summary>
        [HttpPost("search")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<ActionResult<PagedResultDto<LootboxClaimLogDto>>> Search([FromBody] PagedQueryDto query)
        {
            if (query == null) return BadRequest();
            return Ok(await _service.SearchClaimsAsync(query));
        }
    }
}

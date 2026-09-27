using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Lootbox token items (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 5): the plugin issues tokens (staff give,
    /// future PvP/referral drops), hands them over, and redeems one when a player opens the item; the web app lists and
    /// revokes them. The staff member behind an action is the plugin's X-Acting-User-Id header, never a body field.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LootboxTokensController : ControllerBase
    {
        private readonly ILootboxRuntimeService _service;

        public LootboxTokensController(ILootboxRuntimeService service)
        {
            _service = service;
        }

        /// <summary>200 <see cref="LootboxTokenIssueResultDto"/> (<c>replay=true</c> for a repeated idempotency key);
        /// 409 <c>{code: EmptyPool|NoBoxGrade|IdempotencyKeyReused}</c>. Audited LootboxGranted (TokensIssued).</summary>
        [HttpPost("issue")]
        [RequirePluginService]
        public async Task<IActionResult> Issue([FromBody] LootboxTokenIssueRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () =>
                Ok(await _service.IssueTokensAsync(request, HttpContext.GetKnkCaller().ActorUserId)));
        }

        /// <summary>
        /// Opens a token item. 200 <see cref="LootboxClaimResultDto"/> (<c>replay=true</c> for a retry of the same
        /// idempotency key); 409 <c>{code: InvalidToken|AlreadyRedeemed|Revoked|Disabled|Frozen|UserInactive|EmptyPool|IdempotencyKeyReused}</c>;
        /// 429 <c>{code: DailyLimit, scope, limit, resetsAt}</c>.
        /// </summary>
        [HttpPost("{token:guid}/redeem")]
        [RequirePluginService]
        public async Task<IActionResult> Redeem(Guid token, [FromBody] LootboxTokenRedeemRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () => Ok(await _service.RedeemTokenAsync(token, request)));
        }

        /// <summary>The user's issued tokens the plugin hasn't handed over yet (on join, and after a
        /// LootboxTokensIssued notification).</summary>
        [HttpGet("undelivered")]
        [RequirePluginService]
        public async Task<IActionResult> Undelivered([FromQuery] int userId)
        {
            return await LootboxResults.Run(this, async () => Ok(await _service.GetUndeliveredTokensAsync(userId)));
        }

        /// <summary>The plugin handed these token items to the player. Idempotent.</summary>
        [HttpPost("delivered")]
        [RequirePluginService]
        public async Task<IActionResult> Delivered([FromBody] LootboxTokensDeliveredRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () => Ok(await _service.MarkTokensDeliveredAsync(request)));
        }

        /// <summary>What became of the given tokens (the plugin's join scan removes revoked and opened copies).</summary>
        [HttpPost("status")]
        [RequirePluginService]
        public async Task<IActionResult> Status([FromBody] LootboxTokenStatusRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () => Ok(await _service.GetTokenStatusesAsync(request)));
        }

        /// <summary>An unopened token can no longer be opened. The game server removes every online copy within seconds
        /// (LootboxWorldChanged) and offline copies at the holder's next join. 409 AlreadyRedeemed.</summary>
        [HttpPost("{token:guid}/revoke")]
        [RequireServiceOrPermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> Revoke(Guid token)
        {
            return await LootboxResults.Run(this, async () =>
                Ok(await _service.RevokeTokenAsync(token, HttpContext.GetKnkCaller().ActorUserId)));
        }

        /// <summary>Paged tokens. Filters: userId, lootboxTypeId, status, reason, delivered; searchTerm = a token id or
        /// part of a username. Newest first by default.</summary>
        [HttpPost("search")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<ActionResult<PagedResultDto<LootboxTokenDto>>> Search([FromBody] PagedQueryDto query)
        {
            if (query == null) return BadRequest();
            return Ok(await _service.SearchTokensAsync(query));
        }
    }
}

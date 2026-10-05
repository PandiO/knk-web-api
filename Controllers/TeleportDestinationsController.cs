using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Domain warps and teleport fees for the game server (docs/specs/teleport/DESIGN.md §3.7.3,
/// KNG-17 Phase 5). All game-server only: the plugin asks for a player's destinations, then -
/// after the warmup - asks to charge (which re-checks everything server-side), and refunds when
/// the teleport it paid for didn't happen.
/// </summary>
[ApiController]
[Route("api/teleport-destinations")]
public class TeleportDestinationsController : ControllerBase
{
    private readonly ITeleportDestinationService _service;

    public TeleportDestinationsController(ITeleportDestinationService service)
    {
        _service = service;
    }

    /// <summary>
    /// Every warp destination with its lock state for the player: price, required title/premium
    /// tier/discovery, and the first requirement they don't meet. Domains that are disabled, have
    /// no location or don't allow entry are not listed.
    /// </summary>
    /// <response code="200">The destinations (possibly empty)</response>
    /// <response code="401">Not the game server (no or wrong X-API-Key)</response>
    /// <response code="403">A logged-in web user</response>
    /// <response code="404">User not found</response>
    [RequirePluginService]
    [HttpGet]
    [ProducesResponseType(typeof(List<TeleportDestinationDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<List<TeleportDestinationDto>>> List([FromQuery] int userId)
    {
        try
        {
            return Ok(await _service.ListForUserAsync(userId));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
        }
    }

    /// <summary>
    /// Authorizes a warp to the domain for the player and charges its gem price (ledger reason
    /// TELEPORT_FEE), after re-evaluating title, premium tier and discovery requirements. Called
    /// after the warmup for every warp, free ones too. Idempotent per idempotencyKey: a retry
    /// returns the first charge with replayed=true and takes nothing more.
    /// </summary>
    /// <response code="200">Allowed (and charged); carries the destination to teleport to</response>
    /// <response code="400">Bad idempotency key</response>
    /// <response code="404">User not found</response>
    /// <response code="409">Refused - error is NotAvailable, TitleTooLow, PremiumTooLow, NotDiscovered,
    /// InsufficientGems, Refunded or IdempotencyKeyReuse; message is for the player</response>
    [RequirePluginService]
    [HttpPost("{domainId:int}/charge")]
    [ProducesResponseType(typeof(TeleportChargeResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Charge(int domainId, [FromBody] TeleportChargeRequestDto request) =>
        Run(request?.UserId, () => _service.ChargeAsync(domainId, request!));

    /// <summary>
    /// Charges the coin fee of a /tpa or /tpahere (the plugin's teleport.request.price-coins) to the
    /// requester when the teleport commits. Idempotent per idempotencyKey.
    /// </summary>
    /// <response code="200">Charged</response>
    /// <response code="400">Bad key or amount</response>
    /// <response code="404">User not found</response>
    /// <response code="409">InsufficientCoins, Refunded or IdempotencyKeyReuse</response>
    [RequirePluginService]
    [HttpPost("request-fee")]
    [ProducesResponseType(typeof(TeleportChargeResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> RequestFee([FromBody] TeleportRequestFeeDto request) =>
        Run(request?.UserId, () => _service.ChargeRequestFeeAsync(request!));

    /// <summary>
    /// Charges the flat coin fee of a player's own /back (the plugin's teleport.back.price-coins) when
    /// the teleport commits (KNG-42). Idempotent per idempotencyKey.
    /// </summary>
    /// <response code="200">Charged</response>
    /// <response code="400">Bad key, amount or backKind</response>
    /// <response code="404">User not found</response>
    /// <response code="409">InsufficientCoins, Refunded or IdempotencyKeyReuse</response>
    [RequirePluginService]
    [HttpPost("back-fee")]
    [ProducesResponseType(typeof(TeleportChargeResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> BackFee([FromBody] TeleportBackFeeDto request) =>
        Run(request?.UserId, () => _service.ChargeBackFeeAsync(request!));

    /// <summary>
    /// Refunds the charge made under idempotencyKey (a ledger reversal) because the teleport didn't
    /// happen. Safe to repeat. With no charge under the key, nothing is refunded and the key is
    /// voided so a late charge with it is refused.
    /// </summary>
    /// <response code="200">See refunded / replayed</response>
    /// <response code="400">Bad key</response>
    /// <response code="404">User not found</response>
    /// <response code="409">The key belongs to another player or isn't a teleport fee</response>
    [RequirePluginService]
    [HttpPost("refund")]
    [ProducesResponseType(typeof(TeleportRefundResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Refund([FromBody] TeleportRefundRequestDto request) =>
        Run(request?.UserId, () => _service.RefundAsync(request!));

    private async Task<IActionResult> Run<T>(int? userId, Func<Task<T>> action)
    {
        if (userId == null)
        {
            return BadRequest(new { error = "ValidationFailed", message = "A request body is required." });
        }
        try
        {
            return Ok(await action());
        }
        catch (TeleportDestinationException ex)
        {
            return Conflict(new { error = ex.Code, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "ValidationFailed", message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
        }
    }
}

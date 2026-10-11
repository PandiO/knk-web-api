using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Privacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// GDPR data deletion for players and staff (KNG-34, DESIGN.md §F.14, developer decisions
/// 2026-10-03). A signed-in player requests deletion of their own data and confirms it through an
/// emailed link; staff with knk.admin.privacy.request file the same request for a player without the
/// email step. Either way the deletion runs after a grace period (Privacy:GraceDays, 5) during which
/// the player or staff can cancel it. The owner's overview and early execution live in
/// <see cref="PrivacyController"/>.
/// </summary>
[ApiController]
[Route("api/data-deletion")]
public class DataDeletionController : ControllerBase
{
    private readonly IPrivacyDeletionService _privacy;

    public DataDeletionController(IPrivacyDeletionService privacy)
    {
        _privacy = privacy;
    }

    // ------------------------------------------------------------------ the signed-in player

    /// <summary>The caller's open request (awaiting confirmation or scheduled); 204 when there is none.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> GetMine(CancellationToken ct)
    {
        if (SelfId() is not int userId) return Unauthorized();
        var result = await _privacy.GetOpenRequestOfUserAsync(userId, ct);
        return result.Outcome == PrivacyOutcome.Ok ? Ok(ForPlayer(result.Request!)) : NoContent();
    }

    /// <summary>
    /// Requests deletion of the caller's data: emails a confirmation link (valid
    /// Privacy:ConfirmationHours). Asking again while unconfirmed sends a fresh link.
    /// </summary>
    /// <response code="202">Confirmation email sent (status AwaitingConfirmation)</response>
    /// <response code="409">AlreadyScheduled (body: the request) or EmailRequired</response>
    /// <response code="502">EmailFailed — try again later</response>
    [Authorize]
    [HttpPost("me")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 202)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(502)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> RequestMine(CancellationToken ct)
    {
        if (SelfId() is not int userId) return Unauthorized();
        var result = await _privacy.RequestOwnAsync(userId, ct);
        return result.Outcome switch
        {
            PrivacyOutcome.Ok => StatusCode(StatusCodes.Status202Accepted, ForPlayer(result.Request!)),
            PrivacyOutcome.AlreadyPending => Conflict(new { error = "AlreadyScheduled", message = "Your data deletion is already scheduled.", request = ForPlayer(result.Request!) }),
            PrivacyOutcome.EmailRequired => Conflict(new { error = "EmailRequired", message = "Add an email address to your account first: the request is confirmed by email." }),
            PrivacyOutcome.EmailFailed => StatusCode(StatusCodes.Status502BadGateway, new { error = "EmailFailed", message = "The confirmation email could not be sent. Try again later." }),
            _ => NotFound(new { error = "UserNotFound", message = "Account not found." })
        };
    }

    /// <summary>Cancels the caller's open request (unconfirmed or still in its grace period).</summary>
    [Authorize]
    [HttpPost("me/cancel")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> CancelMine(CancellationToken ct)
    {
        if (SelfId() is not int userId) return Unauthorized();
        var result = await _privacy.CancelOwnAsync(userId, ct);
        return result.Outcome switch
        {
            PrivacyOutcome.Ok => Ok(ForPlayer(result.Request!)),
            PrivacyOutcome.NotPending => Conflict(new { error = "NotPending", message = "This request can no longer be cancelled." }),
            _ => NotFound(new { error = "NoOpenRequest", message = "You have no open data deletion request." })
        };
    }

    /// <summary>
    /// Confirms a player's request with the token from the emailed link (no sign-in needed: the link
    /// proves access to the account's mailbox). The deletion is scheduled after the grace period.
    /// </summary>
    /// <response code="400">InvalidToken — unknown, already used or expired link</response>
    [AllowAnonymous]
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> Confirm([FromBody] PrivacyDeletionConfirmDto? body, CancellationToken ct)
    {
        var result = await _privacy.ConfirmAsync(body?.Token ?? "", ct);
        return result.Outcome == PrivacyOutcome.Ok
            ? Ok(ForPlayer(result.Request!))
            : BadRequest(new { error = "InvalidToken", message = "This confirmation link is invalid, already used or expired. Request the deletion again from your account page." });
    }

    // ------------------------------------------------------------------ staff, for a player

    /// <summary>The player's open request; 204 when there is none.</summary>
    [RequirePermission(StaffPermissions.RequestDataDeletion)]
    [HttpGet("users/{userId:int}")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> GetForPlayer(int userId, CancellationToken ct)
    {
        var result = await _privacy.GetOpenRequestOfUserAsync(userId, ct);
        return result.Outcome == PrivacyOutcome.Ok ? Ok(result.Request) : NoContent();
    }

    /// <summary>
    /// Files a deletion request on the player's behalf, without email confirmation. It runs after the
    /// grace period; the player is emailed (when the account has an address) and can still cancel.
    /// </summary>
    /// <response code="201">Scheduled</response>
    /// <response code="409">AlreadyScheduled — the body is the scheduled request</response>
    [RequirePermission(StaffPermissions.RequestDataDeletion)]
    [HttpPost("users/{userId:int}")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> FileForPlayer(int userId, [FromBody] PrivacyDeletionRequestCreateDto? body,
        CancellationToken ct)
    {
        if (body?.Note?.Length > 500) return BadRequest(new { error = "InvalidRequest", message = "note may be at most 500 characters." });
        if (CallerId() is not int staffId) return Unauthorized();
        var result = await _privacy.FileForPlayerAsync(staffId, PrivacyRequestSource.Staff, userId, body?.Note, ct);
        return result.Outcome switch
        {
            PrivacyOutcome.Ok => StatusCode(StatusCodes.Status201Created, result.Request),
            PrivacyOutcome.AlreadyPending => Conflict(new { error = "AlreadyScheduled", message = "This player's data deletion is already scheduled.", request = result.Request }),
            _ => NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" })
        };
    }

    /// <summary>Cancels the player's open request.</summary>
    [RequirePermission(StaffPermissions.RequestDataDeletion)]
    [HttpPost("users/{userId:int}/cancel")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> CancelForPlayer(int userId, CancellationToken ct)
    {
        if (CallerId() is not int staffId) return Unauthorized();
        var open = await _privacy.GetOpenRequestOfUserAsync(userId, ct);
        if (open.Outcome != PrivacyOutcome.Ok) return NotFound(new { error = "NoOpenRequest", message = "This player has no open data deletion request." });
        var result = await _privacy.CancelAsync(staffId, open.Request!.Id, ct);
        return result.Outcome == PrivacyOutcome.Ok
            ? Ok(result.Request)
            : Conflict(new { error = "NotPending", message = "This request can no longer be cancelled.", request = result.Request });
    }

    /// <summary>What the player sees of their own request: no staff note, no removal counts.</summary>
    private static PrivacyDeletionRequestDto ForPlayer(PrivacyDeletionRequestDto dto)
    {
        dto.Note = null;
        dto.Result = null;
        return dto;
    }

    private int? SelfId() => HttpContext.GetKnkCaller().WebUserId;

    private int? CallerId()
    {
        var caller = HttpContext.GetKnkCaller();
        return caller.IsWebUser ? caller.WebUserId : caller.ActingUserId;
    }
}

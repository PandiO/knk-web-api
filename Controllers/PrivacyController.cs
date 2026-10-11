using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Privacy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// GDPR deletion requests, owner view (KNG-34 D12, knk-workspace docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §3.3, DESIGN.md §F.14). Owner only (exact grant of
/// knk.owner.privacy.manage). Players request on the web app and staff file for players through
/// <see cref="DataDeletionController"/>; requests run automatically after the grace period.
/// Execution is irreversible: preview it with <c>?dryRun=true</c>.
/// </summary>
[ApiController]
[Route("api/privacy")]
[RequireOwnerPermission(OwnerPermissions.PrivacyManage)]
public class PrivacyController : ControllerBase
{
    private readonly IPrivacyDeletionService _privacy;

    public PrivacyController(IPrivacyDeletionService privacy)
    {
        _privacy = privacy;
    }

    /// <summary>Requests, newest first; optionally only one status (Pending, Completed, Cancelled,
    /// AwaitingConfirmation, Expired).</summary>
    [HttpGet("deletion-requests")]
    [ProducesResponseType(typeof(List<PrivacyDeletionRequestDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<List<PrivacyDeletionRequestDto>>> GetRequests([FromQuery] string? status, CancellationToken ct)
    {
        PrivacyRequestStatus? parsed = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<PrivacyRequestStatus>(status, true, out var s) || int.TryParse(status, out _))
            {
                return BadRequest(new { error = "InvalidStatus", message = "status must be Pending, Completed, Cancelled, AwaitingConfirmation or Expired." });
            }
            parsed = s;
        }
        return Ok(await _privacy.GetRequestsAsync(parsed, ct));
    }

    [HttpGet("deletion-requests/{id:int}")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> GetRequest(int id, CancellationToken ct) =>
        Map(await _privacy.GetRequestAsync(id, ct), id);

    /// <summary>Files a request for a player (no email confirmation); it runs after Privacy:GraceDays (5).</summary>
    /// <response code="201">Scheduled</response>
    /// <response code="404">UserNotFound</response>
    /// <response code="409">PendingRequestExists — the body is the scheduled request</response>
    [HttpPost("deletion-requests")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> CreateRequest([FromBody] PrivacyDeletionRequestCreateDto? request,
        CancellationToken ct)
    {
        if (request == null || request.UserId <= 0) return BadRequest(new { error = "InvalidRequest", message = "userId is required." });
        if (request.Note?.Length > 500) return BadRequest(new { error = "InvalidRequest", message = "note may be at most 500 characters." });
        var result = await _privacy.FileForPlayerAsync(OwnerId(), PrivacyRequestSource.Owner, request.UserId, request.Note, ct);
        return result.Outcome switch
        {
            PrivacyOutcome.Ok => StatusCode(StatusCodes.Status201Created, result.Request),
            PrivacyOutcome.AlreadyPending => Conflict(new { error = "PendingRequestExists", message = "This player's data deletion is already scheduled.", request = result.Request }),
            _ => NotFound(new { error = "UserNotFound", message = $"User with ID {request.UserId} not found" })
        };
    }

    /// <summary>
    /// Executes a scheduled request whose grace period is over (the hourly job does this
    /// automatically): deletes the player's data (DESIGN.md §F.14) and pseudonymizes their account
    /// (and accounts merged into it). Irreversible. With dryRun=true only the counts are returned
    /// (any open request). Executing a completed request again returns its stored result.
    /// </summary>
    /// <response code="409">NotPending — cancelled, expired or unconfirmed; GracePeriod — the player can still cancel</response>
    [HttpPost("deletion-requests/{id:int}/execute")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> Execute(int id, [FromQuery] bool dryRun = false, CancellationToken ct = default) =>
        Map(await _privacy.ExecuteAsync(id, OwnerId(), dryRun, ct), id);

    /// <summary>Cancels an open request (idempotent for a cancelled one).</summary>
    /// <response code="409">NotPending — the request was already executed or expired</response>
    [HttpPost("deletion-requests/{id:int}/cancel")]
    [ProducesResponseType(typeof(PrivacyDeletionRequestDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<PrivacyDeletionRequestDto>> Cancel(int id, CancellationToken ct) =>
        Map(await _privacy.CancelAsync(OwnerId(), id, ct), id);

    private ActionResult<PrivacyDeletionRequestDto> Map(PrivacyResult result, int id) => result.Outcome switch
    {
        PrivacyOutcome.Ok => Ok(result.Request),
        PrivacyOutcome.NotPending => Conflict(new { error = "NotPending", message = $"Request {id} is {result.Request?.Status}.", request = result.Request }),
        PrivacyOutcome.GracePeriod => Conflict(new { error = "GracePeriod", message = $"Request {id} runs at {result.Request?.ScheduledAt:u}; until then the player can cancel it.", request = result.Request }),
        _ => NotFound(new { error = "RequestNotFound", message = $"No deletion request {id}." })
    };

    private int OwnerId()
    {
        var caller = HttpContext.GetKnkCaller();
        return (caller.IsWebUser ? caller.WebUserId : caller.ActingUserId) ?? 0;
    }
}

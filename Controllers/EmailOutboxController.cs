using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Services.Email;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// The mail log behind /admin/email (KNG-206, docs/specs/invitations/DESIGN.md § 4.2). Staff with
/// knk.admin.config only. Recipients are masked and bodies are never returned.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EmailOutboxController : ControllerBase
{
    private readonly IEmailOutboxAdminService _service;

    public EmailOutboxController(IEmailOutboxAdminService service)
    {
        _service = service;
    }

    /// <summary>Newest first; filter by status (Pending, Sent, Failed, Expired, Suppressed) and kind.</summary>
    [HttpGet]
    [RequirePermission(StaffPermissions.ServerConfig)]
    [ProducesResponseType(typeof(EmailOutboxPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? status = null, [FromQuery] string? kind = null, CancellationToken ct = default) =>
        Ok(await _service.ListAsync(pageNumber, pageSize, status, kind, ct));

    /// <summary>Queues a test mail to the caller's own address. 400 when the caller has no email.</summary>
    [HttpPost("test")]
    [RequirePermission(StaffPermissions.ServerConfig)]
    [ProducesResponseType(typeof(EmailOutboxItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendTest(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return BadRequest(new { error = "NoUser", message = "Log in with a user account to send a test mail." });
        }
        var result = await _service.SendTestAsync(userId.Value, ct);
        return result.Outcome switch
        {
            EmailOutboxActionOutcome.Ok => Ok(result.Item),
            EmailOutboxActionOutcome.NoEmail => BadRequest(new { error = "NoEmail", message = result.Message }),
            _ => BadRequest(new { error = "NoUser", message = result.Message })
        };
    }

    /// <summary>Puts a Failed mail back in the queue. 409 when it isn't Failed or its content was removed.</summary>
    [HttpPost("{id:long}/retry")]
    [RequirePermission(StaffPermissions.ServerConfig)]
    [ProducesResponseType(typeof(EmailOutboxItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(long id, CancellationToken ct = default)
    {
        var result = await _service.RetryAsync(id, ct);
        return result.Outcome switch
        {
            EmailOutboxActionOutcome.Ok => Ok(result.Item),
            EmailOutboxActionOutcome.NotFound => NotFound(new { error = "NotFound", message = result.Message }),
            _ => Conflict(new { error = "NotRetryable", message = result.Message })
        };
    }
}

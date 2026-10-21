using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Services.Invitations;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Invitations behind /admin/invitations and the anonymous check of the sign-up pages (KNG-212,
/// knk-workspace docs/specs/invitations/DESIGN.md § 3, § 6). Every staff route needs knk.admin.invites.
/// Raw links and codes are returned once (create, resend, rotate) and never logged.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InvitationsController : ControllerBase
{
    private readonly IInvitationService _service;
    private readonly knkwebapi_v2.Services.CloudflareAccess.ICloudflareAccessSync? _accessSync;

    public InvitationsController(IInvitationService service, knkwebapi_v2.Services.CloudflareAccess.ICloudflareAccessSync? accessSync = null)
    {
        _service = service;
        _accessSync = accessSync;
    }

    /// <summary>The Cloudflare Access sync's state (KNG-216): last success, last error, pending changes.</summary>
    [HttpGet("access-sync")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(CloudflareAccessSyncStatusDto), StatusCodes.Status200OK)]
    public IActionResult GetAccessSyncStatus() =>
        Ok(_accessSync?.GetStatus() ?? new CloudflareAccessSyncStatusDto());

    /// <summary>Newest first; filter by status (Active, Paused, Exhausted, Expired, Revoked) and kind (Personal, Shared).</summary>
    [HttpGet]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(InvitationPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? status = null, [FromQuery] string? kind = null) =>
        Ok(await _service.ListAsync(pageNumber, pageSize, status, kind));

    /// <summary>The invitable groups and the configured defaults and caps, for the create forms.</summary>
    [HttpGet("options")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(InvitationOptionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOptions() => Ok(await _service.GetOptionsAsync());

    /// <summary>One personal invite per address; addresses already in use are skipped with a reason.</summary>
    [HttpPost("personal")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(CreatePersonalInvitationsResponseDto), StatusCodes.Status200OK)]
    public Task<IActionResult> CreatePersonal([FromBody] CreatePersonalInvitationsRequestDto request) =>
        Run(actor => _service.CreatePersonalAsync(actor, request));

    [HttpPost("shared")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(SharedInvitationResultDto), StatusCodes.Status200OK)]
    public Task<IActionResult> CreateShared([FromBody] CreateSharedInvitationRequestDto request) =>
        Run(actor => _service.CreateSharedAsync(actor, request));

    /// <summary>Personal only: a new link and a fresh expiry; the old link stops working. 429 over Invitations:ResendPerDay.</summary>
    [HttpPost("{id:int}/resend")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(PersonalInvitationResultDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Resend(int id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ResendInvitationRequestDto? request) =>
        Run(actor => _service.ResendAsync(actor, id, request?.SendEmail ?? true));

    /// <summary>Shared only: a new code with the same counters; the old code stops working.</summary>
    [HttpPost("{id:int}/rotate")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(SharedInvitationResultDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Rotate(int id) => Run(actor => _service.RotateAsync(actor, id));

    [HttpPost("{id:int}/pause")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(InvitationDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Pause(int id) => Run(actor => _service.PauseAsync(actor, id));

    [HttpPost("{id:int}/resume")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(InvitationDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Resume(int id) => Run(actor => _service.ResumeAsync(actor, id));

    [HttpPost("{id:int}/revoke")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(InvitationDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Revoke(int id) => Run(actor => _service.RevokeAsync(actor, id));

    /// <summary>Who used the invite: user id, name, masked email, when, and whether Minecraft is linked yet.</summary>
    [HttpGet("{id:int}/redemptions")]
    [RequirePermission(StaffPermissions.ManageInvites)]
    [ProducesResponseType(typeof(List<InvitationRedemptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRedemptions(int id) => ToResult(await _service.GetRedemptionsAsync(id));

    /// <summary>
    /// For /invite/:token and /join: is this token or code usable now? Always 200; every failure is
    /// <c>{ valid: false, error: "InvalidInvite" }</c>, so a revoked code looks like a wrong one.
    /// </summary>
    [HttpPost("check")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.AuthPolicy)]
    [ProducesResponseType(typeof(CheckInvitationResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Check([FromBody] CheckInvitationRequestDto request) =>
        Ok(await _service.CheckAsync(request?.Token, request?.Code));

    private async Task<IActionResult> Run<T>(Func<int, Task<InvitationOutcome<T>>> action)
    {
        var actor = User.GetUserId();
        if (actor == null)
        {
            return BadRequest(new { error = "NoUser", message = "Log in with a user account to manage invitations." });
        }
        return ToResult(await action(actor.Value));
    }

    private IActionResult ToResult<T>(InvitationOutcome<T> outcome)
    {
        if (outcome.Ok)
        {
            return Ok(outcome.Value);
        }
        var body = new { error = outcome.Error, message = outcome.Message };
        return outcome.Error switch
        {
            InvitationErrors.NotFound => NotFound(body),
            InvitationErrors.Escalation => StatusCode(StatusCodes.Status403Forbidden, body),
            InvitationErrors.WrongKind or InvitationErrors.NotActive => Conflict(body),
            InvitationErrors.ResendLimit => StatusCode(StatusCodes.Status429TooManyRequests, body),
            _ => BadRequest(body)
        };
    }
}

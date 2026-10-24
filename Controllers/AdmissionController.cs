using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// The game server's admission gate and the claim codes it hands out (KNG-214, knk-workspace
/// docs/specs/invitations/DESIGN.md § 5.2, plan § 6.1). Claim codes are never logged.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AdmissionController : ControllerBase
{
    private readonly IAdmissionService _service;

    public AdmissionController(IAdmissionService service)
    {
        _service = service;
    }

    /// <summary>
    /// knk-plugin at pre-login, for a Mojang-verified player: <c>{admitted: true}</c>, or
    /// <c>{admitted: false, reason: "NotAdmitted", code, expiresAt}</c> with the claim code for the
    /// disconnect screen, or <c>{admitted: false, reason: "Deactivated"}</c>. Always admitted while
    /// Security:Registration:Mode isn't Invite.
    /// </summary>
    /// <response code="400">Not a UUID, or not a Minecraft name.</response>
    [HttpGet("{uuid}")]
    [RequirePluginService]
    [ProducesResponseType(typeof(AdmissionResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Check(string uuid, [FromQuery] string? name)
    {
        try
        {
            return Ok(await _service.CheckAsync(uuid, name));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "InvalidRequest", message = ex.Message });
        }
    }

    /// <summary>
    /// For /link before login: is this claim code active, and for which Minecraft name? Always 200;
    /// a wrong, used or expired code is <c>{valid: false}</c>. Counts against the per-IP lookup limit.
    /// </summary>
    [HttpPost("claim/check")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LookupPolicy)]
    [ProducesResponseType(typeof(ClaimCheckResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckClaim([FromBody] ClaimCheckRequestDto? request) =>
        Ok(await _service.CheckClaimAsync(request?.Code));
}

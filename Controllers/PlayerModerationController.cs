using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Kick and ban from the web app (PandiO/knk-workspace#565, developer decisions 2026-11-15). Web staff only: the game
    /// server has its own <c>/kick</c> and <c>/ban</c>. Each action is audited and published to the game servers as the
    /// realtime event <c>PlayerKick</c> / <c>PlayerBan</c>; knk-plugin carries it out (#574). Unban stays <c>/pardon</c>
    /// in game. Same routes as <see cref="UsersController"/> (<c>api/Users/{id}/…</c>).
    /// </summary>
    [ApiController]
    [Route("api/Users")]
    public class PlayerModerationController : ControllerBase
    {
        private readonly IPlayerModerationService _moderation;

        public PlayerModerationController(IPlayerModerationService moderation)
        {
            _moderation = moderation;
        }

        /// <summary>Kick an online player (reason optional, at most 200 characters).</summary>
        /// <response code="202">Audited and sent to the game servers</response>
        /// <response code="400">ValidationFailed (reason too long), CannotModerateSelf, NoMinecraftAccount</response>
        /// <response code="401">Not logged in</response>
        /// <response code="403">No knk.kick, or EscalationRefused (the player ranks at or above you)</response>
        /// <response code="404">UserNotFound</response>
        /// <response code="409">PlayerNotOnline</response>
        /// <response code="503">NoGameServerConnected</response>
        [RequirePermission(StaffPermissions.Kick)]
        [HttpPost("{id:int}/kick")]
        [ProducesResponseType(typeof(PlayerModerationResultDto), StatusCodes.Status202Accepted)]
        public Task<IActionResult> Kick(int id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PlayerModerationRequestDto? request = null) =>
            Run((actor) => _moderation.KickAsync(id, request?.Reason, actor));

        /// <summary>Ban a player permanently, online or not (reason required, at most 200 characters).</summary>
        /// <response code="202">Audited and sent to the game servers</response>
        /// <response code="400">ValidationFailed (no reason, too long), CannotModerateSelf, NoMinecraftAccount</response>
        /// <response code="401">Not logged in</response>
        /// <response code="403">No knk.ban, or EscalationRefused (the player ranks at or above you)</response>
        /// <response code="404">UserNotFound</response>
        /// <response code="503">NoGameServerConnected</response>
        [RequirePermission(StaffPermissions.Ban)]
        [HttpPost("{id:int}/ban")]
        [ProducesResponseType(typeof(PlayerModerationResultDto), StatusCodes.Status202Accepted)]
        public Task<IActionResult> Ban(int id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PlayerModerationRequestDto? request = null) =>
            Run((actor) => _moderation.BanAsync(id, request?.Reason, actor));

        private async Task<IActionResult> Run(Func<int, Task<PlayerModerationResultDto>> action)
        {
            var actor = HttpContext.GetKnkCaller().WebUserId;
            if (actor == null)
            {
                return Unauthorized(new { error = "Unauthorized", message = "Log in to use this." });
            }
            try
            {
                return StatusCode(StatusCodes.Status202Accepted, await action(actor.Value));
            }
            catch (PlayerModerationException ex)
            {
                return ex.Refusal switch
                {
                    PlayerModerationRefusal.InvalidReason => BadRequest(new { error = "ValidationFailed", message = ex.Message }),
                    PlayerModerationRefusal.UserNotFound => NotFound(new { error = "UserNotFound", message = ex.Message }),
                    PlayerModerationRefusal.Self => BadRequest(new { error = "CannotModerateSelf", message = ex.Message }),
                    PlayerModerationRefusal.NoMinecraftAccount => BadRequest(new { error = "NoMinecraftAccount", message = ex.Message }),
                    PlayerModerationRefusal.Rank => StatusCode(StatusCodes.Status403Forbidden, new { error = "EscalationRefused", message = ex.Message }),
                    PlayerModerationRefusal.NotOnline => Conflict(new { error = "PlayerNotOnline", message = ex.Message }),
                    _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "NoGameServerConnected", message = ex.Message }),
                };
            }
        }
    }
}

using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Services.Map;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Overview map L7 (PandiO/knk-workspace#741, OVERVIEW_MAP_DESIGN § 5 rule 11, § 6.2.5, O17, O27): live gate commands
    /// from the web app (open, close, toggle, repair; one door or the whole gate). Web staff with <c>knk.gate.admin</c>
    /// only: the plugin executes the command, it never calls this route (in game there is <c>/gate</c>). Same route
    /// prefix as <c>GateStructuresController</c>. Off by default: with <c>Map:GateCommands:Enabled</c> false the route
    /// answers 404 and does nothing.
    /// </summary>
    [ApiController]
    [Route("api/GateStructures")]
    public class GateCommandsController : ControllerBase
    {
        private readonly IGateCommandService _commands;

        public GateCommandsController(IGateCommandService commands)
        {
            _commands = commands;
        }

        /// <summary>Run a gate command in game now, with the normal animation, and wait up to 5 s for the result.</summary>
        /// <response code="200">The game server answered: <c>result</c> <c>done</c> or <c>refused</c> (with a message)</response>
        /// <response code="400">ValidationFailed (unknown action), DoorNotOfGate</response>
        /// <response code="401">Not logged in</response>
        /// <response code="403">No knk.gate.admin</response>
        /// <response code="404">GateNotFound, or GateCommandsOff (Map:GateCommands:Enabled is off)</response>
        /// <response code="409">GateInSiege: a siege match runs on the gate</response>
        /// <response code="503">NoGameServerConnected</response>
        /// <response code="504">GateCommandTimeout: no result within the timeout (the game server may still act)</response>
        [RequirePermission(StaffPermissions.GateAdmin)]
        [HttpPost("{id:int}/commands")]
        [ProducesResponseType(typeof(GateCommandResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Send(int id, [FromBody] GateCommandRequestDto? request, CancellationToken ct = default)
        {
            var actor = HttpContext.GetKnkCaller().WebUserId;
            if (actor == null)
            {
                return Unauthorized(new { error = "Unauthorized", message = "Log in to use this." });
            }
            try
            {
                var result = await _commands.SendAsync(id, request ?? new GateCommandRequestDto(), actor.Value, ct);
                return result.Result switch
                {
                    GateCommandOutcomes.Timeout => StatusCode(StatusCodes.Status504GatewayTimeout, result),
                    GateCommandOutcomes.Siege => Conflict(result),
                    _ => Ok(result),
                };
            }
            catch (GateCommandException ex)
            {
                return ex.Refusal switch
                {
                    GateCommandRefusal.Off => NotFound(new { error = "GateCommandsOff", message = ex.Message }),
                    GateCommandRefusal.InvalidAction => BadRequest(new { error = "ValidationFailed", message = ex.Message }),
                    GateCommandRefusal.GateNotFound => NotFound(new { error = "GateNotFound", message = ex.Message }),
                    GateCommandRefusal.ForeignDoor => BadRequest(new { error = "DoorNotOfGate", message = ex.Message }),
                    GateCommandRefusal.Siege => Conflict(new { error = "GateInSiege", message = ex.Message }),
                    _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "NoGameServerConnected", message = ex.Message }),
                };
            }
        }
    }
}

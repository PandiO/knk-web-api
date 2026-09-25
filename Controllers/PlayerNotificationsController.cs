using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Polled by the plugin's PlayerNotificationPoller to pick up in-game moments (promotion
    /// effects) for writes the plugin didn't make itself. See IPlayerNotificationQueue.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PlayerNotificationsController : ControllerBase
    {
        private readonly IPlayerNotificationQueue _queue;

        public PlayerNotificationsController(IPlayerNotificationQueue queue)
        {
            _queue = queue;
        }

        /// <summary>Every unacknowledged, unexpired notification, oldest first.</summary>
        [HttpGet("pending")]
        public IActionResult GetPending()
        {
            return Ok(_queue.GetPending());
        }

        /// <summary>Removes the given notifications once the plugin has shown them.</summary>
        [HttpPost("acknowledge")]
        public IActionResult Acknowledge([FromBody] AcknowledgePlayerNotificationsDto request)
        {
            if (request?.Ids == null) return BadRequest(new { error = "ValidationFailed", message = "ids is required" });
            return Ok(new { acknowledged = _queue.Acknowledge(request.Ids) });
        }
    }
}

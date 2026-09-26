using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// The server-side private message log (KNG-18 Phase 3, docs/specs/private-messages/DESIGN.md
    /// §3.2). knk-plugin appends /msg and /reply messages in batches; staff read one player's
    /// messages (the web-app viewer is Phase 4). Rows are deleted after
    /// AuditLogRetentionConfiguration.PrivateMessageRetentionDays (default 30).
    /// </summary>
    [ApiController]
    [Route("api/private-message-log")]
    public class PrivateMessageLogController : ControllerBase
    {
        private readonly IPrivateMessageLogService _service;

        public PrivateMessageLogController(IPrivateMessageLogService service)
        {
            _service = service;
        }

        /// <summary>
        /// Appends up to 200 messages. Re-sending a batch is harmless: entries whose clientMessageId
        /// is already stored count as duplicates. Game server only, so nobody can forge log entries.
        /// </summary>
        /// <response code="200">{accepted, duplicates}</response>
        /// <response code="400">More than 200 entries, or a malformed entry (nothing stored)</response>
        [HttpPost("batch")]
        [RequirePluginService]
        [ProducesResponseType(typeof(PrivateMessageLogBatchResultDto), 200)]
        [ProducesResponseType(400)]
        public async Task<ActionResult<PrivateMessageLogBatchResultDto>> AddBatch([FromBody] List<CreatePrivateMessageLogEntryDto> entries)
        {
            try
            {
                return Ok(await _service.AddBatchAsync(entries));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidBatch", message = ex.Message });
            }
        }

        /// <summary>
        /// A player's private messages (sent and received), newest first. Each call writes a
        /// PrivateMessagesViewed audit entry naming the viewer and the player.
        /// <para>
        /// Logged-in staff holding knk.pmlog.read only (DESIGN.md §3.2/§3.6) - not the game server's
        /// key: the plugin never reads the log, and a key holder could otherwise read every PM with
        /// no audited viewer (or name any staff member as the viewer via X-Acting-User-Id).
        /// </para>
        /// </summary>
        /// <param name="participantUserId">The player whose messages to show (required).</param>
        /// <param name="otherUserId">Only the conversation with this player.</param>
        /// <param name="from">Sent at or after (UTC).</param>
        /// <param name="to">Sent before (UTC).</param>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Page size, capped at 100.</param>
        [HttpGet]
        [RequirePermission(StaffPermissions.ReadPrivateMessages)]
        [ProducesResponseType(typeof(PagedResultDto<PrivateMessageLogEntryDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<ActionResult<PagedResultDto<PrivateMessageLogEntryDto>>> Search(
            [FromQuery] int participantUserId,
            [FromQuery] int? otherUserId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var query = new PrivateMessageLogQueryDto
                {
                    ParticipantUserId = participantUserId,
                    OtherUserId = otherUserId,
                    From = from,
                    To = to,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
                return Ok(await _service.SearchAsync(query, HttpContext.GetKnkCaller().WebUserId));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidQuery", message = ex.Message });
            }
        }
    }
}

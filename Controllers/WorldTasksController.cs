using knkwebapi_v2.Attributes;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write needs knk.admin.world or the game server (POST .../search counts as a read);
    // reads stay logged-in. #180: claim/complete/fail/release/heartbeat and GET open are the game server's only.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads(DashboardArea.World)]
    public class WorldTasksController : ControllerBase
    {
        private readonly IWorldTaskService _service;

        public WorldTasksController(IWorldTaskService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WorldTaskCreateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                // #180: the creator is the logged-in caller (null for the game server: the session owner).
                var created = await _service.CreateAsync(dto, User.GetUserId());
                return CreatedAtRoute("GetWorldTaskById", new { id = created.Id }, created);
            }
            catch (GateScanNotReadyException ex)
            {
                // KNG-268: the form shows the message.
                return Conflict(new { code = "GateScanNotReady", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    error = "Invalid WorldTask creation request",
                    details = ex.Message,
                    hint = "Ensure StepNumber, StepKey, FieldName, and TaskType are all provided. These are critical for Minecraft plugin field routing."
                });
            }
        }

        // Create from FormField context (ensures required context is present)
        public class CreateFromFieldRequest
        {
            public int WorkflowSessionId { get; set; }
            public int StepNumber { get; set; }
            public string StepKey { get; set; } = null!;
            public string FieldName { get; set; } = null!;
            public string TaskType { get; set; } = null!;
            public string? InputJson { get; set; }
            public int? AssignedUserId { get; set; }
        }

        [HttpPost("from-field")]
        public async Task<IActionResult> CreateFromField([FromBody] CreateFromFieldRequest body)
        {
            try
            {
                var created = await _service.CreateFromFormFieldAsync(
                    body.WorkflowSessionId,
                    body.StepNumber,
                    body.StepKey,
                    body.FieldName,
                    body.TaskType,
                    body.InputJson,
                    body.AssignedUserId);

                return CreatedAtRoute("GetWorldTaskById", new { id = created.Id }, created);
            }
            catch (GateScanNotReadyException ex)
            {
                // KNG-268: the form shows the message.
                return Conflict(new { code = "GateScanNotReady", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    error = "Invalid WorldTask creation request",
                    details = ex.Message,
                    hint = "Ensure StepNumber, StepKey, FieldName, and TaskType are all provided. These are critical for Minecraft plugin field routing."
                });
            }
        }

        [HttpGet("{id:int}", Name = "GetWorldTaskById")]
        [RequireServiceOrAnyAreaNode(ReadCategory.OwnRecords)]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        // Get an open task by its task code (#180: closed tasks no longer answer to their code)
        [HttpGet("by-link-code/{linkCode}")]
        [RequireServiceOrAnyAreaNode(ReadCategory.OwnRecords)]
        public async Task<IActionResult> GetByLinkCode(string linkCode)
        {
            var item = await _service.GetByLinkCodeAsync(linkCode);
            if (item == null) return NotFound();
            return Ok(item);
        }

        // List tasks by status
        [HttpGet("status/{status}")]
        public async Task<IActionResult> ListByStatus(string status, [FromQuery] string? serverId = null)
        {
            var items = await _service.ListByStatusAsync(status, serverId);
            return Ok(items);
        }

        // List by session
        [HttpGet("session/{sessionId:int}")]
        [RequireServiceOrAnyAreaNode(ReadCategory.OwnRecords)]
        public async Task<IActionResult> GetBySession(int sessionId)
        {
            var items = await _service.GetBySessionAsync(sessionId);
            return Ok(items);
        }

        // List by user (paginated)
        [HttpPost("user/{userId:int}/search")]
        public async Task<ActionResult<PagedResultDto<WorldTaskReadDto>>> GetByUser(int userId, [FromBody] PagedQueryDto query)
        {
            var result = await _service.GetByUserAsync(userId, query);
            return Ok(result);
        }

        // Search/paginate tasks
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<WorldTaskReadDto>>> Search([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        // Status transitions and payload updates
        public class UpdateStatusRequest { public string Status { get; set; } = null!; public string? PayloadJson { get; set; } }

        [HttpPost("{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Status)) return BadRequest();
            return await Run(() => _service.UpdateStatusAsync(id, body.Status, body.PayloadJson));
        }

        // Claim a task (#180: Pending only, task code checked on a code claim; a second claimer gets 409)
        [HttpPost("{id:int}/claim")]
        [RequirePluginService]
        public async Task<IActionResult> ClaimTask(int id, [FromBody] ClaimTaskDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(() => _service.ClaimAsync(id, dto));
        }

        // Complete a task (#180: InProgress only, by the claiming server; a refused result is 422)
        [HttpPost("{id:int}/complete")]
        [RequirePluginService]
        public async Task<IActionResult> CompleteTask(int id, [FromBody] CompleteTaskDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.OutputJson)) return BadRequest();
            return await Run(() => _service.CompleteAsync(id, dto));
        }

        // Fail a task (#180: InProgress only, by the claiming server)
        [HttpPost("{id:int}/fail")]
        [RequirePluginService]
        public async Task<IActionResult> FailTask(int id, [FromBody] FailTaskDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ErrorMessage)) return BadRequest();
            return await Run(() => _service.FailAsync(id, dto));
        }

        /// <summary>
        /// #180: Pending/InProgress → Cancelled. The web app (the creator, who needs knk.admin.world to create a task at
        /// all, or any knk.admin.world holder) on Capture again / Try again / leaving the form / "Cancel task"; the game
        /// server when the player discards the task in game.
        /// </summary>
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> CancelTask(int id, [FromBody] CancelTaskDto? dto = null)
        {
            return await Run(() => _service.CancelAsync(id, dto));
        }

        /// <summary>
        /// PandiO/knk-workspace#240 "Send again": offers a waiting task again (<c>{"deliver": "auto" | "queue" | "replace" |
        /// {"userId": n}}</c>, default auto). 409 while it is offered, once it is no longer Pending, for a scan, or for
        /// auto/replace while you are not online in game. Web writes need knk.admin.world (class attribute).
        /// </summary>
        [HttpPost("{id:int}/offer")]
        public async Task<IActionResult> OfferAgain(int id, [FromBody] OfferTaskDto? dto = null)
        {
            return await Run(() => _service.OfferAgainAsync(id, dto));
        }

        /// <summary>#180: InProgress → Pending (in-game cancel, quit, restart, plugin disable). The task keeps its code.</summary>
        [HttpPost("{id:int}/release")]
        [RequirePluginService]
        public async Task<IActionResult> ReleaseTask(int id, [FromBody] ReleaseTaskDto? dto = null)
        {
            return await Run(() => _service.ReleaseAsync(id, dto));
        }

        /// <summary>#180: renews the claim (every 60 s); 409 when the task is no longer this server's (stop the handler).</summary>
        [HttpPost("{id:int}/heartbeat")]
        [RequirePluginService]
        public async Task<IActionResult> Heartbeat(int id, [FromBody] HeartbeatTaskDto? dto = null)
        {
            return await Run(() => _service.HeartbeatAsync(id, dto));
        }

        /// <summary>#180: the tasks a game server holds (InProgress, claimed by serverId), for its resync after a restart.</summary>
        [HttpGet("open")]
        [RequirePluginService]
        public async Task<IActionResult> ListOpenForServer([FromQuery] string? serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId)) return BadRequest(new { error = "serverId is required." });
            return Ok(await _service.ListOpenForServerAsync(serverId));
        }

        /// <summary>
        /// PandiO/knk-workspace#269: the temp regions (<c>tempregion_worldtask_&lt;id&gt;</c>) this game server may remove now:
        /// their draft was deleted, their task closed without a result or its session was swept, and nothing the API knows
        /// of uses them. Releases of another server are left out; ones with no server are listed to every server.
        /// </summary>
        [HttpGet("temp-region-releases")]
        [RequirePluginService]
        public async Task<IActionResult> ListTempRegionReleases([FromServices] ITempRegionReleaseService releases,
            [FromQuery] string? serverId = null, [FromQuery] int max = 50)
        {
            return Ok(await releases.ListDueAsync(serverId, max, HttpContext.RequestAborted));
        }

        /// <summary>#269: the game server removed the region, found none, or kept it; the release is deleted (404: gone already).</summary>
        [HttpPost("temp-region-releases/{id:int}/settle")]
        [RequirePluginService]
        public async Task<IActionResult> SettleTempRegionRelease(int id, [FromBody] TempRegionReleaseSettleDto? dto,
            [FromServices] ITempRegionReleaseService releases)
        {
            try
            {
                return await releases.SettleAsync(id, dto ?? new TempRegionReleaseSettleDto(), HttpContext.RequestAborted)
                    ? NoContent()
                    : NotFound(new { error = $"Temp region release {id} does not exist (settled already)." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>#180: the open tasks of a workflow session (web reattach after a reload).</summary>
        [HttpGet("session/{sessionId:int}/open")]
        [RequireServiceOrAnyAreaNode(ReadCategory.OwnRecords)]
        public async Task<IActionResult> GetOpenBySession(int sessionId)
        {
            return Ok(await _service.ListOpenBySessionAsync(sessionId));
        }

        /// <summary>
        /// WT2 (PandiO/knk-workspace#181, DESIGN §8.3): the busy check before "Send to Minecraft" / "Hand off to…": the
        /// task the logged-in caller holds in game (with the form it belongs to) and how many wait in their queue.
        /// </summary>
        [HttpGet("active-for-me")]
        [RequireServiceOrAnyAreaNode(ReadCategory.OwnRecords)]
        public async Task<ActionResult<WorldTaskActiveForMeDto>> GetActiveForMe()
        {
            if (User.GetUserId() is not { } userId) return Unauthorized(new { error = "Unauthorized", message = "Log in to use this." });
            return Ok(await _service.GetActiveForUserAsync(userId));
        }

        /// <summary>DESIGN §8.7: the web app wrote this Completed task's result into its draft.</summary>
        [HttpPost("{id:int}/applied")]
        public async Task<IActionResult> MarkApplied(int id)
        {
            return await Run(() => _service.MarkAppliedAsync(id));
        }

        /// <summary>#180: the answers of the state machine: 404, 409 (with the current status), 422, 400.</summary>
        private async Task<IActionResult> Run(Func<Task<WorldTaskReadDto>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (WorldTaskConflictException ex)
            {
                return Conflict(new { code = "WorldTaskConflict", message = ex.Message, status = ex.CurrentStatus });
            }
            catch (WorldTaskRejectedException ex)
            {
                return UnprocessableEntity(new { code = "WorldTaskRejected", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { code = "WorldTaskInvalid", message = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }
    }
}

using knkwebapi_v2.Attributes;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Dtos;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.world or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
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
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetWorldTaskById", new { id = created.Id }, created);
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
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        // Get task by link code
        [HttpGet("by-link-code/{linkCode}")]
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
            try
            {
                var updated = await _service.UpdateStatusAsync(id, body.Status, body.PayloadJson);
                return Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        // Claim a task
        [HttpPost("{id:int}/claim")]
        public async Task<IActionResult> ClaimTask(int id, [FromBody] ClaimTaskDto dto)
        {
            try
            {
                var updated = await _service.ClaimAsync(id, dto);
                return Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // Complete a task
        [HttpPost("{id:int}/complete")]
        public async Task<IActionResult> CompleteTask(int id, [FromBody] CompleteTaskDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.OutputJson)) return BadRequest();
            try
            {
                var updated = await _service.CompleteAsync(id, dto);
                return Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        // Fail a task
        [HttpPost("{id:int}/fail")]
        public async Task<IActionResult> FailTask(int id, [FromBody] FailTaskDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ErrorMessage)) return BadRequest();
            try
            {
                var updated = await _service.FailAsync(id, dto);
                return Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
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

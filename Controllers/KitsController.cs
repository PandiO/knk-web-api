using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KitsController : ControllerBase
    {
        private readonly IKitService _service;

        public KitsController(IKitService service)
        {
            _service = service;
        }

        // ===== CRUD (FormWizard-only, docs/specs/kits/DESIGN.md §4.0) =====

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:int}", Name = "GetKitById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] KitDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetKitById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] KitDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, dto);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _service.DeleteAsync(id);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (DbUpdateException ex)
            {
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<KitDto>>> Search([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        // ===== Availability, claim, purchase, give, first-join grant (DESIGN.md §4.1) =====

        [HttpGet("available")]
        public async Task<IActionResult> GetAvailable([FromQuery] int userId)
        {
            try
            {
                var result = await _service.GetAvailableForUserAsync(userId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPost("{id:int}/claim")]
        public async Task<IActionResult> Claim(int id, [FromQuery] int userId)
        {
            try
            {
                var result = await _service.ClaimKitAsync(userId, id);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "ClaimDenied", message = ex.Message });
            }
        }

        [HttpPost("{id:int}/purchase")]
        public async Task<IActionResult> Purchase(int id, [FromQuery] int userId)
        {
            try
            {
                var result = await _service.PurchaseKitAsync(userId, id);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "PurchaseDenied", message = ex.Message });
            }
        }

        // actorUserId is resolved from the authenticated caller's own JWT claims - never
        // client-supplied (DESIGN.md §4.6) - so this endpoint requires a valid bearer token.
        [Authorize]
        [HttpPost("{id:int}/give")]
        public async Task<IActionResult> Give(int id, [FromBody] GiveKitRequestDto request)
        {
            if (request == null) return BadRequest();

            var actorUserId = GetUserIdFromClaims(User);
            if (!actorUserId.HasValue)
            {
                return Unauthorized(new { error = "InvalidToken", message = "User claim missing or not authenticated." });
            }

            try
            {
                var result = await _service.GiveKitAsync(actorUserId.Value, request.TargetUserId, id);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPost("grant-first-join")]
        public async Task<IActionResult> GrantFirstJoin([FromQuery] int userId)
        {
            try
            {
                var result = await _service.GrantFirstJoinKitsAsync(userId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        private int? GetUserIdFromClaims(ClaimsPrincipal principal)
        {
            var userIdClaim = principal.FindFirst("uid")
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirst(ClaimTypes.NameIdentifier);

            if (userIdClaim == null) return null;
            return int.TryParse(userIdClaim.Value, out var userId) ? userId : (int?)null;
        }
    }
}

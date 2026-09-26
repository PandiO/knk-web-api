using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// A player's ignore list (KNG-18 Phase 2, docs/specs/private-messages/DESIGN.md §3.2) - read
    /// on join and changed by /ignore and /unignore in knk-plugin.
    /// <para>
    /// No auth attribute yet: the plugin calls anonymously, like every plugin endpoint. KNG-22
    /// adds the plugin service key that these endpoints should then require.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/users/{userId:int}/ignores")]
    public class UserIgnoresController : ControllerBase
    {
        private readonly IUserIgnoreService _service;

        public UserIgnoresController(IUserIgnoreService service)
        {
            _service = service;
        }

        /// <summary>Everyone the user ignores, oldest first.</summary>
        /// <response code="200">The ignore list (may be empty)</response>
        /// <response code="404">User not found</response>
        [HttpGet]
        public async Task<ActionResult<List<UserIgnoreDto>>> Get(int userId)
        {
            var ignores = await _service.GetAsync(userId);
            if (ignores == null)
                return NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
            return Ok(ignores);
        }

        /// <summary>Ignore a player. Idempotent: 204 whether they were just added or already ignored.</summary>
        /// <response code="204">Ignored</response>
        /// <response code="400">SelfIgnore, or CannotIgnoreStaff (the target holds knk.msg.unignorable)</response>
        /// <response code="404">Either user not found</response>
        /// <response code="409">IgnoreLimitReached</response>
        [HttpPut("{ignoredUserId:int}")]
        public async Task<IActionResult> Add(int userId, int ignoredUserId)
        {
            var result = await _service.AddAsync(userId, ignoredUserId);
            return result switch
            {
                UserIgnoreAddResult.Ignored => NoContent(),
                UserIgnoreAddResult.UserNotFound => NotFound(new { error = "UserNotFound", message = "User not found" }),
                UserIgnoreAddResult.SelfIgnore => BadRequest(new { error = "SelfIgnore", message = "You can't ignore yourself." }),
                UserIgnoreAddResult.CannotIgnoreStaff => BadRequest(new { error = "CannotIgnoreStaff", message = "You can't ignore staff." }),
                UserIgnoreAddResult.IgnoreLimitReached => Conflict(new
                {
                    error = "IgnoreLimitReached",
                    message = $"You can ignore at most {UserIgnoreService.MaxIgnoresPerUser} players."
                }),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }

        /// <summary>Stop ignoring a player. Idempotent: always 204.</summary>
        [HttpDelete("{ignoredUserId:int}")]
        public async Task<IActionResult> Remove(int userId, int ignoredUserId)
        {
            await _service.RemoveAsync(userId, ignoredUserId);
            return NoContent();
        }
    }
}

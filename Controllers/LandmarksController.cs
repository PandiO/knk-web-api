using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// Landmarks, staff-made named places (off-road navigation phase 5.1, PandiO/knk-workspace#708; knk-workspace
    /// <c>docs/specs/navigation-offroad/DESIGN.md</c> § 6.4, decision O26).
    /// <list type="number">
    ///   <item>Reads by world and by id are anonymous (DESIGN § 6.4.1): a landmark is a public place name like a town on
    ///     the overview map. Both are in <c>EndpointAccessRulesTests.AnonymousAllowList</c> and under the per-IP
    ///     <c>map</c> rate limit (the public-read budget; the <c>lookup</c> budget is shared with the link-code calls of
    ///     the game server).</item>
    ///   <item>The search is a dashboard read (category F): the World area node, <c>knk.admin.world.landmark</c> or the
    ///     plugin key.</item>
    ///   <item>Writes need <c>knk.admin.world.landmark</c> (matched by <c>knk.admin.world.*</c>, <c>knk.admin.*</c> and
    ///     <c>*</c>) or the plugin key; create and update also <c>knk.admin.content</c> for web users (the dashboard's
    ///     change rule, #374); delete also <c>knk.admin.delete</c>.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/landmarks")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.LandmarkManage)]
    [DashboardReads("Landmark")]
    public class LandmarksController : ControllerBase
    {
        private readonly ILandmarkService _service;

        public LandmarksController(ILandmarkService service)
        {
            _service = service;
        }

        /// <summary>Every landmark, or the landmarks whose pin is in <paramref name="world"/>, by name.</summary>
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitingSetup.MapPolicy)]
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<LandmarkReadDto>>> GetAll([FromQuery] string? world)
        {
            return Ok(await _service.GetAllAsync(world));
        }

        [AllowAnonymous]
        [EnableRateLimiting(RateLimitingSetup.MapPolicy)]
        [HttpGet("{id:int}", Name = "GetLandmarkById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [RequireDashboardChange("Landmark")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] LandmarkCreateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetLandmarkById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [RequireDashboardChange("Landmark")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] LandmarkUpdateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await _service.UpdateAsync(id, dto));
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

        // Write node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280).
        [RequireServiceOrPermission(StaffPermissions.Delete)]
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
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<LandmarkListDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchAsync(query));
        }
    }
}

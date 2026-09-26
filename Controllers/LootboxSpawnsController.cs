using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// World boxes at runtime (docs/specs/lootboxes/DESIGN.md §3.3, IMPLEMENTATION_PLAN.md Phase 2). The plugin reads
    /// the runtime config and active boxes, asks to spawn, and claims; the API decides whether a box may spawn, what
    /// it is, and what it gives. Everything is game-server-only (<see cref="RequirePluginServiceAttribute"/>) except
    /// the active list and despawn, which the web app's admin page uses too. The staff member behind an admin action
    /// is the plugin's X-Acting-User-Id header (<c>HttpContext.GetKnkCaller()</c>), never a body field.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LootboxSpawnsController : ControllerBase
    {
        private readonly ILootboxRuntimeService _service;

        public LootboxSpawnsController(ILootboxRuntimeService service)
        {
            _service = service;
        }

        [HttpGet("runtime-config")]
        [RequirePluginService]
        public async Task<ActionResult<LootboxRuntimeConfigDto>> GetRuntimeConfig()
        {
            return Ok(await _service.GetRuntimeConfigAsync());
        }

        [HttpGet("active")]
        [RequireServiceOrPermission(StaffPermissions.ManageLootboxes)]
        public async Task<ActionResult<List<LootboxSpawnDto>>> GetActive()
        {
            return Ok(await _service.GetActiveAsync());
        }

        /// <summary>201 with the new box; 409 <c>{code: Disabled|AreaFull|GlobalFull|NoEnabledType|NoBoxGrade}</c>.</summary>
        [HttpPost]
        [RequirePluginService]
        public async Task<IActionResult> Spawn([FromBody] LootboxSpawnRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () =>
            {
                var spawn = await _service.SpawnAsync(request);
                return StatusCode(StatusCodes.Status201Created, spawn);
            });
        }

        /// <summary><c>/knk lootbox spawn</c>: ignores caps; audited LootboxSpawnedByAdmin.</summary>
        [HttpPost("admin")]
        [RequirePluginService]
        public async Task<IActionResult> AdminSpawn([FromBody] LootboxAdminSpawnRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () =>
            {
                var spawn = await _service.AdminSpawnAsync(request, HttpContext.GetKnkCaller().ActorUserId);
                return StatusCode(StatusCodes.Status201Created, spawn);
            });
        }

        /// <summary>An Active box becomes Removed; 200 with the box as it is now (any status).</summary>
        [HttpPost("{id:int}/despawn")]
        [RequireServiceOrPermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> Despawn(int id)
        {
            return await LootboxResults.Run(this, async () =>
                Ok(await _service.DespawnAsync(id, HttpContext.GetKnkCaller().ActorUserId)));
        }

        /// <summary>
        /// 200 <see cref="LootboxClaimResultDto"/> (<c>replay=true</c> for a repeated idempotency key); 409
        /// <c>{code: AlreadyClaimed|Expired|Removed|TokenMismatch|Disabled|Frozen|UserInactive|EmptyPool|IdempotencyKeyReused}</c>;
        /// 429 <c>{code: DailyLimit, scope: Global|Type, limit, resetsAt}</c>.
        /// </summary>
        [HttpPost("{id:int}/claim")]
        [RequirePluginService]
        public async Task<IActionResult> Claim(int id, [FromBody] LootboxClaimRequestDto request)
        {
            if (request == null) return BadRequest();
            return await LootboxResults.Run(this, async () => Ok(await _service.ClaimAsync(id, request)));
        }
    }

    /// <summary>The lootbox runtime's error mapping: 400 / 404 / 409 <c>{code, message}</c> / 429 daily limit.</summary>
    internal static class LootboxResults
    {
        public static async Task<IActionResult> Run(ControllerBase controller, Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (LootboxDailyLimitException ex)
            {
                return controller.StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    code = ex.Code,
                    scope = ex.Scope,
                    limit = ex.Limit,
                    resetsAt = ex.ResetsAt,
                    message = ex.Message,
                });
            }
            catch (LootboxConflictException ex)
            {
                return controller.Conflict(new { code = ex.Code, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return controller.NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return controller.BadRequest(new { message = ex.Message });
            }
        }
    }
}

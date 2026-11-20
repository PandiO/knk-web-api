using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege shop L4 (knk-workspace docs/specs/siege-shop/DESIGN.md § 4.2, § 6.2.3-6.2.4; P08, P09; W32, W67;
    // PandiO/knk-workspace#869, #795): a player buys a siege weapon type's unlock with coins. The game server buys for the
    // player it names (plugin key: the in-game Siege unlocks menu, which refuses during a match); a signed-in web user
    // only for themselves (their profile's Siege unlocks panel). Staff grants stay on UserSiegeWeaponUnlocksController.
    // No rate limit, like the kit purchase: the per-IP limiters would put every player behind the game server's one IP,
    // and the idempotency key makes a repeated click harmless.
    [ApiController]
    [Route("api/[controller]")]
    public class SiegeWeaponUnlocksController : ControllerBase
    {
        private readonly ISiegeWeaponUnlockPurchaseService _service;

        public SiegeWeaponUnlocksController(ISiegeWeaponUnlockPurchaseService service)
        {
            _service = service;
        }

        /// <summary>The signed-in player's view: every template with its price, minimum title and their state.</summary>
        [Authorize]
        [HttpGet("mine")]
        public async Task<ActionResult<SiegeWeaponUnlocksOverviewDto>> GetMine()
        {
            var caller = HttpContext.GetKnkCaller();
            if (!caller.IsWebUser || caller.WebUserId is not int userId) return Unauthorized();
            try
            {
                return Ok(await _service.GetOverviewAsync(userId));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>The same view for the game server's Siege unlocks menu.</summary>
        [RequirePluginService]
        [HttpGet("by-user/{userId:int}")]
        public async Task<ActionResult<SiegeWeaponUnlocksOverviewDto>> GetByUser(int userId)
        {
            try
            {
                return Ok(await _service.GetOverviewAsync(userId));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Buys the unlock: 200 with the result (also when it was unlocked already: nothing charged), 404 for an unknown
        /// template or user, 409 <c>{ code, message }</c> when the title level (<c>NeedsTitle</c>) or the coins
        /// (<c>InsufficientFunds</c>) are short, 403 when a web user names another player.
        /// </summary>
        [RequireServiceOrLoggedIn] // the action decides whose unlock it is
        [HttpPost("purchase")]
        public async Task<ActionResult<SiegeWeaponUnlockPurchaseResultDto>> Purchase([FromBody] SiegeWeaponUnlockPurchaseRequestDto request)
        {
            if (request == null || request.TemplateId <= 0) return BadRequest("templateId is required.");

            var caller = HttpContext.GetKnkCaller();
            int userId;
            if (caller.IsWebUser && caller.WebUserId is int self)
            {
                if (request.UserId.HasValue && request.UserId.Value != self)
                    return StatusCode(StatusCodes.Status403Forbidden, new { code = "NotYourAccount", message = "You can only unlock siege weapons for yourself." });
                userId = self;
            }
            else if (caller.IsPluginService && request.UserId is int named && named > 0)
            {
                userId = named;
            }
            else
            {
                return BadRequest("userId is required.");
            }

            try
            {
                // The key is the deterministic siege-unlock:{templateId}:{userId}; the caller only names the initiator.
                var ctx = CurrencyContext.ForCaller(caller, CurrencyReasons.SiegeUnlock, "siege-unlock", nameof(SiegeWeaponUnlocksController), staffAction: false);
                return Ok(await _service.PurchaseAsync(userId, request.TemplateId, ctx));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (SiegeWeaponUnlockRefusedException ex)
            {
                return Conflict(new { code = ex.Code, message = ex.Message });
            }
            catch (CurrencyException ex)
            {
                return Conflict(new { code = ex.Code.ToString(), message = ex.Message });
            }
        }
    }
}

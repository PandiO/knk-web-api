using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Stock;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// Item deliveries to players (P7, PandiO/knk-workspace#60, production DESIGN § 4.6). Reads (GET and the POST search):
    /// the World area or knk.economy.admin (dashboard type <c>ItemDelivery</c>), or the plugin key. A delivery (POST):
    /// the game server (what it could not place; it names the player in the body) or staff with knk.economy.admin (a
    /// gift or grant from the web app). Needs <c>Production:Enabled</c> (409 while off, A21) and an idempotency key.
    /// </summary>
    [ApiController]
    [Route("api/ItemDeliveries")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.EconomyAdmin)]
    [DashboardReads("ItemDelivery")]
    public class ItemDeliveriesController : ControllerBase
    {
        private readonly IItemDeliveryService _service;

        public ItemDeliveriesController(IItemDeliveryService service) => _service = service;

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ItemDeliveryListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        [HttpGet("{id:long}", Name = "GetItemDeliveryById")]
        public async Task<IActionResult> GetById(long id, CancellationToken ct)
        {
            var item = await _service.GetAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Delivers items to a player's inbox: 200 with the delivery (also on a replay), lost parts included.</summary>
        [HttpPost]
        public async Task<IActionResult> Deliver([FromBody] ItemDeliveryRequestDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest();
            var caller = HttpContext.GetKnkCaller();
            var actor = caller.IsWebUser && caller.WebUserId is { } staff ? DeliveryActor.Staff(staff) : DeliveryActor.GameServer(caller.ActingUserId);
            return await DeliveryErrors.Run(async () => Ok(await _service.DeliverAsync(dto, actor, ct)));
        }
    }

    /// <summary>
    /// A player's reward inbox and premium stash (P7, PandiO/knk-workspace#60, DESIGN § 4.6, D7).
    /// <list type="number">
    ///   <item>Views (<c>GET {userId}</c>, <c>GET {userId}/stash</c>): the game server, the player themself (web), or staff
    ///   with knk.economy.admin. They work while <c>Production:Enabled</c> is off.</item>
    ///   <item>Claim (<c>POST {userId}/claim</c>): the game server only, for the player it names in
    ///   <c>X-Acting-User-Id</c> (it places the items it claims).</item>
    ///   <item>Stash deposit and withdraw: the game server for that player, or staff with knk.economy.admin (admin use).</item>
    ///   <item><c>POST sweep</c>: reminders and expiry now (staff with knk.economy.admin; the API runs it hourly).</item>
    /// </list>
    /// Every move needs <c>Production:Enabled</c> (409) and an idempotency key; partial results are 200 with a status.
    /// </summary>
    [ApiController]
    [Route("api/Inbox")]
    public class InboxController : ControllerBase
    {
        private readonly IItemDeliveryService _service;

        public InboxController(IItemDeliveryService service) => _service = service;

        [RequireServiceSelfOrPermission(StaffPermissions.EconomyAdmin)]
        [HttpGet("{userId:int}")]
        public Task<IActionResult> Inbox(int userId, CancellationToken ct) =>
            DeliveryErrors.Run(async () => Ok(await _service.ViewAsync(userId, StoragePurpose.Inbox, ct)));

        [RequireServiceSelfOrPermission(StaffPermissions.EconomyAdmin)]
        [HttpGet("{userId:int}/stash")]
        public Task<IActionResult> Stash(int userId, CancellationToken ct) =>
            DeliveryErrors.Run(async () => Ok(await _service.ViewAsync(userId, StoragePurpose.Stash, ct)));

        /// <summary>The game server claims what the player has room for; it places the accepted amounts.</summary>
        [RequirePluginService]
        [HttpPost("{userId:int}/claim")]
        public async Task<IActionResult> Claim(int userId, [FromBody] PlayerStorageMoveDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest();
            var caller = HttpContext.GetKnkCaller();
            if (caller.ActingUserId != userId)
                return BadRequest(new { error = "ActingUserRequired", message = $"{PluginServiceAuth.ActingUserHeader} must name the player who claims." });
            return await DeliveryErrors.Run(async () => Ok(ToDto(await _service.ClaimAsync(userId, dto, DeliveryActor.GameServer(userId), ct))));
        }

        [RequireServiceOrPermission(StaffPermissions.EconomyAdmin)]
        [HttpPost("{userId:int}/stash/deposit")]
        public Task<IActionResult> Deposit(int userId, [FromBody] PlayerStorageMoveDto dto, CancellationToken ct) => StashMove(userId, true, dto, ct);

        [RequireServiceOrPermission(StaffPermissions.EconomyAdmin)]
        [HttpPost("{userId:int}/stash/withdraw")]
        public Task<IActionResult> Withdraw(int userId, [FromBody] PlayerStorageMoveDto dto, CancellationToken ct) => StashMove(userId, false, dto, ct);

        [RequireServiceOrPermission(StaffPermissions.EconomyAdmin)]
        [HttpPost("sweep")]
        public async Task<IActionResult> Sweep(CancellationToken ct) => Ok(await _service.SweepAsync(ct));

        private async Task<IActionResult> StashMove(int userId, bool deposit, PlayerStorageMoveDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest();
            var caller = HttpContext.GetKnkCaller();
            DeliveryActor actor;
            if (caller.IsWebUser && caller.WebUserId is { } staff) actor = DeliveryActor.Staff(staff);
            else if (caller.ActingUserId == userId) actor = DeliveryActor.GameServer(userId);
            else return BadRequest(new { error = "ActingUserRequired", message = $"{PluginServiceAuth.ActingUserHeader} must name the stash's player." });
            return await DeliveryErrors.Run(async () => Ok(ToDto(await _service.StashMoveAsync(userId, deposit, dto, actor, ct))));
        }

        private static PlayerStorageMoveResultDto ToDto(PlayerStorageMoveResult r) => new()
        {
            StorageId = r.StorageId,
            Result = StockTransactionsController.ToDto(r.Result),
            View = r.View,
        };
    }

    internal static class DeliveryErrors
    {
        public static async Task<IActionResult> Run(Func<Task<IActionResult>> work)
        {
            try
            {
                return await work();
            }
            catch (KeyNotFoundException ex)
            {
                return new NotFoundObjectResult(new { error = ex.Message });
            }
            catch (ProductionOffException ex)
            {
                return new ConflictObjectResult(new { code = "ProductionDisabled", error = ex.Message });
            }
            catch (StockIdempotencyConflictException ex)
            {
                return new ConflictObjectResult(new { code = "IdempotencyKeyReuse", error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return new ConflictObjectResult(new { error = ex.Message });
            }
            catch (DbUpdateException)
            {
                return new ConflictObjectResult(new { error = "The inbox changed while saving; try again with the same key." });
            }
        }
    }
}

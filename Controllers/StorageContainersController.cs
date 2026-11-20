using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// Containers bound to a storage (P6a, PandiO/knk-workspace#683, production DESIGN § 6): Terminal and Display blocks.
    /// Staff CRUD: reads the World area (or the plugin key), writes <c>knk.admin.world</c> (+ the dashboard change rule),
    /// delete also <c>knk.admin.delete</c>; it works while <c>Production:Enabled</c> is off. The in-game routes
    /// (<c>terminal</c>, <c>put</c>, <c>take</c>, <c>display</c>) are the game server's only: it names the player in
    /// <c>X-Acting-User-Id</c>, and the API checks that player may use the storage (its town's members, its player,
    /// staff; M3). Take and put need <c>Production:Enabled</c> (409 while off, A21).
    /// </summary>
    [ApiController]
    [Route("api/StorageContainers")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("StorageContainer")]
    public class StorageContainersController : ControllerBase
    {
        private readonly IStorageContainerService _service;
        private readonly IProductionViewerResolver _viewers;

        public StorageContainersController(IStorageContainerService service, IProductionViewerResolver viewers)
        {
            _service = service;
            _viewers = viewers;
        }

        [HttpGet("{id:int}", Name = "GetStorageContainerById")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>The container bound to a block, or 404 (the game server asks when a player opens a chest).</summary>
        [HttpGet("by-block")]
        public async Task<IActionResult> GetByBlock([FromQuery] string world, [FromQuery] int x, [FromQuery] int y, [FromQuery] int z,
            CancellationToken ct)
        {
            var item = await _service.GetByBlockAsync(world, x, y, z, ct);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<StorageContainerListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        [RequireDashboardChange("StorageContainer")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StorageContainerDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest();
            return await Run(async () =>
            {
                var created = await _service.CreateAsync(dto, HttpContext.GetKnkCaller().ActorUserId, ct);
                return CreatedAtRoute("GetStorageContainerById", new { id = created.Id }, created);
            });
        }

        [RequireDashboardChange("StorageContainer")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] StorageContainerDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await _service.UpdateAsync(id, dto, ct)));
        }

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280).
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
            await Run(async () =>
            {
                await _service.DeleteAsync(id, ct);
                return NoContent();
            });

        // ---- In game (the game server, for the player in X-Acting-User-Id) ----

        /// <summary>One page of a Terminal container's storage (403 for a player who may not use it).</summary>
        [RequirePluginService]
        [HttpGet("{id:int}/terminal")]
        public async Task<IActionResult> Terminal(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = StorageContainerService.MaxPageSize,
            CancellationToken ct = default)
        {
            var viewer = await _viewers.ResolveAsync(HttpContext.GetKnkCaller(), ct);
            return await Run(async () => Ok(await _service.TerminalViewAsync(id, viewer, page, pageSize, ct)));
        }

        /// <summary>A player puts items into the terminal's storage (<c>SYS_WORLD</c> → storage, reason Deposit).</summary>
        [RequirePluginService]
        [HttpPost("{id:int}/put")]
        public Task<IActionResult> Put(int id, [FromBody] TerminalMoveDto dto, CancellationToken ct) => Move(id, put: true, dto, ct);

        /// <summary>A player takes items out of the terminal's storage (storage → <c>SYS_WORLD</c>, reason Withdraw).</summary>
        [RequirePluginService]
        [HttpPost("{id:int}/take")]
        public Task<IActionResult> Take(int id, [FromBody] TerminalMoveDto dto, CancellationToken ct) => Move(id, put: false, dto, ct);

        /// <summary>The read-only sample a Display container shows.</summary>
        [RequirePluginService]
        [HttpGet("{id:int}/display")]
        public async Task<IActionResult> Display(int id, CancellationToken ct) =>
            await Run(async () => Ok(await _service.DisplaySampleAsync(id, ct)));

        private async Task<IActionResult> Move(int id, bool put, TerminalMoveDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest();
            var caller = HttpContext.GetKnkCaller();
            if (caller.ActingUserId is not > 0)
                return BadRequest(new { error = "ActingUserRequired", message = $"{PluginServiceAuth.ActingUserHeader} must name the player." });
            var viewer = await _viewers.ResolveAsync(caller, ct);
            return await Run(async () => Ok(StockTransactionsController.ToDto(await _service.TerminalMoveAsync(id, put, dto, viewer, ct))));
        }

        private async Task<IActionResult> Run(Func<Task<IActionResult>> work)
        {
            try
            {
                return await work();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (StorageAccessDeniedException ex)
            {
                return StatusCode(403, new { error = "Forbidden", message = ex.Message });
            }
            catch (ProductionDisabledException ex)
            {
                return Conflict(new { code = "ProductionDisabled", error = ex.Message });
            }
            catch (StockIdempotencyConflictException ex)
            {
                return Conflict(new { code = "IdempotencyKeyReuse", error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { error = "The storage changed while saving; try again." });
            }
            catch (DbUpdateException)
            {
                return Conflict(new { error = "That block is already bound." });
            }
        }
    }
}

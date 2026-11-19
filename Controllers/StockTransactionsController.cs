using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The stock ledger (stock core P1, PandiO/knk-workspace#97, production DESIGN § 4.3-4.5): the ledger explorer's
    /// reads (transactions, moves, roll-ups, with filters and the drill-down from a stock line to its moves), manual
    /// moves and adjustments, compensating corrections, the reconciler and the roll-up. Reads (GET and the POST
    /// searches): the World area node or knk.economy.admin (the StockTransaction dashboard type); writes:
    /// knk.economy.admin; the plugin key passes both. Capacity, missing stock and locks are results (200 with a status), never
    /// errors. Available while <c>Production:Enabled</c> is off (admin tools, DESIGN § 18.2).
    /// </summary>
    [ApiController]
    [Route("api/StockTransactions")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.EconomyAdmin)]
    [DashboardReads("StockTransaction")]
    public class StockTransactionsController : ControllerBase
    {
        private readonly IStockService _stock;
        private readonly IStockLedgerService _ledger;
        private readonly IStockReconciler _reconciler;
        private readonly IStockRollupService _rollup;

        public StockTransactionsController(IStockService stock, IStockLedgerService ledger, IStockReconciler reconciler,
            IStockRollupService rollup)
        {
            _stock = stock;
            _ledger = ledger;
            _reconciler = reconciler;
            _rollup = rollup;
        }

        // ---- Reads (the ledger explorer) ----

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<StockTransactionListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _ledger.SearchTransactionsAsync(query));

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var item = await _ledger.GetTransactionAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Moves with filters; <c>storageId</c> + <c>itemBlueprintId</c> = the drill-down from a stock line.</summary>
        [HttpPost("moves/search")]
        public async Task<ActionResult<PagedResultDto<StockMoveDto>>> SearchMoves([FromBody] PagedQueryDto query) =>
            Ok(await _ledger.SearchMovesAsync(query));

        /// <summary>The 30-day roll-up rows (G3) with the same filters.</summary>
        [HttpPost("rollups/search")]
        public async Task<ActionResult<PagedResultDto<StockMoveRollupDto>>> SearchRollups([FromBody] PagedQueryDto query) =>
            Ok(await _ledger.SearchRollupsAsync(query));

        /// <summary>Runs the reconciler now (read-only).</summary>
        [HttpGet("reconcile")]
        public async Task<ActionResult<StockReconciliationReport>> Reconcile(CancellationToken ct) =>
            Ok(await _reconciler.RunAsync(ct));

        // ---- Writes ----

        /// <summary>A move of one or more lines in one transaction (the move dialog, or the game server).</summary>
        [HttpPost]
        public Task<IActionResult> Move([FromBody] StockMoveRequestDto dto, CancellationToken ct) => Run(async () =>
        {
            if (dto?.Lines == null) throw new ArgumentException("A move needs lines.");
            var lines = dto.Lines.Select(l => new StockLineRequest(l.FromStorageId, l.ToStorageId,
                l.ItemInstanceId.HasValue ? null : l.ItemBlueprintId, l.Amount ?? (l.ItemInstanceId.HasValue ? 1 : 0),
                l.ItemInstanceId)).ToList();
            return await _stock.MoveManyAsync(lines, Context(dto.Reason, dto.Note, dto.SourceType, dto.SourceRef, dto.IdempotencyKey),
                dto.Mode, ct);
        });

        /// <summary>The adjust dialog: + from SYS_ADJUSTMENT into the storage, − back out (reason AdminAdjust, note required).</summary>
        [HttpPost("adjust")]
        public Task<IActionResult> Adjust([FromBody] StockAdjustRequestDto dto, CancellationToken ct) => Run(async () =>
        {
            if (dto == null) throw new ArgumentException("An adjustment is required.");
            return await _stock.AdjustAsync(dto.StorageId, dto.ItemBlueprintId, dto.ItemInstanceId, dto.Delta,
                Context(StockReason.AdminAdjust, dto.Note, null, null, dto.IdempotencyKey), ct);
        });

        /// <summary>A compensating correction that moves every move of the transaction back (never an edit).</summary>
        [HttpPost("{id:long}/reverse")]
        public Task<IActionResult> Reverse(long id, [FromBody] StockReverseRequestDto? dto, CancellationToken ct) => Run(() =>
            _stock.ReverseAsync(id, Context(StockReason.Correction, dto?.Note, null, null, dto?.IdempotencyKey), ct));

        /// <summary>Runs the 30-day roll-up of Production and ProductionInput moves now (G3).</summary>
        [HttpPost("rollup")]
        public async Task<ActionResult<StockRollupResult>> RollUp(CancellationToken ct) => Ok(await _rollup.RollUpAsync(ct));

        // ---- Helpers ----

        private StockContext Context(StockReason reason, string? note, string? sourceType, string? sourceRef, string? key)
        {
            var caller = HttpContext.GetKnkCaller();
            return new StockContext
            {
                Reason = reason,
                ActorKind = caller.IsWebUser ? StockActorKind.User : StockActorKind.PluginServer,
                ActorUserId = caller.ActorUserId,
                ActorComponent = caller.IsWebUser ? "web" : "plugin",
                Note = note,
                SourceType = sourceType,
                SourceRef = sourceRef,
                IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim(),
            };
        }

        private async Task<IActionResult> Run(Func<Task<StockResult>> work)
        {
            try
            {
                return Ok(ToDto(await work()));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (StockIdempotencyConflictException ex)
            {
                return Conflict(new { code = "IdempotencyKeyReuse", error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { error = "A storage changed while saving; try again." });
            }
        }

        internal static StockResultDto ToDto(StockResult r) => new()
        {
            Status = r.Status,
            TransactionId = r.TransactionId,
            Replayed = r.Replayed,
            Accepted = r.Accepted,
            Lines = r.Lines.Select(l => new StockLineResultDto
            {
                LineNo = l.LineNo,
                FromStorageId = l.FromStorageId,
                ToStorageId = l.ToStorageId,
                ItemBlueprintId = l.ItemBlueprintId,
                ItemInstanceId = l.ItemInstanceId,
                Requested = l.Requested,
                Accepted = l.Accepted,
                Rejected = l.Rejected,
                RejectReason = l.RejectReason,
            }).ToList(),
        };
    }
}

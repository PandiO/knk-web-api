using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Shops;
using knkwebapi_v2.Services.Stock;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    // Shops P10 (PandiO/knk-workspace#248, shops DESIGN § 2-5, § 9.1). Reads: the World area (or the plugin key). Admin
    // writes: knk.admin.world or the plugin key (+ the dashboard change rule on create/update); deletes also
    // knk.admin.delete. Buying and selling: the plugin key only (the game server acts for the player after the player
    // confirmed). Admin CRUD and buying work while Production:Enabled is off; selling, delivery to the inbox,
    // restocking, processing and the stats job need it (409 ProductionDisabled).

    /// <summary>Shop types: accepted categories (a category accepts its sub-categories) and levels.</summary>
    [ApiController]
    [Route("api/ShopTypes")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ShopType")]
    public class ShopTypesController : ControllerBase
    {
        private readonly IShopTypeService _service;

        public ShopTypesController(IShopTypeService service) => _service = service;

        [HttpGet("{id:int}", Name = "GetShopTypeById")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Filters isActive; search term = name.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ShopTypeListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        [RequireDashboardChange("ShopType")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] ShopTypeDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateAsync(dto, ct);
            return CreatedAtRoute("GetShopTypeById", new { id = created.Id }, created);
        });

        [RequireDashboardChange("ShopType")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] ShopTypeDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _service.UpdateAsync(id, dto, ct)));

        /// <summary>409 while a shop uses the type.</summary>
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id, CancellationToken ct) => ShopErrors.Run(async () =>
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        });
    }

    /// <summary>Shop type levels (the production level pattern): speed, yield, input cost, price multiplier, caps and the
    /// default restocking min/max. Saving a level writes its caps to the storages of the shops at that level.</summary>
    [ApiController]
    [Route("api/ShopTypeLevels")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ShopTypeLevel")]
    public class ShopTypeLevelsController : ControllerBase
    {
        private readonly IShopTypeService _service;

        public ShopTypeLevelsController(IShopTypeService service) => _service = service;

        [HttpGet("{id:int}", Name = "GetShopTypeLevelById")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetLevelAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Filter shopTypeId.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ShopTypeLevelListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchLevelsAsync(query, ct));

        [RequireDashboardChange("ShopTypeLevel")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] ShopTypeLevelDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateLevelAsync(dto, ct);
            return CreatedAtRoute("GetShopTypeLevelById", new { id = created.Id }, created);
        });

        [RequireDashboardChange("ShopTypeLevel")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] ShopTypeLevelDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _service.UpdateLevelAsync(id, dto, ct)));

        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id, CancellationToken ct) => ShopErrors.Run(async () =>
        {
            await _service.DeleteLevelAsync(id, ct);
            return NoContent();
        });
    }

    /// <summary>Shops: a structure with an Input and a Merchandise storage, its own worker and processing lines
    /// (<c>api/ProductionLines</c>, <c>api/ProductionStructures/{id}/production</c> work for shops), the offer, and the
    /// purchase and sale endpoints the game server calls.</summary>
    [ApiController]
    [Route("api/Shops")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("Shop")]
    public class ShopsController : ControllerBase
    {
        private readonly IShopService _service;
        private readonly IShopTradingService _trading;

        public ShopsController(IShopService service, IShopTradingService trading)
        {
            _service = service;
            _trading = trading;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetShopById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Filters districtId, townId, isRural, shopTypeId, kind; search term = name or description.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ProductionWorldListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        [RequireDashboardChange("Shop")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] ShopDto dto) => ShopErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateAsync(dto);
            return CreatedAtRoute("GetShopById", new { id = created.Id }, created);
        });

        [RequireDashboardChange("Shop")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] ShopDto dto) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _service.UpdateAsync(id, dto)));

        /// <summary>409 while its storages hold stock or have ledger history, or its worker is out collecting.</summary>
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id) => ShopErrors.Run(async () =>
        {
            await _service.DeleteAsync(id);
            return NoContent();
        });

        /// <summary>What the shop offers now: catalogue entries and Merchandise contents with price and stock.</summary>
        [HttpGet("{id:int}/offer")]
        public Task<IActionResult> Offer(int id, CancellationToken ct) => ShopErrors.Run(async () => Ok(await _trading.OfferAsync(id, ct)));

        /// <summary>The price of a purchase now (the confirmation screen); changes nothing.</summary>
        [RequirePluginService]
        [HttpPost("{id:int}/quote")]
        public Task<IActionResult> Quote(int id, [FromBody] ShopPurchaseRequestDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _trading.QuoteAsync(id, dto, ct)));

        /// <summary>A confirmed purchase: coins to the town account and the goods out of Merchandise in one transaction.</summary>
        [RequirePluginService]
        [HttpPost("{id:int}/purchase")]
        public Task<IActionResult> Purchase(int id, [FromBody] ShopPurchaseRequestDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _trading.PurchaseAsync(id, dto, ct)));

        /// <summary>The sell menu's buy-back prices; changes nothing. 409 ProductionDisabled while the feature is off.</summary>
        [RequirePluginService]
        [HttpPost("{id:int}/sell-quote")]
        public Task<IActionResult> SellQuote(int id, [FromBody] ShopSellRequestDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _trading.SellQuoteAsync(id, dto, ct)));

        /// <summary>A confirmed sale, all lines or none: goods into Merchandise, the buy-back price from the town account.</summary>
        [RequirePluginService]
        [HttpPost("{id:int}/sell")]
        public Task<IActionResult> Sell(int id, [FromBody] ShopSellRequestDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _trading.SellAsync(id, dto, ct)));
    }

    /// <summary>The static admin catalogue: a blueprint or one item instance per entry, a static price, Stocked or
    /// Unlimited, pin and order.</summary>
    [ApiController]
    [Route("api/ShopCatalogueEntries")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ShopCatalogueEntry")]
    public class ShopCatalogueEntriesController : ControllerBase
    {
        private readonly IShopCatalogueService _service;

        public ShopCatalogueEntriesController(IShopCatalogueService service) => _service = service;

        [HttpGet("{id:int}", Name = "GetShopCatalogueEntryById")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Filters shopId, itemBlueprintId; search term = blueprint name.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ShopCatalogueEntryListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        [RequireDashboardChange("ShopCatalogueEntry")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] ShopCatalogueEntryDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateAsync(dto, ct);
            return CreatedAtRoute("GetShopCatalogueEntryById", new { id = created.Id }, created);
        });

        [RequireDashboardChange("ShopCatalogueEntry")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] ShopCatalogueEntryDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _service.UpdateAsync(id, dto, ct)));

        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id, CancellationToken ct) => ShopErrors.Run(async () =>
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        });
    }

    /// <summary>Display spots of shops (display stalls P12, PandiO/knk-workspace#250, shops DESIGN § 7): one block per spot,
    /// registered explicitly (in game with <c>/knk shop display add</c>, or the web app). The game server chooses what each
    /// spot shows from the offer and the trade stats. Same rules as the catalogue; the plugin key registers and removes
    /// spots from game.</summary>
    [ApiController]
    [Route("api/ShopDisplaySpots")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ShopDisplaySpot")]
    public class ShopDisplaySpotsController : ControllerBase
    {
        private readonly IShopDisplaySpotService _service;

        public ShopDisplaySpotsController(IShopDisplaySpotService service) => _service = service;

        [HttpGet("{id:int}", Name = "GetShopDisplaySpotById")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Filters shopId, enabled, worldName; search term = label or shop name.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ShopDisplaySpotListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        /// <summary>409 when the block is already a spot or the shop has its maximum.</summary>
        [RequireDashboardChange("ShopDisplaySpot")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] ShopDisplaySpotDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateAsync(dto, HttpContext.GetKnkCaller().ActorUserId, ct);
            return CreatedAtRoute("GetShopDisplaySpotById", new { id = created.Id }, created);
        });

        [RequireDashboardChange("ShopDisplaySpot")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] ShopDisplaySpotDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _service.UpdateAsync(id, dto, ct)));

        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id, CancellationToken ct) => ShopErrors.Run(async () =>
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        });
    }

    /// <summary>Hourly trade stats per shop, blueprint and hour, rolled up from the ledger; read only, plus a rebuild.</summary>
    [ApiController]
    [Route("api/ShopTradeStats")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ShopTradeStat")]
    public class ShopTradeStatsController : ControllerBase
    {
        private readonly IShopTradeStatService _service;

        public ShopTradeStatsController(IShopTradeStatService service) => _service = service;

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Filters shopId, itemBlueprintId, from, to (UTC, on the hour).</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ShopTradeStatListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        /// <summary>Recomputes a range of hours from the ledger (idempotent).</summary>
        [HttpPost("rebuild")]
        public Task<IActionResult> Rebuild([FromBody] ShopTradeStatRebuildDto dto, CancellationToken ct) => ShopErrors.Run(async () =>
            dto == null ? BadRequest() : Ok(await _service.RebuildAsync(dto, ct)));
    }

    /// <summary>Town accounts (currency AccountKind.Town): read only; balances change through the ledger only.</summary>
    [ApiController]
    [Route("api/TownAccounts")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("TownAccount")]
    public class TownAccountsController : ControllerBase
    {
        private readonly ITownAccountService _service;

        public TownAccountsController(ITownAccountService service) => _service = service;

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpGet("by-town/{townId:int}")]
        public async Task<IActionResult> GetByTown(int townId, CancellationToken ct)
        {
            var item = await _service.GetByTownAsync(townId, ct);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Filter townId; search term = town name.</summary>
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<TownAccountListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));

        /// <summary>The account's town legs, newest first.</summary>
        [HttpGet("{id:int}/ledger")]
        public Task<IActionResult> Ledger(int id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
            ShopErrors.Run(async () => Ok(await _service.LedgerAsync(id, new PagedQueryDto { PageNumber = pageNumber, PageSize = pageSize }, ct)));
    }

    internal static class ShopErrors
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
            catch (ShopRefusedException ex)
            {
                return new ConflictObjectResult(new { code = ex.Code, error = ex.Message });
            }
            catch (CurrencyException ex)
            {
                return new ConflictObjectResult(new { code = ex.Code.ToString(), error = ex.Message });
            }
            catch (StockIdempotencyConflictException ex)
            {
                return new ConflictObjectResult(new { code = "IdempotencyKeyReuse", error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(new { error = ex.PlainMessage() });
            }
            catch (InvalidOperationException ex)
            {
                return new ConflictObjectResult(new { error = ex.Message });
            }
        }
    }
}

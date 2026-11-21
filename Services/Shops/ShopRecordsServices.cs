using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services.Shops
{
    // ================= Catalogue entries =================

    public interface IShopCatalogueService
    {
        Task<ShopCatalogueEntryDto?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<PagedResultDto<ShopCatalogueEntryListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);
        Task<ShopCatalogueEntryDto> CreateAsync(ShopCatalogueEntryDto dto, CancellationToken ct = default);
        Task<ShopCatalogueEntryDto> UpdateAsync(int id, ShopCatalogueEntryDto dto, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);
    }

    /// <summary>
    /// The static admin catalogue (shops DESIGN § 2.3): a blueprint (once per shop) or one item instance (once across
    /// shops), a static price, Stocked or Unlimited (blueprints only), a pin and an order. Works while
    /// <c>Production:Enabled</c> is off.
    /// </summary>
    public sealed class ShopCatalogueService : IShopCatalogueService
    {
        public const long MaxStaticPrice = 1_000_000_000L;

        private readonly KnKDbContext _db;

        public ShopCatalogueService(KnKDbContext db) => _db = db;

        public async Task<ShopCatalogueEntryDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<ShopCatalogueEntry>().AsNoTracking().Include(x => x.Shop).Include(x => x.ItemBlueprint)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return e == null ? null : new ShopCatalogueEntryDto
            {
                Id = e.Id, ShopId = e.ShopId, Shop = new NamedRefDto { Id = e.ShopId, Name = e.Shop.Name },
                ItemBlueprintId = e.ItemBlueprintId,
                ItemBlueprint = new NamedRefDto { Id = e.ItemBlueprintId, Name = e.ItemBlueprint.Name ?? e.ItemBlueprint.DefaultDisplayName },
                ItemInstanceId = e.ItemInstanceId, StaticPrice = e.StaticPrice, StockMode = e.StockMode.ToString(), Pinned = e.Pinned,
                SortOrder = e.SortOrder, Enabled = e.Enabled, CreatedAt = e.CreatedAt,
            };
        }

        public async Task<PagedResultDto<ShopCatalogueEntryListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ShopCatalogueEntry> q = _db.Set<ShopCatalogueEntry>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "shopId") is { } shopId) q = q.Where(e => e.ShopId == shopId);
            if (TerritoryPaging.IntFilter(query, "itemBlueprintId") is { } bid) q = q.Where(e => e.ItemBlueprintId == bid);
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(e => e.ItemBlueprint.Name != null && e.ItemBlueprint.Name.ToLower().Contains(term));
            int total = await q.CountAsync(ct);
            var items = await q.OrderBy(e => e.ShopId).ThenByDescending(e => e.Pinned).ThenBy(e => e.SortOrder).ThenBy(e => e.Id)
                .Skip(ShopEnum.Skip(query)).Take(ShopEnum.Size(query))
                .Select(e => new ShopCatalogueEntryListDto
                {
                    Id = e.Id, ShopId = e.ShopId, ShopName = e.Shop.Name, ItemBlueprintId = e.ItemBlueprintId,
                    ItemBlueprintName = e.ItemBlueprint.Name ?? e.ItemBlueprint.DefaultDisplayName, ItemInstanceId = e.ItemInstanceId,
                    StaticPrice = e.StaticPrice, StockMode = e.StockMode.ToString(), Pinned = e.Pinned, SortOrder = e.SortOrder, Enabled = e.Enabled,
                }).ToListAsync(ct);
            return ShopEnum.Page(items, total, query);
        }

        public async Task<ShopCatalogueEntryDto> CreateAsync(ShopCatalogueEntryDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.ShopId is not > 0 || !await _db.Set<Shop>().AnyAsync(s => s.Id == dto.ShopId, ct))
                throw new ArgumentException("shopId must name an existing shop.");
            var e = new ShopCatalogueEntry { ShopId = dto.ShopId.Value };
            if (dto.ItemInstanceId is > 0)
            {
                var bid = await _db.ItemInstances.AsNoTracking().Where(i => i.Id == dto.ItemInstanceId).Select(i => (int?)i.ItemBlueprintId).FirstOrDefaultAsync(ct)
                    ?? throw new ArgumentException($"Item instance {dto.ItemInstanceId} not found.");
                if (dto.ItemBlueprintId.HasValue && dto.ItemBlueprintId != bid)
                    throw new ArgumentException("itemBlueprintId must be the instance's blueprint (or left out).");
                if (await _db.Set<ShopCatalogueEntry>().AnyAsync(x => x.ItemInstanceId == dto.ItemInstanceId, ct))
                    throw new InvalidOperationException($"Item instance {dto.ItemInstanceId} is already in a shop's catalogue.");
                e.ItemInstanceId = dto.ItemInstanceId;
                e.ItemBlueprintId = bid;
            }
            else
            {
                if (dto.ItemBlueprintId is not > 0 || !await _db.ItemBlueprints.AnyAsync(b => b.Id == dto.ItemBlueprintId, ct))
                    throw new ArgumentException("itemBlueprintId must name an existing blueprint (or give an itemInstanceId).");
                if (await _db.Set<ShopCatalogueEntry>().AnyAsync(x => x.ShopId == e.ShopId && x.ItemInstanceId == null && x.ItemBlueprintId == dto.ItemBlueprintId, ct))
                    throw new InvalidOperationException("The shop's catalogue already lists that blueprint.");
                e.ItemBlueprintId = dto.ItemBlueprintId.Value;
            }
            Apply(e, dto);
            _db.Set<ShopCatalogueEntry>().Add(e);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(e.Id, ct))!;
        }

        public async Task<ShopCatalogueEntryDto> UpdateAsync(int id, ShopCatalogueEntryDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var e = await _db.Set<ShopCatalogueEntry>().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"ShopCatalogueEntry with id {id} not found.");
            if ((dto.ShopId.HasValue && dto.ShopId != e.ShopId) || (dto.ItemBlueprintId.HasValue && dto.ItemBlueprintId != e.ItemBlueprintId)
                || (dto.ItemInstanceId.HasValue && dto.ItemInstanceId != e.ItemInstanceId))
                throw new ArgumentException("An entry keeps its shop and item; delete it and add another instead.");
            Apply(e, dto);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(id, ct))!;
        }

        private static void Apply(ShopCatalogueEntry e, ShopCatalogueEntryDto dto)
        {
            if (dto.StaticPrice.HasValue)
            {
                if (dto.StaticPrice.Value is < 0 or > MaxStaticPrice) throw new ArgumentException($"staticPrice must be 0 to {MaxStaticPrice} (0 clears it).");
                e.StaticPrice = dto.StaticPrice.Value > 0 ? dto.StaticPrice.Value : null;
            }
            if (dto.StockMode != null) e.StockMode = ShopEnum.Parse<ShopStockMode>(dto.StockMode, "stockMode");
            if (e.StockMode == ShopStockMode.Unlimited && e.ItemInstanceId != null)
                throw new ArgumentException("A unique item can't be Unlimited: it is sold once, from Merchandise.");
            if (dto.Pinned.HasValue) e.Pinned = dto.Pinned.Value;
            if (dto.SortOrder.HasValue) e.SortOrder = dto.SortOrder.Value;
            if (dto.Enabled.HasValue) e.Enabled = dto.Enabled.Value;
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<ShopCatalogueEntry>().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"ShopCatalogueEntry with id {id} not found.");
            _db.Set<ShopCatalogueEntry>().Remove(e);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ================= Hourly trade stats =================

    public interface IShopTradeStatService
    {
        Task<ShopTradeStatDto?> GetByIdAsync(long id, CancellationToken ct = default);
        /// <summary>Filters shopId, itemBlueprintId, from, to (ISO times on HourStart).</summary>
        Task<PagedResultDto<ShopTradeStatListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);
        /// <summary>Recomputes the hours of the range from the ledger (idempotent: the rows of each shop and hour are
        /// replaced). Works while Production:Enabled is off.</summary>
        Task<ShopTradeStatRebuildResultDto> RebuildAsync(ShopTradeStatRebuildDto request, CancellationToken ct = default);
        /// <summary>The hourly job: the last completed hours (statsLookbackHours + 1). Does nothing while
        /// Production:Enabled is off.</summary>
        Task<ShopTradeStatRebuildResultDto> RollupAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// <see cref="ShopTradeStat"/> rows built from the ledger (shops DESIGN § 9.1): sales and buy-backs are the shop's
    /// <see cref="StockReason.ShopSale"/> and <see cref="StockReason.ShopBuyBack"/> transactions (source Shop, the shop's
    /// id) with their currency posting under the same idempotency key; supply is what came into Merchandise from
    /// production, transport and distribution. Rebuilding an hour gives the same rows.
    /// </summary>
    public sealed class ShopTradeStatService : IShopTradeStatService
    {
        public const int MaxRebuildHours = 24 * 31;
        private static readonly StockReason[] SupplyReasons = { StockReason.Production, StockReason.TransportDeliver, StockReason.Distribution };

        private readonly KnKDbContext _db;
        private readonly IProductionSettingsReader _settings;
        private readonly TimeProvider _clock;

        public ShopTradeStatService(KnKDbContext db, IProductionSettingsReader settings, TimeProvider? clock = null)
        {
            _db = db;
            _settings = settings;
            _clock = clock ?? TimeProvider.System;
        }

        public static DateTime HourOf(DateTime t)
        {
            var u = t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;
            return new DateTime(u.Year, u.Month, u.Day, u.Hour, 0, 0, DateTimeKind.Utc);
        }

        public async Task<ShopTradeStatDto?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            var s = await _db.Set<ShopTradeStat>().AsNoTracking().Include(x => x.Shop).Include(x => x.ItemBlueprint)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return s == null ? null : new ShopTradeStatDto
            {
                Id = s.Id, ShopId = s.ShopId, Shop = new NamedRefDto { Id = s.ShopId, Name = s.Shop.Name }, ItemBlueprintId = s.ItemBlueprintId,
                ItemBlueprint = new NamedRefDto { Id = s.ItemBlueprintId, Name = s.ItemBlueprint.Name ?? s.ItemBlueprint.DefaultDisplayName },
                HourStart = s.HourStart, UnitsSold = s.UnitsSold, Revenue = s.Revenue, UnitsBoughtBack = s.UnitsBoughtBack,
                BuyBackPaid = s.BuyBackPaid, UnitsSupplied = s.UnitsSupplied, DistinctCustomers = s.DistinctCustomers,
                TownPlayerCount = s.TownPlayerCount, AvgSellPrice = s.AvgSellPrice, StockAtEnd = s.StockAtEnd, ComputedAt = s.ComputedAt,
            };
        }

        public async Task<PagedResultDto<ShopTradeStatListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ShopTradeStat> q = _db.Set<ShopTradeStat>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "shopId") is { } shopId) q = q.Where(s => s.ShopId == shopId);
            if (TerritoryPaging.IntFilter(query, "itemBlueprintId") is { } bid) q = q.Where(s => s.ItemBlueprintId == bid);
            if (query.Filters != null && query.Filters.TryGetValue("from", out var f) && DateTime.TryParse(f, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var from))
                q = q.Where(s => s.HourStart >= from);
            if (query.Filters != null && query.Filters.TryGetValue("to", out var t) && DateTime.TryParse(t, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var to))
                q = q.Where(s => s.HourStart < to);
            int total = await q.CountAsync(ct);
            var items = await q.OrderByDescending(s => s.HourStart).ThenBy(s => s.ShopId).ThenBy(s => s.ItemBlueprintId)
                .Skip(ShopEnum.Skip(query)).Take(ShopEnum.Size(query))
                .Select(s => new ShopTradeStatListDto
                {
                    Id = s.Id, ShopId = s.ShopId, ShopName = s.Shop.Name, ItemBlueprintId = s.ItemBlueprintId,
                    ItemBlueprintName = s.ItemBlueprint.Name ?? s.ItemBlueprint.DefaultDisplayName, HourStart = s.HourStart,
                    UnitsSold = s.UnitsSold, Revenue = s.Revenue, UnitsBoughtBack = s.UnitsBoughtBack, BuyBackPaid = s.BuyBackPaid,
                    UnitsSupplied = s.UnitsSupplied, StockAtEnd = s.StockAtEnd,
                }).ToListAsync(ct);
            return ShopEnum.Page(items, total, query);
        }

        public async Task<ShopTradeStatRebuildResultDto> RollupAsync(CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            if (!settings.Enabled) return new ShopTradeStatRebuildResultDto();
            var current = HourOf(_clock.GetUtcNow().UtcDateTime);
            int back = settings.Shops?.StatsLookbackHours ?? 2;
            return await BuildAsync(null, current.AddHours(-(back + 1)), current, ct);
        }

        public Task<ShopTradeStatRebuildResultDto> RebuildAsync(ShopTradeStatRebuildDto request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var from = HourOf(request.From);
            var to = HourOf(request.To);
            if (to <= from) throw new ArgumentException("to must be after from (whole UTC hours).");
            if ((to - from).TotalHours > MaxRebuildHours) throw new ArgumentException($"At most {MaxRebuildHours} hours per rebuild.");
            var now = HourOf(_clock.GetUtcNow().UtcDateTime).AddHours(1);
            if (to > now) to = now;
            return BuildAsync(request.ShopId, from, to, ct);
        }

        private sealed record Move(long TxId, int? ActorUserId, DateTime At, StockReason Reason, int From, int To, int Blueprint, long Amount,
            string? Key, string? SourceRef);

        private async Task<ShopTradeStatRebuildResultDto> BuildAsync(int? shopId, DateTime from, DateTime to, CancellationToken ct)
        {
            var result = new ShopTradeStatRebuildResultDto { Hours = (int)(to - from).TotalHours };
            var shops = await _db.Set<Shop>().AsNoTracking().Where(s => shopId == null || s.Id == shopId).Select(s => s.Id).ToListAsync(ct);
            if (shopId != null && shops.Count == 0) throw new KeyNotFoundException($"Shop with id {shopId} not found.");
            var merch = await _db.Storages.AsNoTracking()
                .Where(s => s.Kind == StorageKind.Domain && s.Purpose == StoragePurpose.Merchandise && s.DomainId != null && shops.Contains(s.DomainId.Value))
                .Select(s => new { s.Id, ShopId = s.DomainId!.Value }).ToListAsync(ct);
            var shopOfStorage = merch.ToDictionary(m => m.Id, m => m.ShopId);
            var storageIds = merch.Select(m => m.Id).ToList();
            var shopRefs = shops.Select(s => s.ToString()).ToList();

            // Everything that touched the Merchandise storages from `from` on (for the hours and for stock at the end).
            var moves = (await _db.StockMoves.AsNoTracking()
                    .Where(m => m.CreatedAt >= from && m.ItemBlueprintId != null && (storageIds.Contains(m.FromStorageId) || storageIds.Contains(m.ToStorageId)))
                    .Select(m => new Move(m.TransactionId, m.Transaction.ActorUserId, m.CreatedAt, m.Reason, m.FromStorageId, m.ToStorageId,
                        m.ItemBlueprintId!.Value, m.Amount, m.Transaction.IdempotencyKey, m.Transaction.SourceRef)).ToListAsync(ct))
                .ToList();
            var unitMoves = await _db.StockMoves.AsNoTracking()
                .Where(m => m.CreatedAt >= from && m.ItemInstanceId != null && (storageIds.Contains(m.FromStorageId) || storageIds.Contains(m.ToStorageId)))
                .Select(m => new Move(m.TransactionId, m.Transaction.ActorUserId, m.CreatedAt, m.Reason, m.FromStorageId, m.ToStorageId,
                    m.ItemInstance!.ItemBlueprintId, m.Amount, m.Transaction.IdempotencyKey, m.Transaction.SourceRef)).ToListAsync(ct);
            moves.AddRange(unitMoves);
            // Unlimited catalogue sales never touch Merchandise: they are the shop's ShopSale transactions from SYS_ADJUSTMENT.
            var unlimited = await _db.StockMoves.AsNoTracking()
                .Where(m => m.CreatedAt >= from && m.CreatedAt < to && m.Reason == StockReason.ShopSale && m.ItemBlueprintId != null
                    && m.Transaction.SourceType == ShopTradingService.StockSourceType && m.Transaction.SourceRef != null && shopRefs.Contains(m.Transaction.SourceRef)
                    && !storageIds.Contains(m.FromStorageId))
                .Select(m => new Move(m.TransactionId, m.Transaction.ActorUserId, m.CreatedAt, m.Reason, m.FromStorageId, m.ToStorageId,
                    m.ItemBlueprintId!.Value, m.Amount, m.Transaction.IdempotencyKey, m.Transaction.SourceRef)).ToListAsync(ct);

            var keys = moves.Concat(unlimited).Where(m => m.Reason is StockReason.ShopSale or StockReason.ShopBuyBack && m.Key != null)
                .Select(m => m.Key!).Distinct().ToList();
            var paid = (await _db.CurrencyEntries.AsNoTracking()
                    .Where(e => e.AccountKind == CurrencyAccountKind.User && keys.Contains(e.Transaction.IdempotencyKey)
                        && e.Transaction.IdempotencyScope == CurrencyIdempotencyScopes.Plugin
                        && (e.Transaction.ReasonCode == CurrencyReasons.ShopPurchase || e.Transaction.ReasonCode == CurrencyReasons.ShopBuyBack))
                    .Select(e => new { e.Transaction.IdempotencyKey, e.Amount }).ToListAsync(ct))
                .GroupBy(e => e.IdempotencyKey).ToDictionary(g => g.Key, g => Math.Abs(g.Sum(x => x.Amount)));

            // Stock now per (storage, blueprint), lines plus units.
            var lineStock = await _db.StockLines.AsNoTracking().Where(l => storageIds.Contains(l.StorageId))
                .Select(l => new { l.StorageId, l.ItemBlueprintId, l.Amount }).ToListAsync(ct);
            var unitStock = await _db.StockUnits.AsNoTracking().Where(u => storageIds.Contains(u.StorageId))
                .Select(u => new { u.StorageId, u.ItemInstance.ItemBlueprintId }).ToListAsync(ct);
            var stockNow = new Dictionary<(int Shop, int Blueprint), long>();
            foreach (var l in lineStock) Add(stockNow, (shopOfStorage[l.StorageId], l.ItemBlueprintId), l.Amount);
            foreach (var u in unitStock) Add(stockNow, (shopOfStorage[u.StorageId], u.ItemBlueprintId), 1);

            var rows = new List<ShopTradeStat>();
            var computedAt = _clock.GetUtcNow().UtcDateTime;
            for (var hour = from; hour < to; hour = hour.AddHours(1))
            {
                var end = hour.AddHours(1);
                var inHour = moves.Where(m => m.At >= hour && m.At < end).ToList();
                var unlimitedInHour = unlimited.Where(m => m.At >= hour && m.At < end).ToList();
                var groups = new Dictionary<(int Shop, int Blueprint), ShopTradeStat>();
                ShopTradeStat Row((int Shop, int Blueprint) k) =>
                    groups.TryGetValue(k, out var r) ? r : groups[k] = new ShopTradeStat { ShopId = k.Shop, ItemBlueprintId = k.Blueprint, HourStart = hour, ComputedAt = computedAt };
                var customers = new Dictionary<(int, int), HashSet<int>>();
                foreach (var m in inHour)
                {
                    bool outOf = shopOfStorage.TryGetValue(m.From, out var fromShop);
                    bool into = shopOfStorage.TryGetValue(m.To, out var toShop);
                    if (m.Reason == StockReason.ShopSale && outOf) Sale((fromShop, m.Blueprint), m);
                    else if (m.Reason == StockReason.ShopBuyBack && into)
                    {
                        var r = Row((toShop, m.Blueprint));
                        r.UnitsBoughtBack += m.Amount;
                        r.BuyBackPaid += m.Key != null ? paid.GetValueOrDefault(m.Key) : 0;
                    }
                    else if (into && SupplyReasons.Contains(m.Reason)) Row((toShop, m.Blueprint)).UnitsSupplied += m.Amount;
                }
                foreach (var m in unlimitedInHour)
                    if (int.TryParse(m.SourceRef, out var s)) Sale((s, m.Blueprint), m);

                void Sale((int Shop, int Blueprint) k, Move m)
                {
                    var r = Row(k);
                    r.UnitsSold += m.Amount;
                    r.Revenue += m.Key != null ? paid.GetValueOrDefault(m.Key) : 0;
                    if (m.ActorUserId is { } u)
                    {
                        if (!customers.TryGetValue(k, out var set)) customers[k] = set = new HashSet<int>();
                        set.Add(u);
                    }
                }

                foreach (var (k, r) in groups)
                {
                    r.DistinctCustomers = customers.TryGetValue(k, out var set) ? set.Count : 0;
                    r.AvgSellPrice = r.UnitsSold > 0 ? Math.Round((decimal)r.Revenue / r.UnitsSold, 4) : null;
                    // Stock at the end of the hour = now − what came in since + what went out since.
                    long net = 0;
                    foreach (var m in moves.Where(m => m.At >= end && m.Blueprint == k.Blueprint))
                    {
                        if (shopOfStorage.TryGetValue(m.To, out var ts) && ts == k.Shop) net += m.Amount;
                        if (shopOfStorage.TryGetValue(m.From, out var fs) && fs == k.Shop) net -= m.Amount;
                    }
                    r.StockAtEnd = Math.Max(0, stockNow.GetValueOrDefault(k) - net);
                    rows.Add(r);
                }
            }

            await using var tx = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(ct) : null;
            var stale = await _db.Set<ShopTradeStat>().Where(s => s.HourStart >= from && s.HourStart < to && shops.Contains(s.ShopId)).ToListAsync(ct);
            _db.Set<ShopTradeStat>().RemoveRange(stale);
            await _db.SaveChangesAsync(ct);
            _db.Set<ShopTradeStat>().AddRange(rows);
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            result.RowsDeleted = stale.Count;
            result.RowsWritten = rows.Count;
            return result;
        }

        private static void Add(Dictionary<(int, int), long> d, (int, int) k, long v) => d[k] = d.GetValueOrDefault(k) + v;
    }

    /// <summary>The hourly trade-stats job (shops DESIGN § 9.1): every 10 minutes it rolls up the last completed hours;
    /// idempotent, so running often costs little. Nothing while Production:Enabled is off.</summary>
    public sealed class ShopTradeStatSweepService : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ShopTradeStatSweepService> _logger;

        public ShopTradeStatSweepService(IServiceScopeFactory scopes, ILogger<ShopTradeStatSweepService> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IShopTradeStatService>().RollupAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Shop trade stats rollup failed; retrying next interval.");
                }
            }
        }
    }

    // ================= Town accounts =================

    public interface ITownAccountService
    {
        Task<TownAccountDto?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<TownAccountDto?> GetByTownAsync(int townId, CancellationToken ct = default);
        Task<PagedResultDto<TownAccountListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);
        /// <summary>The town legs of the account, newest first.</summary>
        Task<PagedResultDto<TownAccountEntryDto>> LedgerAsync(int id, PagedQueryDto query, CancellationToken ct = default);
    }

    /// <summary>Reads of the town accounts (currency AccountKind.Town). Balances change only through the currency ledger
    /// (shop purchases and buy-backs); <c>reconciled</c> compares the stored balance with the last town leg.</summary>
    public sealed class TownAccountService : ITownAccountService
    {
        private readonly KnKDbContext _db;

        public TownAccountService(KnKDbContext db) => _db = db;

        public async Task<TownAccountDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var a = await _db.Set<TownAccount>().AsNoTracking().Include(x => x.Town).FirstOrDefaultAsync(x => x.Id == id, ct);
            return a == null ? null : await ToDtoAsync(a, ct);
        }

        public async Task<TownAccountDto?> GetByTownAsync(int townId, CancellationToken ct = default)
        {
            var a = await _db.Set<TownAccount>().AsNoTracking().Include(x => x.Town).FirstOrDefaultAsync(x => x.TownId == townId, ct);
            return a == null ? null : await ToDtoAsync(a, ct);
        }

        private async Task<TownAccountDto> ToDtoAsync(TownAccount a, CancellationToken ct)
        {
            var last = await _db.CurrencyEntries.AsNoTracking()
                .Where(e => e.AccountKind == CurrencyAccountKind.Town && e.TownId == a.TownId && e.Currency == Currency.Coins)
                .OrderByDescending(e => e.Id).Select(e => e.BalanceAfter).FirstOrDefaultAsync(ct);
            return new TownAccountDto
            {
                Id = a.Id, TownId = a.TownId, Town = new NamedRefDto { Id = a.TownId, Name = a.Town?.Name ?? string.Empty }, Coins = a.Coins,
                LedgerBalance = last, Reconciled = (last ?? 0) == a.Coins, CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt,
            };
        }

        public async Task<PagedResultDto<TownAccountListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<TownAccount> q = _db.Set<TownAccount>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "townId") is { } townId) q = q.Where(a => a.TownId == townId);
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(a => a.Town.Name.ToLower().Contains(term));
            int total = await q.CountAsync(ct);
            var items = await q.OrderBy(a => a.Town.Name).ThenBy(a => a.Id).Skip(ShopEnum.Skip(query)).Take(ShopEnum.Size(query))
                .Select(a => new TownAccountListDto { Id = a.Id, TownId = a.TownId, TownName = a.Town.Name, Coins = a.Coins, UpdatedAt = a.UpdatedAt })
                .ToListAsync(ct);
            return ShopEnum.Page(items, total, query);
        }

        public async Task<PagedResultDto<TownAccountEntryDto>> LedgerAsync(int id, PagedQueryDto query, CancellationToken ct = default)
        {
            query ??= new PagedQueryDto();
            var townId = await _db.Set<TownAccount>().AsNoTracking().Where(a => a.Id == id).Select(a => (int?)a.TownId).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"TownAccount with id {id} not found.");
            var q = _db.CurrencyEntries.AsNoTracking().Where(e => e.AccountKind == CurrencyAccountKind.Town && e.TownId == townId);
            int total = await q.CountAsync(ct);
            var page = await q.OrderByDescending(e => e.Id).Skip(ShopEnum.Skip(query)).Take(ShopEnum.Size(query))
                .Select(e => new
                {
                    Row = new TownAccountEntryDto
                    {
                        EntryId = e.Id, TransactionPublicId = e.Transaction.PublicId, ReasonCode = e.Transaction.ReasonCode, Amount = e.Amount,
                        BalanceBefore = e.BalanceBefore ?? 0, BalanceAfter = e.BalanceAfter ?? 0, SourceType = e.Transaction.SourceType,
                        SourceRef = e.Transaction.SourceRef, UserId = e.Transaction.InitiatorUserId, CreatedAt = e.Transaction.CreatedAt,
                    },
                    e.Transaction.IdempotencyScope,
                    e.Transaction.IdempotencyKey,
                }).ToListAsync(ct);
            var rows = page.Select(p => p.Row).ToList();

            // The player's username (#938 point 2), one query per page.
            var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
            if (userIds.Count > 0)
            {
                var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
                foreach (var r in rows)
                    if (r.UserId.HasValue && names.TryGetValue(r.UserId.Value, out var name)) r.Username = name;
            }

            // A shop purchase or buy-back's stock transaction (#938 point 3): the trading service posts both legs under the
            // same plugin idempotency key, the same join the trade stats use.
            var keys = page.Where(p => p.IdempotencyScope == CurrencyIdempotencyScopes.Plugin && CurrencyReasons.IsTownReason(p.Row.ReasonCode))
                .Select(p => p.IdempotencyKey).Distinct().ToList();
            if (keys.Count > 0)
            {
                var stock = (await _db.StockTransactions.AsNoTracking()
                        .Where(t => t.SourceType == ShopTradingService.StockSourceType && t.IdempotencyKey != null && keys.Contains(t.IdempotencyKey))
                        .Select(t => new { t.Id, t.IdempotencyKey }).ToListAsync(ct))
                    .GroupBy(t => t.IdempotencyKey!).ToDictionary(g => g.Key, g => g.Min(t => t.Id));
                foreach (var p in page)
                    if (p.IdempotencyScope == CurrencyIdempotencyScopes.Plugin && CurrencyReasons.IsTownReason(p.Row.ReasonCode)
                        && stock.TryGetValue(p.IdempotencyKey, out var stockId))
                        p.Row.StockTransactionId = stockId;
            }
            return ShopEnum.Page(rows, total, query);
        }
    }
}

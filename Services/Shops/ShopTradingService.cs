using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Shops
{
    /// <summary>A purchase or sale the shop refuses (409 with <see cref="Code"/>): PriceChanged, SoldOut, NotEnoughStock,
    /// NotForSale, InsufficientFunds, TownFundsInsufficient, NoTown, InboxFull, NoRoom, CategoryNotAccepted, NoPrice, ...</summary>
    public sealed class ShopRefusedException : InvalidOperationException
    {
        public string Code { get; }

        public ShopRefusedException(string code, string message) : base(message) => Code = code;
    }

    public interface IShopTradingService
    {
        /// <summary>What the shop offers now, with prices and stock (the shop menu, display stalls).</summary>
        Task<ShopOfferDto> OfferAsync(int shopId, CancellationToken ct = default);

        /// <summary>The price of a purchase now; <c>refusal</c> says why it can't be bought. Changes nothing.</summary>
        Task<ShopQuoteDto> QuoteAsync(int shopId, ShopPurchaseRequestDto request, CancellationToken ct = default);

        /// <summary>
        /// A confirmed purchase (shops DESIGN § 3.1), in one database transaction: the coins from the player to the town's
        /// account (<c>SHOP_PURCHASE</c>), the goods Merchandise → <c>SYS_WORLD</c> (<see cref="StockReason.ShopSale"/>),
        /// and for <c>Inbox</c> the P7 delivery into the player's inbox. Nothing is taken without the item. Idempotent on
        /// the key. Works while <c>Production:Enabled</c> is off, except delivery to the inbox.
        /// </summary>
        Task<ShopPurchaseResultDto> PurchaseAsync(int shopId, ShopPurchaseRequestDto request, CancellationToken ct = default);

        /// <summary>The sell menu's buy-back prices; changes nothing. Needs <c>Production:Enabled</c>.</summary>
        Task<ShopSellQuoteDto> SellQuoteAsync(int shopId, ShopSellRequestDto request, CancellationToken ct = default);

        /// <summary>
        /// A confirmed sale (shops DESIGN § 3.2), all lines or none, in one database transaction: per line the goods
        /// <c>SYS_WORLD</c> → Merchandise (<see cref="StockReason.ShopBuyBack"/>; a unique item becomes a stock unit and is
        /// resold as that exact item) and the buy-back price from the town's account (<c>SHOP_BUYBACK</c>). Needs
        /// <c>Production:Enabled</c> (409 ProductionDisabled).
        /// </summary>
        Task<ShopSellResultDto> SellAsync(int shopId, ShopSellRequestDto request, CancellationToken ct = default);
    }

    public sealed class ShopTradingService : IShopTradingService
    {
        public const string Component = "shops";
        public const int MaxClientKeyLength = 60;
        public const int MaxSellLines = 54;
        public const long MaxAmount = 100_000;
        public const string StockSourceType = "Shop";
        public const string PurchaseSourceType = "ShopPurchase";
        public const string BuyBackSourceType = "ShopBuyBack";
        private static readonly Regex KeyPattern = new("^[A-Za-z0-9:_.\\-]{1," + MaxClientKeyLength + "}$", RegexOptions.Compiled);

        private readonly KnKDbContext _db;
        private readonly IStockService _stock;
        private readonly ICurrencyTownService _currency;
        private readonly IProductionSettingsReader _settings;
        private readonly IItemDeliveryService? _delivery;

        public ShopTradingService(KnKDbContext db, IStockService stock, ICurrencyTownService currency, IProductionSettingsReader settings,
            IItemDeliveryService? delivery = null)
        {
            _db = db;
            _stock = stock;
            _currency = currency;
            _settings = settings;
            _delivery = delivery;
        }

        public static string PurchaseKey(string clientKey) => $"shop-buy:{clientKey}";

        public static string SellKey(string clientKey, int lineNo) => $"shop-sell:{clientKey}:{lineNo}";

        // ================= Context =================

        private sealed class ShopContext
        {
            public Shop Shop { get; init; } = null!;
            public int? TownId { get; init; }
            public string? TownName { get; init; }
            public ShopTypeLevel? Level { get; init; }
            public Storage? Merchandise { get; init; }
            public HashSet<int> AcceptedCategories { get; init; } = new();
            public ProductionSettingsDto Settings { get; init; } = null!;
            public decimal PriceMultiplier => Level?.PriceMultiplier > 0 ? Level.PriceMultiplier : 1m;
        }

        private async Task<ShopContext> LoadAsync(int shopId, CancellationToken ct)
        {
            var shop = await _db.Set<Shop>().AsNoTracking().Include(s => s.ShopType).Include(s => s.District)
                .FirstOrDefaultAsync(s => s.Id == shopId, ct)
                ?? throw new KeyNotFoundException($"Shop with id {shopId} not found.");
            int? townId = shop.District?.TownId ?? shop.TownId;
            var townName = townId == null ? null : await _db.Towns.AsNoTracking().Where(t => t.Id == townId).Select(t => t.Name).FirstOrDefaultAsync(ct);
            var level = await ShopLevels.FindAsync(_db, shop.ShopTypeId, shop.Level, ct);
            var merchandise = await _db.Storages.AsNoTracking()
                .Where(s => s.Kind == StorageKind.Domain && s.DomainId == shopId && s.Purpose == StoragePurpose.Merchandise)
                .OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
            var accepted = shop.ShopTypeId == null ? new List<int>()
                : await _db.Set<ShopTypeCategory>().AsNoTracking().Where(c => c.ShopTypeId == shop.ShopTypeId).Select(c => c.CategoryId).ToListAsync(ct);
            return new ShopContext
            {
                Shop = shop, TownId = townId, TownName = townName, Level = level, Merchandise = merchandise,
                AcceptedCategories = accepted.ToHashSet(), Settings = await _settings.GetAsync(),
            };
        }

        private sealed record Priced(int BlueprintId, string BlueprintName, long? InstanceId, ShopCatalogueEntry? Entry, ShopPrice Price,
            long? Stock, List<string>? Enchantments);

        private async Task<Dictionary<int, (string Name, decimal Base, int? CategoryId)>> BlueprintsAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            var list = ids.Distinct().ToList();
            return (await _db.ItemBlueprints.AsNoTracking().Where(b => list.Contains(b.Id))
                    .Select(b => new { b.Id, b.Name, b.DefaultDisplayName, b.BasePriceMin, b.BasePriceMax, b.CategoryId }).ToListAsync(ct))
                .ToDictionary(b => b.Id, b => (string.IsNullOrWhiteSpace(b.Name) ? b.DefaultDisplayName : b.Name!,
                    ShopPricing.BasePrice(b.BasePriceMin, b.BasePriceMax), b.CategoryId));
        }

        private async Task<Dictionary<long, List<(string Name, int Level)>>> EnchantmentsAsync(IEnumerable<long> instanceIds, CancellationToken ct)
        {
            var list = instanceIds.Distinct().ToList();
            if (list.Count == 0) return new();
            var rows = await _db.Set<ItemInstanceEnchantment>().AsNoTracking().Where(e => list.Contains(e.ItemInstanceId))
                .Select(e => new { e.ItemInstanceId, e.Level, Name = e.EnchantmentDefinition.DisplayName ?? e.EnchantmentDefinition.Key })
                .ToListAsync(ct);
            return rows.GroupBy(r => r.ItemInstanceId).ToDictionary(g => g.Key, g => g.Select(r => (r.Name ?? "enchantment", r.Level)).ToList());
        }

        private static (string Name, decimal Base, int? CategoryId) Bp(Dictionary<int, (string Name, decimal Base, int? CategoryId)> map, int id) =>
            map.TryGetValue(id, out var b) ? b : ($"blueprint {id}", 0m, null);

        private static ShopPrice PriceOf(ShopContext c, long? staticPrice, decimal basePrice, IEnumerable<int>? enchantmentLevels) =>
            ShopPricing.SellPrice(staticPrice, basePrice, c.Shop.TaxModifier, c.PriceMultiplier,
                enchantmentLevels == null ? 1m : ShopPricing.EnchantmentFactor(enchantmentLevels, c.Settings.Shops!.EnchantmentPercentPerLevel));

        /// <summary>Every item on offer: entries first (by pin, order), then Merchandise contents without an entry.</summary>
        private async Task<List<Priced>> PriceAllAsync(ShopContext c, CancellationToken ct)
        {
            var entries = await _db.Set<ShopCatalogueEntry>().AsNoTracking().Where(e => e.ShopId == c.Shop.Id && e.Enabled)
                .OrderByDescending(e => e.Pinned).ThenBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync(ct);
            var lines = c.Merchandise == null ? new List<(int Blueprint, long Amount)>()
                : (await _db.StockLines.AsNoTracking().Where(l => l.StorageId == c.Merchandise.Id && l.Amount > 0)
                    .Select(l => new { l.ItemBlueprintId, l.Amount }).ToListAsync(ct)).Select(l => (Blueprint: l.ItemBlueprintId, l.Amount)).ToList();
            var units = c.Merchandise == null ? new List<(long Instance, int Blueprint)>()
                : (await _db.StockUnits.AsNoTracking().Where(u => u.StorageId == c.Merchandise.Id)
                    .Select(u => new { u.ItemInstanceId, u.ItemInstance.ItemBlueprintId }).ToListAsync(ct)).Select(u => (Instance: u.ItemInstanceId, Blueprint: u.ItemBlueprintId)).ToList();
            var blueprints = await BlueprintsAsync(entries.Select(e => e.ItemBlueprintId).Concat(lines.Select(l => l.Blueprint)).Concat(units.Select(u => u.Blueprint)), ct);
            var enchants = await EnchantmentsAsync(units.Select(u => u.Instance).Concat(entries.Where(e => e.ItemInstanceId.HasValue).Select(e => e.ItemInstanceId!.Value)), ct);
            var stockOf = lines.ToDictionary(l => l.Blueprint, l => l.Amount);
            var unitSet = units.Select(u => u.Instance).ToHashSet();

            var result = new List<Priced>();
            foreach (var e in entries)
            {
                var (name, basePrice, _) = Bp(blueprints, e.ItemBlueprintId);
                if (e.ItemInstanceId is { } inst)
                {
                    var levels = enchants.GetValueOrDefault(inst);
                    result.Add(new Priced(e.ItemBlueprintId, name, inst, e, PriceOf(c, e.StaticPrice, basePrice, levels?.Select(x => x.Level) ?? Array.Empty<int>()),
                        unitSet.Contains(inst) ? 1 : 0, levels?.Select(x => $"{x.Name} {x.Level}").ToList()));
                }
                else
                {
                    long? stock = e.StockMode == ShopStockMode.Unlimited ? null : stockOf.GetValueOrDefault(e.ItemBlueprintId);
                    result.Add(new Priced(e.ItemBlueprintId, name, null, e, PriceOf(c, e.StaticPrice, basePrice, null), stock, null));
                }
            }
            var listedBlueprints = entries.Where(e => e.ItemInstanceId == null).Select(e => e.ItemBlueprintId).ToHashSet();
            var listedInstances = entries.Where(e => e.ItemInstanceId != null).Select(e => e.ItemInstanceId!.Value).ToHashSet();
            foreach (var (bid, amount) in lines.Where(l => !listedBlueprints.Contains(l.Blueprint)).OrderBy(l => l.Blueprint))
            {
                var (name, basePrice, _) = Bp(blueprints, bid);
                result.Add(new Priced(bid, name, null, null, PriceOf(c, null, basePrice, null), amount, null));
            }
            foreach (var (inst, bid) in units.Where(u => !listedInstances.Contains(u.Instance)).OrderBy(u => u.Instance))
            {
                var (name, basePrice, _) = Bp(blueprints, bid);
                var levels = enchants.GetValueOrDefault(inst);
                result.Add(new Priced(bid, name, inst, null, PriceOf(c, null, basePrice, levels?.Select(x => x.Level) ?? Array.Empty<int>()), 1,
                    levels?.Select(x => $"{x.Name} {x.Level}").ToList()));
            }
            return result;
        }

        // ================= Offer and quote =================

        public async Task<ShopOfferDto> OfferAsync(int shopId, CancellationToken ct = default)
        {
            var c = await LoadAsync(shopId, ct);
            var priced = await PriceAllAsync(c, ct);
            var items = priced
                .Where(p => p.Price.Source != ShopPriceSource.None)
                .Select(p => new ShopOfferItemDto
                {
                    CatalogueEntryId = p.Entry?.Id, ItemBlueprintId = p.BlueprintId, ItemBlueprintName = p.BlueprintName,
                    ItemInstanceId = p.InstanceId, UnitPrice = p.Price.UnitPrice, PriceSource = p.Price.Source.ToString(),
                    Stock = p.Stock, SoldOut = p.Stock is <= 0, Pinned = p.Entry?.Pinned ?? false, SortOrder = p.Entry?.SortOrder ?? int.MaxValue,
                    Enchantments = p.Enchantments,
                })
                .Where(i => !(c.Shop.HideSoldOut && i.SoldOut))
                .ToList();
            return new ShopOfferDto
            {
                ShopId = c.Shop.Id, ShopName = c.Shop.Name, Kind = c.Shop.Kind.ToString(), Level = c.Shop.Level,
                ShopTypeName = c.Shop.ShopType?.Name, TownId = c.TownId, TownName = c.TownName, HideSoldOut = c.Shop.HideSoldOut,
                BuysFromPlayers = c.Settings.Enabled && c.AcceptedCategories.Count > 0, Items = items,
            };
        }

        private async Task<(ShopQuoteDto Quote, Priced? Item)> QuoteCoreAsync(ShopContext c, ShopPurchaseRequestDto r, CancellationToken ct)
        {
            if (r.Amount is < 1 or > MaxAmount) throw new ArgumentException($"amount must be 1 to {MaxAmount}.");
            var priced = await PriceAllAsync(c, ct);
            Priced? item;
            if (r.CatalogueEntryId is { } entryId)
            {
                item = priced.FirstOrDefault(p => p.Entry?.Id == entryId);
                if (item == null)
                {
                    if (!await _db.Set<ShopCatalogueEntry>().AnyAsync(e => e.Id == entryId && e.ShopId == c.Shop.Id, ct))
                        throw new KeyNotFoundException($"Catalogue entry {entryId} is not in shop {c.Shop.Id}.");
                    return (Refused(c, r, "NotForSale", "That catalogue entry is switched off."), null);
                }
            }
            else if (r.ItemInstanceId is { } inst) item = priced.FirstOrDefault(p => p.InstanceId == inst);
            else if (r.ItemBlueprintId is { } bid) item = priced.FirstOrDefault(p => p.InstanceId == null && p.BlueprintId == bid);
            else throw new ArgumentException("A purchase needs catalogueEntryId, itemInstanceId or itemBlueprintId.");

            if (item == null) return (Refused(c, r, "NotForSale", "The shop doesn't sell that item."), null);
            var quote = new ShopQuoteDto
            {
                ShopId = c.Shop.Id, CatalogueEntryId = item.Entry?.Id, ItemBlueprintId = item.BlueprintId, ItemBlueprintName = item.BlueprintName,
                ItemInstanceId = item.InstanceId, Amount = r.Amount, PriceSource = item.Price.Source.ToString(), Stock = item.Stock,
            };
            if (item.InstanceId.HasValue && r.Amount != 1) throw new ArgumentException("A unique item is bought one at a time (amount 1).");
            if (item.Price.UnitPrice is not { } unit)
                return (Refuse(quote, "NotForSale", "The item has no price (no base price and no static price)."), item);
            quote.UnitPrice = unit;
            try
            {
                quote.TotalPrice = checked(unit * r.Amount);
            }
            catch (OverflowException)
            {
                throw new ArgumentException("The total price is too large.");
            }
            if (item.Stock is <= 0) return (Refuse(quote, "SoldOut", "Sold out."), item);
            if (item.Stock is { } s && s < r.Amount) return (Refuse(quote, "NotEnoughStock", $"Only {s} in stock."), item);
            if (c.TownId == null) return (Refuse(quote, "NoTown", "The shop has no town to pay."), item);
            return (quote, item);
        }

        private static ShopQuoteDto Refused(ShopContext c, ShopPurchaseRequestDto r, string code, string message) => new()
        {
            ShopId = c.Shop.Id, CatalogueEntryId = r.CatalogueEntryId, ItemBlueprintId = r.ItemBlueprintId ?? 0, ItemInstanceId = r.ItemInstanceId,
            Amount = r.Amount, PriceSource = nameof(ShopPriceSource.None), Refusal = code, RefusalMessage = message,
        };

        private static ShopQuoteDto Refuse(ShopQuoteDto q, string code, string message)
        {
            q.Refusal = code;
            q.RefusalMessage = message;
            return q;
        }

        public async Task<ShopQuoteDto> QuoteAsync(int shopId, ShopPurchaseRequestDto request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var c = await LoadAsync(shopId, ct);
            return (await QuoteCoreAsync(c, request, ct)).Quote;
        }

        // ================= Purchase =================

        private static string ClientKey(string? key)
        {
            var k = key?.Trim();
            if (string.IsNullOrEmpty(k) || !KeyPattern.IsMatch(k))
                throw new ArgumentException($"idempotencyKey is required: 1-{MaxClientKeyLength} characters of A-Z, a-z, 0-9, ':', '_', '.', '-'.");
            return k;
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
            _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;

        public async Task<ShopPurchaseResultDto> PurchaseAsync(int shopId, ShopPurchaseRequestDto request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var clientKey = ClientKey(request.IdempotencyKey);
            if (request.UserId <= 0) throw new ArgumentException("userId is required.");
            var deliverTo = request.DeliverTo == null ? ShopDeliveryTarget.Inventory : ShopEnum.Parse<ShopDeliveryTarget>(request.DeliverTo, "deliverTo");
            string key = PurchaseKey(clientKey);

            var replay = await ReplayPurchaseAsync(shopId, request.UserId, key, deliverTo, ct);
            if (replay != null) return replay;

            if (!request.Confirmed)
                throw new ShopRefusedException("NotConfirmed", "Every purchase needs the player's confirmation (confirmed = true).");
            var c = await LoadAsync(shopId, ct);
            if (deliverTo == ShopDeliveryTarget.Inbox && (!c.Settings.Enabled || _delivery == null))
                throw new ProductionOffException("Delivery to the inbox needs Production:Enabled; place the item in the inventory instead.");
            var (quote, item) = await QuoteCoreAsync(c, request, ct);
            if (quote.Refusal != null) throw new ShopRefusedException(quote.Refusal, quote.RefusalMessage!);
            if (request.ExpectedUnitPrice != quote.UnitPrice)
                throw new ShopRefusedException("PriceChanged", $"The price is now {quote.UnitPrice} coins each; confirm again.");
            var user = await _db.Users.AsNoTracking().Where(u => u.Id == request.UserId).Select(u => new { u.Id, u.Coins }).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
            if (user.Coins < quote.TotalPrice)
                throw new ShopRefusedException("InsufficientFunds", $"The player has {user.Coins} coins, {quote.TotalPrice} needed.");
            if (deliverTo == ShopDeliveryTarget.Inbox)
            {
                var inbox = await _delivery!.ViewAsync(request.UserId, StoragePurpose.Inbox, ct);
                var weight = await _db.ItemBlueprints.AsNoTracking().Where(b => b.Id == item!.BlueprintId).Select(b => b.StorageWeight).FirstOrDefaultAsync(ct);
                decimal need = (weight > 0 ? weight : ItemBlueprint.DefaultStorageWeight) * request.Amount;
                if (inbox.CapUnits > 0 && inbox.UsedUnits + need > inbox.CapUnits)
                    throw new ShopRefusedException("InboxFull", "The player's inbox has no room for it.");
            }

            int world = await _stock.GetSystemStorageIdAsync(Storage.SysWorld, ct);
            int from = item!.Entry?.StockMode == ShopStockMode.Unlimited && item.InstanceId == null
                ? await _stock.GetSystemStorageIdAsync(Storage.SysAdjustment, ct)
                : c.Merchandise?.Id ?? throw new ShopRefusedException("SoldOut", "The shop has no Merchandise storage.");

            var result = new ShopPurchaseResultDto
            {
                ShopId = shopId, UserId = request.UserId, ItemBlueprintId = item.BlueprintId, ItemInstanceId = item.InstanceId,
                Amount = request.Amount, UnitPrice = quote.UnitPrice, TotalPrice = quote.TotalPrice, DeliverTo = deliverTo.ToString(),
                TownId = c.TownId!.Value,
            };
            await using var tx = await BeginAsync(ct);
            try
            {
                // Coins first (users, then the town account), then the goods: the same lock order as a delivery.
                var posting = await _currency.PostWithTownAsync(c.TownId.Value,
                    new[] { new CurrencyLeg(request.UserId, Currency.Coins, -quote.TotalPrice) }, new CurrencyContext
                    {
                        IdempotencyKey = key, IdempotencyScope = CurrencyIdempotencyScopes.Plugin, ReasonCode = CurrencyReasons.ShopPurchase,
                        Reason = $"{quote.Amount} × {quote.ItemBlueprintName} at {c.Shop.Name}",
                        Initiator = CurrencyInitiator.Player, InitiatorUserId = request.UserId,
                        SourceType = PurchaseSourceType, SourceRef = shopId.ToString(),
                    }, ct);
                result.CurrencyTransactionId = posting.PublicId;
                result.CoinsAfter = posting.Balances.TryGetValue(request.UserId, out var b) ? b.Coins : null;

                var line = item.InstanceId is { } inst
                    ? StockLineRequest.Unique(from, world, inst)
                    : StockLineRequest.Fungible(from, world, item.BlueprintId, request.Amount);
                var moved = await _stock.MoveAsync(line, new StockContext
                {
                    Reason = StockReason.ShopSale, ActorKind = StockActorKind.PluginServer, ActorUserId = request.UserId,
                    ActorComponent = Component, SourceType = StockSourceType, SourceRef = shopId.ToString(),
                    Note = $"Bought by user {request.UserId} for {quote.TotalPrice} coins ({posting.PublicId})", IdempotencyKey = key,
                }, StockMoveMode.AllOrNothing, ct);
                if (moved.Status != StockResultStatus.Applied)
                {
                    bool locked = moved.Lines.Any(l => l.RejectReason == StockRejectReason.Locked);
                    throw locked
                        ? new ShopRefusedException("StorageLocked", "The shop's Merchandise storage is locked; nothing was taken.")
                        : new ShopRefusedException("NotEnoughStock", "The item sold out meanwhile; nothing was taken.");
                }
                result.StockTransactionId = moved.TransactionId;

                if (item.InstanceId is { } owned)
                {
                    var instance = await _db.ItemInstances.FirstAsync(i => i.Id == owned, ct);
                    if (instance.OwnerUserId != request.UserId) instance.OwnerCount++;
                    instance.OwnerUserId = request.UserId;
                    await _db.SaveChangesAsync(ct);
                }

                if (deliverTo == ShopDeliveryTarget.Inbox)
                {
                    var delivery = await _delivery!.DeliverAsync(new ItemDeliveryRequestDto
                    {
                        UserId = request.UserId, Reason = ItemDeliveryReason.ShopPurchase, SourceType = StockSourceType, SourceRef = shopId.ToString(),
                        Note = $"Bought at {c.Shop.Name}", IdempotencyKey = $"{key}:inbox",
                        Lines = new() { new ItemDeliveryLineRequestDto { ItemBlueprintId = item.InstanceId == null ? item.BlueprintId : null, ItemInstanceId = item.InstanceId, Amount = request.Amount } },
                    }, DeliveryActor.GameServer(request.UserId), ct);
                    if (delivery.LostUnits > 0)
                        throw new ShopRefusedException("InboxFull", "The player's inbox has no room for it.");
                    result.ItemDeliveryId = delivery.Id;
                }
                if (tx != null) await tx.CommitAsync(ct);
            }
            catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.InsufficientFunds)
            {
                throw new ShopRefusedException("InsufficientFunds", ex.Message);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
            return result;
        }

        private async Task<ShopPurchaseResultDto?> ReplayPurchaseAsync(int shopId, int userId, string key, ShopDeliveryTarget deliverTo, CancellationToken ct)
        {
            var stockTx = await _db.StockTransactions.AsNoTracking().Include(t => t.Moves).FirstOrDefaultAsync(t => t.IdempotencyKey == key, ct);
            var currencyTx = await _db.CurrencyTransactions.AsNoTracking().Include(t => t.Entries)
                .FirstOrDefaultAsync(t => t.IdempotencyScope == CurrencyIdempotencyScopes.Plugin && t.IdempotencyKey == key, ct);
            if (stockTx == null || currencyTx == null) return null;
            if (stockTx.SourceRef != shopId.ToString() || stockTx.ActorUserId != userId)
                throw new ShopRefusedException("IdempotencyKeyReuse", "The idempotency key was used for another purchase.");
            var move = stockTx.Moves.First();
            long total = -currencyTx.Entries.Where(e => e.AccountKind == CurrencyAccountKind.User).Sum(e => e.Amount);
            var delivery = deliverTo == ShopDeliveryTarget.Inbox
                ? await _db.Set<ItemDelivery>().AsNoTracking().Where(d => d.IdempotencyKey == key + ":inbox").Select(d => (long?)d.Id).FirstOrDefaultAsync(ct)
                : null;
            return new ShopPurchaseResultDto
            {
                ShopId = shopId, UserId = userId, ItemBlueprintId = move.ItemBlueprintId ?? 0, ItemInstanceId = move.ItemInstanceId,
                Amount = move.Amount, UnitPrice = move.Amount > 0 ? total / move.Amount : total, TotalPrice = total, DeliverTo = deliverTo.ToString(),
                StockTransactionId = stockTx.Id, CurrencyTransactionId = currencyTx.PublicId, ItemDeliveryId = delivery,
                TownId = currencyTx.Entries.Where(e => e.TownId != null).Select(e => e.TownId!.Value).FirstOrDefault(), Replayed = true,
            };
        }

        // ================= Selling =================

        private async Task<(ShopContext C, ShopSellQuoteDto Quote)> SellQuoteCoreAsync(int shopId, ShopSellRequestDto r, CancellationToken ct)
        {
            if (r.Lines == null || r.Lines.Count == 0) throw new ArgumentException("A sale needs at least one line.");
            if (r.Lines.Count > MaxSellLines) throw new ArgumentException($"A sale has at most {MaxSellLines} lines.");
            var c = await LoadAsync(shopId, ct);
            if (!c.Settings.Enabled) throw new ProductionOffException("Selling to shops needs Production:Enabled.");

            var instanceIds = r.Lines.Where(l => l.ItemInstanceId.HasValue).Select(l => l.ItemInstanceId!.Value).ToList();
            if (instanceIds.Count != instanceIds.Distinct().Count()) throw new ArgumentException("A unique item can be on one line only.");
            var instances = await _db.ItemInstances.AsNoTracking().Where(i => instanceIds.Contains(i.Id))
                .Select(i => new { i.Id, i.ItemBlueprintId, i.IsSoulbound }).ToDictionaryAsync(i => i.Id, ct);
            var inStorage = await _db.StockUnits.AsNoTracking().Where(u => instanceIds.Contains(u.ItemInstanceId)).Select(u => u.ItemInstanceId).ToListAsync(ct);
            var blueprintIds = r.Lines.Select(l => l.ItemInstanceId is { } i && instances.TryGetValue(i, out var x) ? x.ItemBlueprintId : l.ItemBlueprintId ?? 0)
                .Where(id => id > 0).ToList();
            var blueprints = await BlueprintsAsync(blueprintIds, ct);
            var enchants = await EnchantmentsAsync(instances.Keys, ct);
            var parents = await _db.Categories.AsNoTracking().Select(x => new { x.Id, x.ParentCategoryId }).ToDictionaryAsync(x => x.Id, x => x.ParentCategoryId, ct);
            var statics = await _db.Set<ShopCatalogueEntry>().AsNoTracking().Where(e => e.ShopId == shopId && e.Enabled && e.StaticPrice != null)
                .Select(e => new { e.ItemBlueprintId, e.ItemInstanceId, e.StaticPrice }).ToListAsync(ct);

            bool Accepts(int? categoryId)
            {
                var seen = new HashSet<int>();
                for (int? cat = categoryId; cat.HasValue && seen.Add(cat.Value); cat = parents.GetValueOrDefault(cat.Value))
                    if (c.AcceptedCategories.Contains(cat.Value)) return true;
                return false;
            }

            var quote = new ShopSellQuoteDto { ShopId = shopId };
            for (int i = 0; i < r.Lines.Count; i++)
            {
                var l = r.Lines[i];
                var q = new ShopSellQuoteLineDto { LineNo = i, ItemInstanceId = l.ItemInstanceId, Amount = l.Amount };
                quote.Lines.Add(q);
                int? bid;
                if (l.ItemInstanceId is { } inst)
                {
                    if (l.Amount != 1) throw new ArgumentException($"Line {i}: a unique item is sold one at a time (amount 1).");
                    if (!instances.TryGetValue(inst, out var found)) { Refuse(q, "NotResolved", $"Item instance {inst} not found."); continue; }
                    bid = found.ItemBlueprintId;
                    if (inStorage.Contains(inst)) { q.ItemBlueprintId = bid; Refuse(q, "InStorage", "That item is already in a storage, not in the player's hands."); continue; }
                    if (found.IsSoulbound) { q.ItemBlueprintId = bid; Refuse(q, "Soulbound", "Soulbound items can't be sold."); continue; }
                }
                else
                {
                    if (l.Amount is < 1 or > MaxAmount) throw new ArgumentException($"Line {i}: amount must be 1 to {MaxAmount}.");
                    bid = l.ItemBlueprintId;
                    if (bid is not > 0 || !blueprints.ContainsKey(bid.Value)) { Refuse(q, "NotResolved", "The item doesn't resolve to a blueprint."); continue; }
                }
                q.ItemBlueprintId = bid;
                if (!blueprints.TryGetValue(bid!.Value, out var bp)) { Refuse(q, "NotResolved", "The item doesn't resolve to a blueprint."); continue; }
                q.ItemBlueprintName = bp.Name;
                if (!Accepts(bp.CategoryId))
                {
                    Refuse(q, "CategoryNotAccepted", $"{c.Shop.ShopType?.Name ?? "This shop"} doesn't buy {bp.Name}.");
                    continue;
                }
                long? staticPrice = l.ItemInstanceId is { } si
                    ? statics.FirstOrDefault(s => s.ItemInstanceId == si)?.StaticPrice
                    : statics.FirstOrDefault(s => s.ItemInstanceId == null && s.ItemBlueprintId == bid)?.StaticPrice;
                var sell = PriceOf(c, staticPrice, bp.Base, l.ItemInstanceId is { } ei ? enchants.GetValueOrDefault(ei)?.Select(x => x.Level) ?? Array.Empty<int>() : null);
                long unit = sell.UnitPrice is { } p ? ShopPricing.BuyBack(p, c.Shop.BuyBackPercent) : 0;
                if (unit <= 0) { Refuse(q, "NoPrice", "The shop pays nothing for that item."); continue; }
                q.UnitPrice = unit;
                q.LinePrice = checked(unit * q.Amount);
            }
            if (c.Merchandise == null)
                foreach (var q in quote.Lines.Where(x => x.Refusal == null)) Refuse(q, "NoRoom", "The shop has no Merchandise storage.");
            quote.Accepted = quote.Lines.All(x => x.Refusal == null);
            quote.Total = quote.Lines.Where(x => x.Refusal == null).Sum(x => x.LinePrice);
            long townCoins = c.TownId == null ? 0
                : await _db.Set<TownAccount>().AsNoTracking().Where(a => a.TownId == c.TownId).Select(a => a.Coins).FirstOrDefaultAsync(ct);
            quote.TownCanPay = c.TownId != null && townCoins >= quote.Total;
            return (c, quote);
        }

        private static void Refuse(ShopSellQuoteLineDto q, string code, string message)
        {
            q.Refusal = code;
            q.RefusalMessage = message;
        }

        public async Task<ShopSellQuoteDto> SellQuoteAsync(int shopId, ShopSellRequestDto request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return (await SellQuoteCoreAsync(shopId, request, ct)).Quote;
        }

        public async Task<ShopSellResultDto> SellAsync(int shopId, ShopSellRequestDto request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var clientKey = ClientKey(request.IdempotencyKey);
            if (request.UserId <= 0) throw new ArgumentException("userId is required.");
            if (!(await _settings.GetAsync()).Enabled) throw new ProductionOffException("Selling to shops needs Production:Enabled.");

            var replay = await ReplaySaleAsync(shopId, request.UserId, clientKey, ct);
            if (replay != null) return replay;
            if (!request.Confirmed)
                throw new ShopRefusedException("NotConfirmed", "Every sale needs the player's confirmation (confirmed = true).");

            var (c, quote) = await SellQuoteCoreAsync(shopId, request, ct);
            var refused = quote.Lines.FirstOrDefault(l => l.Refusal != null);
            if (refused != null) throw new ShopRefusedException(refused.Refusal!, $"Line {refused.LineNo}: {refused.RefusalMessage}");
            if (request.ExpectedTotal != quote.Total)
                throw new ShopRefusedException("PriceChanged", $"The shop now pays {quote.Total} coins in total; confirm again.");
            if (c.TownId == null) throw new ShopRefusedException("NoTown", "The shop has no town to pay from.");
            if (!quote.TownCanPay) throw new ShopRefusedException("TownFundsInsufficient", "The town's account can't pay that much right now.");

            int world = await _stock.GetSystemStorageIdAsync(Storage.SysWorld, ct);
            var result = new ShopSellResultDto { ShopId = shopId, UserId = request.UserId, Total = quote.Total, Lines = quote.Lines };
            await using var tx = await BeginAsync(ct);
            try
            {
                foreach (var q in quote.Lines)
                {
                    string key = SellKey(clientKey, q.LineNo);
                    var posting = await _currency.PostWithTownAsync(c.TownId.Value,
                        new[] { new CurrencyLeg(request.UserId, Currency.Coins, q.LinePrice) }, new CurrencyContext
                        {
                            IdempotencyKey = key, IdempotencyScope = CurrencyIdempotencyScopes.Plugin, ReasonCode = CurrencyReasons.ShopBuyBack,
                            Reason = $"{q.Amount} × {q.ItemBlueprintName} sold to {c.Shop.Name}",
                            Initiator = CurrencyInitiator.Player, InitiatorUserId = request.UserId,
                            SourceType = BuyBackSourceType, SourceRef = shopId.ToString(),
                        }, ct);
                    result.CurrencyTransactionIds.Add(posting.PublicId);
                    result.CoinsAfter = posting.Balances.TryGetValue(request.UserId, out var b) ? b.Coins : result.CoinsAfter;

                    var line = q.ItemInstanceId is { } inst
                        ? StockLineRequest.Unique(world, c.Merchandise!.Id, inst)
                        : StockLineRequest.Fungible(world, c.Merchandise!.Id, q.ItemBlueprintId!.Value, q.Amount);
                    var moved = await _stock.MoveAsync(line, new StockContext
                    {
                        Reason = StockReason.ShopBuyBack, ActorKind = StockActorKind.PluginServer, ActorUserId = request.UserId,
                        ActorComponent = Component, SourceType = StockSourceType, SourceRef = shopId.ToString(),
                        Note = $"Sold by user {request.UserId} for {q.LinePrice} coins ({posting.PublicId})", IdempotencyKey = key,
                    }, StockMoveMode.AllOrNothing, ct);
                    if (moved.Status != StockResultStatus.Applied)
                        throw moved.Lines.Any(l => l.RejectReason == StockRejectReason.Locked)
                            ? new ShopRefusedException("StorageLocked", $"Line {q.LineNo}: the shop's Merchandise storage is locked; nothing was sold.")
                            : new ShopRefusedException("NoRoom", $"Line {q.LineNo}: the shop's Merchandise storage is full; nothing was sold.");
                    result.StockTransactionIds.Add(moved.TransactionId!.Value);
                    if (q.ItemInstanceId is { } sold)
                    {
                        var instance = await _db.ItemInstances.FirstAsync(i => i.Id == sold, ct);
                        instance.OwnerUserId = null;
                        await _db.SaveChangesAsync(ct);
                    }
                }
                if (tx != null) await tx.CommitAsync(ct);
            }
            catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.InsufficientFunds)
            {
                throw new ShopRefusedException("TownFundsInsufficient", ex.Message);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
            return result;
        }

        private async Task<ShopSellResultDto?> ReplaySaleAsync(int shopId, int userId, string clientKey, CancellationToken ct)
        {
            string prefix = $"shop-sell:{clientKey}:";
            var stockTxs = await _db.StockTransactions.AsNoTracking().Include(t => t.Moves)
                .Where(t => t.IdempotencyKey != null && t.IdempotencyKey.StartsWith(prefix)).ToListAsync(ct);
            if (stockTxs.Count == 0) return null;
            if (stockTxs.Any(t => t.SourceRef != shopId.ToString() || t.ActorUserId != userId))
                throw new ShopRefusedException("IdempotencyKeyReuse", "The idempotency key was used for another sale.");
            var keys = stockTxs.Select(t => t.IdempotencyKey!).ToList();
            var postings = await _db.CurrencyTransactions.AsNoTracking().Include(t => t.Entries)
                .Where(t => t.IdempotencyScope == CurrencyIdempotencyScopes.Plugin && keys.Contains(t.IdempotencyKey)).ToListAsync(ct);
            var result = new ShopSellResultDto { ShopId = shopId, UserId = userId, Replayed = true };
            foreach (var t in stockTxs.OrderBy(t => t.IdempotencyKey!.Length).ThenBy(t => t.IdempotencyKey))
            {
                var move = t.Moves.First();
                var p = postings.FirstOrDefault(x => x.IdempotencyKey == t.IdempotencyKey);
                long paid = p?.Entries.Where(e => e.AccountKind == CurrencyAccountKind.User).Sum(e => e.Amount) ?? 0;
                result.StockTransactionIds.Add(t.Id);
                if (p != null) result.CurrencyTransactionIds.Add(p.PublicId);
                result.Lines.Add(new ShopSellQuoteLineDto
                {
                    LineNo = int.TryParse(t.IdempotencyKey![prefix.Length..], out var n) ? n : 0, ItemBlueprintId = move.ItemBlueprintId,
                    ItemInstanceId = move.ItemInstanceId, Amount = move.Amount, LinePrice = paid, UnitPrice = move.Amount > 0 ? paid / move.Amount : paid,
                });
                result.Total += paid;
            }
            return result;
        }
    }
}

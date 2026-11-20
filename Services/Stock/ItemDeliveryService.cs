using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Stock
{
    /// <summary>Who asks for a delivery, claim or stash move: the game server (for a player) or a staff member.</summary>
    public sealed record DeliveryActor(StockActorKind Kind, int? UserId, string Component)
    {
        public static DeliveryActor GameServer(int? actingUserId) => new(StockActorKind.PluginServer, actingUserId, "game-server");

        public static DeliveryActor Staff(int userId) => new(StockActorKind.User, userId, "web");
    }

    /// <summary>A claim or stash move: the storage, the ledger result and the storage afterwards.</summary>
    public sealed record PlayerStorageMoveResult(int StorageId, StockResult Result, PlayerStorageViewDto View);

    /// <summary>
    /// The one entry point for rewards and grants that reach a player (P7, PandiO/knk-workspace#60, production DESIGN
    /// § 4.6): lootbox delivery, kit grants, quest rewards, gifts, inbox claims, withdrawals.
    /// <list type="number">
    ///   <item>The game server places what the player can receive now (online, room, not in a siege match or a minigame
    ///   inventory). Placed items never touch the ledger.</item>
    ///   <item>Everything else is a delivery (<see cref="DeliverAsync"/>): <c>SYS_WORLD</c> → the player's Inbox storage
    ///   (<see cref="StockReason.RewardInbox"/>), with an InboxUpdated notification.</item>
    ///   <item>The player claims later (<see cref="ClaimAsync"/>): Inbox → <c>SYS_WORLD</c> (<see cref="StockReason.InboxClaim"/>)
    ///   while the game server places the items.</item>
    /// </list>
    /// Inbox limits (D7, decision record § 2.4): a hard cap (premium tiers raise it); what does not fit is lost to
    /// <c>SYS_LOSS</c> (<see cref="StockReason.InboxOverflow"/>) with a loss notification; a near-cap warning when a
    /// delivery passes a fill level; 30-day expiry with reminders (<see cref="SweepAsync"/>, InboxExpired to
    /// <c>SYS_LOSS</c>). The premium stash is a separate Player storage (<see cref="StoragePurpose.Stash"/>) with its own
    /// cap and idempotent deposit and withdraw.
    /// <para>
    /// Every move needs <c>Production:Enabled</c> (409 while off, A21); reads work while it is off. Every move needs an
    /// idempotency key, so a retry applies once. Kit and lootbox delivery keep their own paths until the game server
    /// moves them here (L20, #684).
    /// </para>
    /// </summary>
    public interface IItemDeliveryService
    {
        Task<ItemDeliveryDto> DeliverAsync(ItemDeliveryRequestDto request, DeliveryActor actor, CancellationToken ct = default);

        Task<ItemDeliveryDto?> GetAsync(long id, CancellationToken ct = default);

        Task<PagedResultDto<ItemDeliveryListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);

        /// <summary>The player's Inbox or Stash with its cap and fill (an empty view before the first item).</summary>
        Task<PlayerStorageViewDto> ViewAsync(int userId, StoragePurpose purpose, CancellationToken ct = default);

        /// <summary>Inbox → <c>SYS_WORLD</c>, partial: each line moves what the inbox holds; the oldest lots go first.</summary>
        Task<PlayerStorageMoveResult> ClaimAsync(int userId, PlayerStorageMoveDto dto, DeliveryActor actor, CancellationToken ct = default);

        /// <summary><c>SYS_WORLD</c> → Stash (deposit, up to the cap) or Stash → <c>SYS_WORLD</c> (withdraw), partial.</summary>
        Task<PlayerStorageMoveResult> StashMoveAsync(int userId, bool deposit, PlayerStorageMoveDto dto, DeliveryActor actor,
            CancellationToken ct = default);

        /// <summary>Expiry reminders, then expiry. Does nothing while <c>Production:Enabled</c> is off.</summary>
        Task<InboxSweepResultDto> SweepAsync(CancellationToken ct = default);
    }

    public sealed class ItemDeliveryService : IItemDeliveryService
    {
        public const int MaxLines = 100;
        public const int MaxKeyLength = 80;
        public const string Component = "item-delivery";
        private static readonly Regex KeyPattern = new("^[A-Za-z0-9:_.\\-]{1," + MaxKeyLength + "}$", RegexOptions.Compiled);

        private readonly KnKDbContext _db;
        private readonly IStockService _stock;
        private readonly IProductionSettingsReader _settings;
        private readonly IPlayerNotificationQueue? _notifications;
        private readonly TimeProvider _clock;

        public ItemDeliveryService(KnKDbContext db, IStockService stock, IProductionSettingsReader settings,
            IPlayerNotificationQueue? notifications = null, TimeProvider? clock = null)
        {
            _db = db;
            _stock = stock;
            _settings = settings;
            _notifications = notifications;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ================= Deliver =================

        public async Task<ItemDeliveryDto> DeliverAsync(ItemDeliveryRequestDto request, DeliveryActor actor, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var settings = await RequireEnabledAsync();
            var key = Key(request.IdempotencyKey);
            var lines = await ResolveLinesAsync(request.Lines, ct);
            var user = await _db.Users.AsNoTracking().Where(u => u.Id == request.UserId)
                .Select(u => new { u.Id, u.Uuid, u.Username }).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
            var hash = Hash(new { request.UserId, Reason = request.Reason.ToString(), request.SourceType, request.SourceRef,
                Lines = lines.Select(l => new { l.BlueprintId, l.InstanceId, l.Amount }) });

            var earlier = await ReplayAsync(key, hash, ct);
            if (earlier != null) return earlier;

            foreach (var unit in lines.Where(l => l.InstanceId.HasValue))
            {
                var at = await _db.StockUnits.AsNoTracking().Where(u => u.ItemInstanceId == unit.InstanceId)
                    .Select(u => (int?)u.StorageId).FirstOrDefaultAsync(ct);
                if (at != null)
                    throw new ArgumentException($"Item instance {unit.InstanceId} is already in storage {at}; a delivery brings items from the world.");
            }

            var (inboxCap, _, _) = await CapsAsync(user.Id, settings, ct);
            int world = await _stock.GetSystemStorageIdAsync(Storage.SysWorld, ct);
            int loss = await _stock.GetSystemStorageIdAsync(Storage.SysLoss, ct);
            var now = Now;

            await using var tx = await BeginAsync(ct);
            await LockUserAsync(user.Id, ct);
            var inbox = await EnsureStorageAsync(user.Id, StoragePurpose.Inbox, inboxCap, ct);
            await StockLocks.LockStoragesAsync(_db, new[] { world, loss, inbox.Id }, ct);
            earlier = await ReplayAsync(key, hash, ct);
            if (earlier != null)
            {
                if (tx != null) await tx.CommitAsync(ct);
                return earlier;
            }
            if (inbox.IsLockedAt(now)) throw new InvalidOperationException($"The inbox is locked: {inbox.LockReason ?? "no reason given"}.");
            await SetCapAsync(inbox, inboxCap, ct);
            decimal usedBefore = await UsedAsync(inbox.Id, ct);

            var ctx = Context(StockReason.RewardInbox, actor, "delivery:" + key, request);
            var moved = await _stock.MoveManyAsync(lines.Select(l => l.Request(world, inbox.Id)).ToList(), ctx, StockMoveMode.Partial, ct);
            if (moved.Lines.Any(r => r.RejectReason is StockRejectReason.Locked))
                throw new InvalidOperationException("The inbox is locked.");
            if (moved.Lines.Any(r => r.RejectReason is StockRejectReason.NotEnoughStock))
                throw new InvalidOperationException("A unique item of this delivery is no longer in the world.");

            var overflow = moved.Lines.Where(r => r.Rejected > 0)
                .Select(r => r.ItemInstanceId is { } i ? StockLineRequest.Unique(world, loss, i)
                    : StockLineRequest.Fungible(world, loss, r.ItemBlueprintId!.Value, r.Rejected))
                .ToList();
            StockResult? lost = null;
            if (overflow.Count > 0)
                lost = await _stock.MoveManyAsync(overflow, Context(StockReason.InboxOverflow, actor, "delivery:" + key + ":overflow", request),
                    StockMoveMode.AllOrNothing, ct);

            var expires = now.AddDays(settings.ExpiryDays);
            var delivery = new ItemDelivery
            {
                UserId = user.Id,
                Reason = request.Reason,
                SourceType = Clean(request.SourceType, ItemDelivery.SourceMaxLength),
                SourceRef = Clean(request.SourceRef, ItemDelivery.SourceMaxLength),
                Note = Clean(request.Note, ItemDelivery.NoteMaxLength),
                ActorKind = actor.Kind,
                ActorUserId = actor.UserId,
                IdempotencyKey = key,
                RequestHash = hash,
                InboxStorageId = inbox.Id,
                StockTransactionId = moved.TransactionId,
                OverflowTransactionId = lost?.TransactionId,
                CapUnits = inboxCap,
                CreatedAt = now,
                ExpiresAt = expires,
            };
            for (int i = 0; i < lines.Count; i++)
            {
                var r = moved.Lines[i];
                delivery.Lines.Add(new ItemDeliveryLine
                {
                    LineNo = i,
                    ItemBlueprintId = lines[i].BlueprintId,
                    ItemInstanceId = lines[i].InstanceId,
                    Requested = lines[i].Amount,
                    Inboxed = r.Accepted,
                    Lost = r.Rejected,
                    Remaining = r.Accepted,
                    ExpiresAt = expires,
                });
            }
            long inboxed = delivery.Lines.Sum(l => l.Inboxed), lostUnits = delivery.Lines.Sum(l => l.Lost);
            delivery.Status = lostUnits == 0 ? ItemDeliveryStatus.Inboxed : inboxed == 0 ? ItemDeliveryStatus.Lost : ItemDeliveryStatus.PartlyLost;
            _db.Set<ItemDelivery>().Add(delivery);
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            decimal usedAfter = await UsedAsync(inbox.Id, ct);
            _notifications?.EnqueueInbox(user.Id, user.Uuid, user.Username ?? "", new InboxNotificationDto
            {
                Event = InboxNotificationDto.EventDelivered,
                DeliveryId = delivery.Id,
                Reason = request.Reason.ToString(),
                InboxedUnits = inboxed,
                LostUnits = lostUnits,
                WarningPercent = PassedWarning(settings.WarnAtPercent, usedBefore, usedAfter, inboxCap),
                FillPercent = Percent(usedAfter, inboxCap),
                CapUnits = inboxCap,
            });
            return (await GetAsync(delivery.Id, ct))!;
        }

        // ================= Reads =================

        public async Task<ItemDeliveryDto?> GetAsync(long id, CancellationToken ct = default)
        {
            var d = await _db.Set<ItemDelivery>().AsNoTracking().Where(x => x.Id == id)
                .Select(x => new ItemDeliveryDto
                {
                    Id = x.Id,
                    UserId = x.UserId,
                    Username = x.User.Username,
                    Reason = x.Reason,
                    Status = x.Status,
                    SourceType = x.SourceType,
                    SourceRef = x.SourceRef,
                    Note = x.Note,
                    ActorKind = x.ActorKind,
                    ActorUserId = x.ActorUserId,
                    IdempotencyKey = x.IdempotencyKey,
                    InboxStorageId = x.InboxStorageId,
                    StockTransactionId = x.StockTransactionId,
                    OverflowTransactionId = x.OverflowTransactionId,
                    CapUnits = x.CapUnits,
                    CreatedAt = x.CreatedAt,
                    ExpiresAt = x.ExpiresAt,
                }).FirstOrDefaultAsync(ct);
            if (d == null) return null;
            d.Lines = await _db.Set<ItemDeliveryLine>().AsNoTracking().Where(l => l.ItemDeliveryId == id).OrderBy(l => l.LineNo)
                .Select(l => new ItemDeliveryLineDto
                {
                    Id = l.Id,
                    LineNo = l.LineNo,
                    ItemBlueprintId = l.ItemBlueprintId,
                    ItemBlueprintName = l.ItemBlueprint.Name,
                    ItemInstanceId = l.ItemInstanceId,
                    CustomDisplayName = l.ItemInstance != null ? l.ItemInstance.CustomDisplayName : null,
                    Requested = l.Requested,
                    Inboxed = l.Inboxed,
                    Lost = l.Lost,
                    Claimed = l.Claimed,
                    Expired = l.Expired,
                    Remaining = l.Remaining,
                    ExpiresAt = l.ExpiresAt,
                    RemindersSent = l.RemindersSent,
                }).ToListAsync(ct);
            d.InboxedUnits = d.Lines.Sum(l => l.Inboxed);
            d.LostUnits = d.Lines.Sum(l => l.Lost);
            d.RemainingUnits = d.Lines.Sum(l => l.Remaining);
            return d;
        }

        public async Task<PagedResultDto<ItemDeliveryListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            query ??= new PagedQueryDto();
            var q = _db.Set<ItemDelivery>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "userId") is { } userId) q = q.Where(d => d.UserId == userId);
            if (Filter(query, "reason") is { } r && Enum.TryParse<ItemDeliveryReason>(r, true, out var reason)) q = q.Where(d => d.Reason == reason);
            if (Filter(query, "status") is { } s && Enum.TryParse<ItemDeliveryStatus>(s, true, out var status)) q = q.Where(d => d.Status == status);
            if (Filter(query, "sourceType") is { } sourceType) q = q.Where(d => d.SourceType == sourceType);
            if (Filter(query, "sourceRef") is { } sourceRef) q = q.Where(d => d.SourceRef == sourceRef);
            if (Filter(query, "open") is "true") q = q.Where(d => d.Lines.Any(l => l.Remaining > 0));
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(d => d.User.Username.ToLower().Contains(term) || d.IdempotencyKey.ToLower().Contains(term)
                    || (d.SourceRef != null && d.SourceRef.ToLower().Contains(term)) || (d.Note != null && d.Note.ToLower().Contains(term)));
            q = !query.SortDescending && string.Equals(query.SortBy, "id", StringComparison.OrdinalIgnoreCase)
                ? q.OrderBy(d => d.Id) : q.OrderByDescending(d => d.Id);
            return await TerritoryPaging.PageAsync(q.Select(d => new ItemDeliveryListDto
            {
                Id = d.Id,
                UserId = d.UserId,
                Username = d.User.Username,
                Reason = d.Reason,
                Status = d.Status,
                SourceType = d.SourceType,
                SourceRef = d.SourceRef,
                CreatedAt = d.CreatedAt,
                ExpiresAt = d.ExpiresAt,
                InboxedUnits = d.Lines.Sum(l => l.Inboxed),
                LostUnits = d.Lines.Sum(l => l.Lost),
                RemainingUnits = d.Lines.Sum(l => l.Remaining),
            }), query);
        }

        public async Task<PlayerStorageViewDto> ViewAsync(int userId, StoragePurpose purpose, CancellationToken ct = default)
        {
            if (purpose is not (StoragePurpose.Inbox or StoragePurpose.Stash))
                throw new ArgumentException("Only the Inbox and the Stash are player storages here.");
            if (!await _db.Users.AnyAsync(u => u.Id == userId, ct)) throw new KeyNotFoundException($"User {userId} not found.");
            var settings = (await _settings.GetAsync()).Delivery ?? new ProductionDeliverySettingsDto();
            var (inboxCap, stashCap, tier) = await CapsAsync(userId, settings, ct);
            long cap = purpose == StoragePurpose.Inbox ? inboxCap : stashCap;
            var storage = await FindStorageAsync(userId, purpose, ct);
            var view = new PlayerStorageViewDto { UserId = userId, Purpose = purpose, CapUnits = cap, PremiumTierGroupId = tier };
            if (storage == null) return view;

            view.StorageId = storage.Id;
            view.IsLocked = storage.IsLockedAt(Now);
            view.UsedUnits = await UsedAsync(storage.Id, ct);
            view.FillPercent = Percent(view.UsedUnits, cap);
            if (purpose == StoragePurpose.Inbox)
                view.WarningPercent = settings.WarnAtPercent.Where(w => view.FillPercent >= w).Select(w => (int?)w).DefaultIfEmpty().Max();
            view.Entries = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == storage.Id && l.Amount > 0)
                .OrderBy(l => l.ItemBlueprint.Name).ThenBy(l => l.ItemBlueprintId)
                .Select(l => new PlayerStorageEntryDto { ItemBlueprintId = l.ItemBlueprintId, ItemBlueprintName = l.ItemBlueprint.Name, Amount = l.Amount })
                .ToListAsync(ct);
            view.Entries.AddRange(await _db.StockUnits.AsNoTracking().Where(u => u.StorageId == storage.Id).OrderBy(u => u.ItemInstanceId)
                .Select(u => new PlayerStorageEntryDto
                {
                    ItemBlueprintId = u.ItemInstance.ItemBlueprintId,
                    ItemBlueprintName = u.ItemInstance.ItemBlueprint.Name,
                    ItemInstanceId = u.ItemInstanceId,
                    CustomDisplayName = u.ItemInstance.CustomDisplayName,
                    Amount = 1,
                }).ToListAsync(ct));
            if (purpose == StoragePurpose.Inbox)
            {
                var lots = await OpenLots(storage.Id).Select(l => new { l.ItemBlueprintId, l.ItemInstanceId, l.Remaining, l.ExpiresAt }).ToListAsync(ct);
                foreach (var e in view.Entries)
                {
                    var mine = lots.Where(l => e.ItemInstanceId.HasValue ? l.ItemInstanceId == e.ItemInstanceId
                        : l.ItemInstanceId == null && l.ItemBlueprintId == e.ItemBlueprintId).ToList();
                    if (mine.Count == 0) continue;
                    var first = mine.Min(l => l.ExpiresAt);
                    e.NextExpiresAt = first;
                    e.NextExpiringAmount = Math.Min(e.Amount, mine.Where(l => l.ExpiresAt == first).Sum(l => l.Remaining));
                }
            }
            return view;
        }

        // ================= Claim and stash =================

        public async Task<PlayerStorageMoveResult> ClaimAsync(int userId, PlayerStorageMoveDto dto, DeliveryActor actor, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            await RequireEnabledAsync();
            var key = Key(dto.IdempotencyKey);
            var lines = await ResolveLinesAsync(dto.Lines, ct);
            var inbox = await FindStorageAsync(userId, StoragePurpose.Inbox, ct)
                ?? throw new KeyNotFoundException($"User {userId} has no inbox yet.");
            int world = await _stock.GetSystemStorageIdAsync(Storage.SysWorld, ct);

            await using var tx = await BeginAsync(ct);
            await StockLocks.LockStoragesAsync(_db, new[] { world, inbox.Id }, ct);
            var result = await _stock.MoveManyAsync(lines.Select(l => l.Request(inbox.Id, world)).ToList(),
                new StockContext
                {
                    Reason = StockReason.InboxClaim, ActorKind = actor.Kind, ActorUserId = actor.UserId, ActorComponent = actor.Component,
                    SourceType = "Inbox", SourceRef = userId.ToString(), IdempotencyKey = "inbox-claim:" + key,
                }, StockMoveMode.Partial, ct);
            if (!result.Replayed && result.TransactionId != null)
            {
                var lots = await _db.Set<ItemDeliveryLine>().Where(l => l.ItemDelivery.InboxStorageId == inbox.Id && l.Remaining > 0)
                    .OrderBy(l => l.ExpiresAt).ThenBy(l => l.Id).ToListAsync(ct);
                foreach (var r in result.Lines.Where(r => r.Accepted > 0))
                {
                    long left = r.Accepted;
                    foreach (var lot in lots.Where(l => r.ItemInstanceId.HasValue ? l.ItemInstanceId == r.ItemInstanceId
                                 : l.ItemInstanceId == null && l.ItemBlueprintId == r.ItemBlueprintId))
                    {
                        if (left == 0) break;
                        long take = Math.Min(left, lot.Remaining);
                        lot.Claimed += take;
                        lot.Remaining -= take;
                        left -= take;
                    }
                }
                await _db.SaveChangesAsync(ct);
            }
            if (tx != null) await tx.CommitAsync(ct);
            return new PlayerStorageMoveResult(inbox.Id, result, await ViewAsync(userId, StoragePurpose.Inbox, ct));
        }

        public async Task<PlayerStorageMoveResult> StashMoveAsync(int userId, bool deposit, PlayerStorageMoveDto dto, DeliveryActor actor,
            CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var settings = await RequireEnabledAsync();
            var key = Key(dto.IdempotencyKey);
            var lines = await ResolveLinesAsync(dto.Lines, ct);
            if (!await _db.Users.AnyAsync(u => u.Id == userId, ct)) throw new KeyNotFoundException($"User {userId} not found.");
            var (_, stashCap, _) = await CapsAsync(userId, settings, ct);
            int world = await _stock.GetSystemStorageIdAsync(Storage.SysWorld, ct);

            await using var tx = await BeginAsync(ct);
            await LockUserAsync(userId, ct);
            var stash = await EnsureStorageAsync(userId, StoragePurpose.Stash, stashCap, ct);
            await StockLocks.LockStoragesAsync(_db, new[] { world, stash.Id }, ct);
            await SetCapAsync(stash, stashCap, ct);
            var result = await _stock.MoveManyAsync(
                lines.Select(l => deposit ? l.Request(world, stash.Id) : l.Request(stash.Id, world)).ToList(),
                new StockContext
                {
                    Reason = deposit ? StockReason.Deposit : StockReason.Withdraw, ActorKind = actor.Kind, ActorUserId = actor.UserId,
                    ActorComponent = actor.Component, SourceType = "Stash", SourceRef = userId.ToString(),
                    IdempotencyKey = (deposit ? "stash-deposit:" : "stash-withdraw:") + key,
                }, StockMoveMode.Partial, ct);
            if (tx != null) await tx.CommitAsync(ct);
            return new PlayerStorageMoveResult(stash.Id, result, await ViewAsync(userId, StoragePurpose.Stash, ct));
        }

        // ================= Reminders and expiry (D7) =================

        public async Task<InboxSweepResultDto> SweepAsync(CancellationToken ct = default)
        {
            var now = Now;
            var all = await _settings.GetAsync();
            var result = new InboxSweepResultDto { RanAt = now };
            if (!all.Enabled) return result;
            var settings = all.Delivery ?? new ProductionDeliverySettingsDto();

            // Reminders: one per configured day before expiry, summed per player into one notification.
            var days = settings.ReminderDaysBefore.OrderByDescending(d => d).ToList();
            if (days.Count > 0)
            {
                var horizon = now.AddDays(days[0]);
                var due = await _db.Set<ItemDeliveryLine>().Include(l => l.ItemDelivery)
                    .Where(l => l.Remaining > 0 && l.ExpiresAt > now && l.ExpiresAt <= horizon && l.RemindersSent < days.Count)
                    .ToListAsync(ct);
                var reminded = new List<ItemDeliveryLine>();
                foreach (var lot in due)
                {
                    int stage = days.Count(d => lot.ExpiresAt <= now.AddDays(d));
                    if (stage <= lot.RemindersSent) continue;
                    lot.RemindersSent = stage;
                    reminded.Add(lot);
                }
                await _db.SaveChangesAsync(ct);
                foreach (var g in reminded.GroupBy(l => l.ItemDelivery.UserId))
                {
                    await NotifyAsync(g.Key, new InboxNotificationDto
                    {
                        Event = InboxNotificationDto.EventExpiryReminder,
                        ExpiringUnits = g.Sum(l => l.Remaining),
                        ExpiresAt = g.Min(l => l.ExpiresAt),
                    }, ct);
                    result.RemindersSent++;
                }
            }

            // Expiry: what is still in the inbox moves to SYS_LOSS, one transaction per lot (the key makes a rerun safe).
            int loss = await _stock.GetSystemStorageIdAsync(Storage.SysLoss, ct);
            var expired = await _db.Set<ItemDeliveryLine>().AsNoTracking().Where(l => l.Remaining > 0 && l.ExpiresAt <= now)
                .Select(l => new { l.Id, l.ItemDelivery.UserId, l.ItemDelivery.InboxStorageId })
                .ToListAsync(ct);
            foreach (var g in expired.GroupBy(l => (l.UserId, l.InboxStorageId)))
            {
                long lostUnits = 0;
                foreach (var lotRef in g.OrderBy(l => l.Id))
                {
                    await using var tx = await BeginAsync(ct);
                    await StockLocks.LockStoragesAsync(_db, new[] { loss, g.Key.InboxStorageId }, ct);
                    var lot = await _db.Set<ItemDeliveryLine>().FirstAsync(l => l.Id == lotRef.Id, ct);
                    if (lot.Remaining <= 0) continue;
                    var line = lot.ItemInstanceId is { } instance
                        ? StockLineRequest.Unique(g.Key.InboxStorageId, loss, instance)
                        : StockLineRequest.Fungible(g.Key.InboxStorageId, loss, lot.ItemBlueprintId, lot.Remaining);
                    var moved = await _stock.MoveAsync(line, new StockContext
                    {
                        Reason = StockReason.InboxExpired, ActorKind = StockActorKind.System, ActorComponent = Component,
                        SourceType = nameof(ItemDelivery), SourceRef = lot.ItemDeliveryId.ToString(),
                        IdempotencyKey = "inbox-expire:" + lot.Id,
                    }, StockMoveMode.Partial, ct);
                    // What the storage no longer holds (moved by staff) is gone from the lot too.
                    lot.Expired += moved.Accepted;
                    lot.Remaining = 0;
                    await _db.SaveChangesAsync(ct);
                    if (tx != null) await tx.CommitAsync(ct);
                    lostUnits += moved.Accepted;
                    result.ExpiredLines++;
                }
                result.ExpiredUnits += lostUnits;
                if (lostUnits > 0)
                    await NotifyAsync(g.Key.UserId, new InboxNotificationDto { Event = InboxNotificationDto.EventExpired, LostUnits = lostUnits }, ct);
            }
            return result;
        }

        // ================= Helpers =================

        private sealed record ResolvedLine(int BlueprintId, long? InstanceId, long Amount)
        {
            public StockLineRequest Request(int from, int to) => InstanceId is { } i
                ? StockLineRequest.Unique(from, to, i)
                : StockLineRequest.Fungible(from, to, BlueprintId, Amount);
        }

        private async Task<List<ResolvedLine>> ResolveLinesAsync(List<ItemDeliveryLineRequestDto>? lines, CancellationToken ct)
        {
            if (lines == null || lines.Count == 0) throw new ArgumentException("At least one line is required.");
            if (lines.Count > MaxLines) throw new ArgumentException($"At most {MaxLines} lines.");
            var result = new List<ResolvedLine>(lines.Count);
            foreach (var l in lines)
            {
                if (l == null) throw new ArgumentException("A line is empty.");
                if (l.ItemInstanceId.HasValue)
                {
                    if (l.Amount is not (0 or 1)) throw new ArgumentException("A unique item (itemInstanceId) has amount 1.");
                    var bp = await _db.ItemInstances.AsNoTracking().Where(i => i.Id == l.ItemInstanceId).Select(i => (int?)i.ItemBlueprintId)
                        .FirstOrDefaultAsync(ct) ?? throw new ArgumentException($"Item instance {l.ItemInstanceId} not found.");
                    if (l.ItemBlueprintId.HasValue && l.ItemBlueprintId != bp)
                        throw new ArgumentException($"Item instance {l.ItemInstanceId} is not of blueprint {l.ItemBlueprintId}.");
                    result.Add(new ResolvedLine(bp, l.ItemInstanceId, 1));
                }
                else
                {
                    if (l.ItemBlueprintId is not { } bp) throw new ArgumentException("Each line needs itemBlueprintId or itemInstanceId.");
                    if (l.Amount < 1 || l.Amount > StockService.MaxAmount) throw new ArgumentException("amount must be at least 1.");
                    if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == bp, ct)) throw new ArgumentException($"Item blueprint {bp} not found.");
                    result.Add(new ResolvedLine(bp, null, l.Amount));
                }
            }
            if (result.Where(r => r.InstanceId.HasValue).GroupBy(r => r.InstanceId).Any(g => g.Count() > 1))
                throw new ArgumentException("A unique item can be in one line only.");
            return result;
        }

        private async Task<ProductionDeliverySettingsDto> RequireEnabledAsync()
        {
            var all = await _settings.GetAsync();
            if (!all.Enabled)
                throw new ProductionOffException("Production is switched off (Production:Enabled); the inbox and the stash move nothing.");
            return all.Delivery ?? new ProductionDeliverySettingsDto();
        }

        private static string Key(string? raw)
        {
            var key = raw?.Trim();
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("idempotencyKey is required (one per delivery or click, so a retry applies once).");
            if (!KeyPattern.IsMatch(key))
                throw new ArgumentException($"idempotencyKey is 1 to {MaxKeyLength} characters of letters, digits and : _ . -");
            return key;
        }

        private async Task<ItemDeliveryDto?> ReplayAsync(string key, string hash, CancellationToken ct)
        {
            var earlier = await _db.Set<ItemDelivery>().AsNoTracking().Where(d => d.IdempotencyKey == key)
                .Select(d => new { d.Id, d.RequestHash }).FirstOrDefaultAsync(ct);
            if (earlier == null) return null;
            if (earlier.RequestHash != hash) throw new StockIdempotencyConflictException(key);
            var dto = await GetAsync(earlier.Id, ct);
            dto!.Replayed = true;
            return dto;
        }

        /// <summary>The player's caps: the base, or their active premium tier's entry when it is higher.</summary>
        private async Task<(long Inbox, long Stash, int? TierGroupId)> CapsAsync(int userId, ProductionDeliverySettingsDto settings, CancellationToken ct)
        {
            var memberships = await _db.UserPermissionGroups.AsNoTracking().Include(m => m.PermissionGroup)
                .Where(m => m.UserId == userId && m.PermissionGroup.IsPremiumTier).ToListAsync(ct);
            var tier = UserPermissionGroupService.SelectPremiumTier(memberships, Now);
            var entry = tier == null ? null : settings.PremiumTiers.FirstOrDefault(t => t.PermissionGroupId == tier.PermissionGroupId);
            return (Math.Max(settings.InboxBaseCapUnits, entry?.InboxCapUnits ?? 0),
                Math.Max(settings.StashBaseCapUnits, entry?.StashCapUnits ?? 0),
                tier?.PermissionGroupId);
        }

        private Task<Storage?> FindStorageAsync(int userId, StoragePurpose purpose, CancellationToken ct) =>
            _db.Storages.AsNoTracking().Where(s => s.Kind == StorageKind.Player && s.UserId == userId && s.Purpose == purpose)
                .OrderBy(s => s.Id).FirstOrDefaultAsync(ct);

        /// <summary>The player's Inbox or Stash, created on first use (under the user's row lock, so once).</summary>
        private async Task<Storage> EnsureStorageAsync(int userId, StoragePurpose purpose, long cap, CancellationToken ct)
        {
            var existing = await FindStorageAsync(userId, purpose, ct);
            if (existing != null) return existing;
            var storage = new Storage
            {
                Name = purpose == StoragePurpose.Inbox ? $"Inbox of user {userId}" : $"Stash of user {userId}",
                Kind = StorageKind.Player,
                Purpose = purpose,
                UserId = userId,
                MaxUnits = cap,
                CreatedAt = Now,
            };
            _db.Storages.Add(storage);
            await _db.SaveChangesAsync(ct);
            _db.Entry(storage).State = EntityState.Detached;
            return storage;
        }

        /// <summary>Writes the player's current cap (a premium tier may have started or ended), under the storage lock.</summary>
        private async Task SetCapAsync(Storage storage, long cap, CancellationToken ct)
        {
            var row = await _db.Storages.FirstAsync(s => s.Id == storage.Id, ct);
            if (row.MaxUnits != cap)
            {
                row.MaxUnits = cap;
                row.RowVersion++;
                await _db.SaveChangesAsync(ct);
            }
            _db.Entry(row).State = EntityState.Detached;
        }

        private async Task<decimal> UsedAsync(int storageId, CancellationToken ct)
        {
            var lines = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == storageId)
                .SumAsync(l => (decimal?)(l.Amount * l.ItemBlueprint.StorageWeight), ct) ?? 0m;
            var units = await _db.StockUnits.AsNoTracking().Where(u => u.StorageId == storageId)
                .SumAsync(u => (decimal?)u.ItemInstance.ItemBlueprint.StorageWeight, ct) ?? 0m;
            return lines + units;
        }

        private IQueryable<ItemDeliveryLine> OpenLots(int inboxStorageId) =>
            _db.Set<ItemDeliveryLine>().AsNoTracking().Where(l => l.ItemDelivery.InboxStorageId == inboxStorageId && l.Remaining > 0);

        /// <summary>A transaction of our own, unless the caller has one (or the database is not relational: tests).</summary>
        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
            _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;

        private async Task LockUserAsync(int userId, CancellationToken ct)
        {
            if (!_db.Database.IsRelational()) return;
#pragma warning disable EF1002 // an int only
            await _db.Database.ExecuteSqlRawAsync($"SELECT Id FROM users WHERE Id = {userId} FOR UPDATE", ct);
#pragma warning restore EF1002
        }

        private async Task NotifyAsync(int userId, InboxNotificationDto dto, CancellationToken ct)
        {
            if (_notifications == null) return;
            var user = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Uuid, u.Username }).FirstOrDefaultAsync(ct);
            if (user == null) return;
            _notifications.EnqueueInbox(userId, user.Uuid, user.Username ?? "", dto);
        }

        private static StockContext Context(StockReason reason, DeliveryActor actor, string key, ItemDeliveryRequestDto request) => new()
        {
            Reason = reason,
            ActorKind = actor.Kind,
            ActorUserId = actor.UserId,
            ActorComponent = actor.Component,
            SourceType = Clean(request.SourceType, ItemDelivery.SourceMaxLength) ?? "ItemDelivery",
            SourceRef = Clean(request.SourceRef, ItemDelivery.SourceMaxLength),
            Note = Clean(request.Note, ItemDelivery.NoteMaxLength),
            IdempotencyKey = key,
        };

        public static int? PassedWarning(IEnumerable<int> levels, decimal before, decimal after, long cap)
        {
            int b = Percent(before, cap), a = Percent(after, cap);
            return levels.Where(w => b < w && a >= w).Select(w => (int?)w).DefaultIfEmpty().Max();
        }

        public static int Percent(decimal used, long cap) =>
            cap <= 0 ? (used > 0 ? 100 : 0) : (int)Math.Min(100m, Math.Floor(used * 100m / cap));

        private static string? Filter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        private static string? Clean(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length <= max ? s : s[..max];
        }

        private static string Hash(object request)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Hourly: expiry reminders and expiry of the reward inbox (D7), only with <c>Production:Enabled</c> on (A21).
    /// Staff can run the same sweep now with <c>POST api/Inbox/sweep</c>.
    /// </summary>
    public sealed class InboxMaintenanceService : Microsoft.Extensions.Hosting.BackgroundService
    {
        public static readonly TimeSpan Tick = TimeSpan.FromHours(1);

        private readonly IServiceProvider _services;
        private readonly ConnectivityAwareFailureLogger _failures;
        private readonly Microsoft.Extensions.Logging.ILogger<InboxMaintenanceService> _logger;

        public InboxMaintenanceService(IServiceProvider services, Microsoft.Extensions.Logging.ILogger<InboxMaintenanceService> logger)
        {
            _services = services;
            _logger = logger;
            _failures = new ConnectivityAwareFailureLogger(logger, "Inbox maintenance");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Tick);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_services);
                        var service = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                            .GetRequiredService<IItemDeliveryService>(scope.ServiceProvider);
                        var r = await service.SweepAsync(stoppingToken);
                        if (r.ExpiredLines > 0)
                            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger,
                                "Inbox: {Lines} lot(s) expired ({Units} units to SYS_LOSS), {Reminders} reminder(s)",
                                r.ExpiredLines, r.ExpiredUnits, r.RemindersSent);
                        _failures.Succeeded();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failures.Failed(ex, "Inbox maintenance failed");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}

using System.Text.Json;
using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Survival
{
    // Siege Survival phase 4 (knk-workspace docs/specs/siege-survival/DESIGN.md §8a, S24, S34, S39, S59;
    // PandiO/knk-workspace#41, link S7a): the match-scoped Mystery Box roll. The plugin charges the price in its
    // MatchLedger (charge id = rollId), then calls roll; the API checks the match is in progress, the point belongs
    // to the match's scenario, the user is a participant and the pool validates (exactly 100 %), rolls once with
    // SurvivalMysteryBoxRollEngine, stores one row and answers it. The same rollId answers the stored row (no second
    // roll); one delivery per roll; forfeit and claim exclude each other. Nothing here touches the world lootboxes,
    // item instances, daily quotas or tokens (DESIGN §8a).
    public class SurvivalMysteryBoxRollService : ISurvivalMysteryBoxRollService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

        private readonly ISurvivalMysteryBoxRollRepository _repo;
        private readonly ILootRandom _random;
        private readonly IMapper _mapper;
        private readonly SiegeSurvivalOptions _options;

        public SurvivalMysteryBoxRollService(ISurvivalMysteryBoxRollRepository repo, ILootRandom random, IMapper mapper,
            IOptions<SiegeSurvivalOptions>? options = null)
        {
            _repo = repo;
            _random = random;
            _mapper = mapper;
            _options = options?.Value ?? new SiegeSurvivalOptions();
        }

        public async Task<(SurvivalMysteryBoxRollDto Roll, bool Created)> RollAsync(int siegeMatchId, int pointId, SurvivalMysteryBoxRollRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var rollId = RequireRollId(request.RollId);
            if (request.UserId <= 0) throw new ArgumentException("userId is required.");
            if (request.PriceCharged < 0) throw new ArgumentException("priceCharged can't be negative.");

            var existing = await _repo.GetRollAsync(rollId);
            if (existing != null) return (Replay(existing, siegeMatchId, pointId, request), false);

            var match = await _repo.GetMatchAsync(siegeMatchId)
                ?? throw new KeyNotFoundException($"Siege match {siegeMatchId} not found.");
            if (match.Status != SiegeMatchStatus.InProgress)
                throw new SurvivalMysteryBoxConflictException(SurvivalMysteryBoxConflictException.MatchNotInProgress,
                    $"Siege match {siegeMatchId} is {match.Status}, not in progress.");

            var point = await _repo.GetPointWithPoolAsync(pointId);
            if (point == null || point.SiegeScenarioId != match.SiegeScenarioId)
                throw new KeyNotFoundException($"Mystery Box point {pointId} not found in the scenario of siege match {siegeMatchId}.");

            var participant = match.Participants.FirstOrDefault(p => p.UserId == request.UserId)
                ?? throw new KeyNotFoundException($"User {request.UserId} is not a participant of siege match {siegeMatchId}.");
            if (participant.LeftAt.HasValue)
                throw new SurvivalMysteryBoxConflictException(SurvivalMysteryBoxConflictException.ParticipantLeft,
                    $"User {request.UserId} has left siege match {siegeMatchId}.");

            var pool = point.SurvivalMysteryBoxPool;
            var validation = SiegeSurvivalReadiness.EvaluatePool(pool,
                await _repo.ExistingItemBlueprintIdsAsync(pool.Entries.Where(e => e.ItemBlueprintId.HasValue).Select(e => e.ItemBlueprintId!.Value)),
                await _repo.ExistingEnchantmentDefinitionIdsAsync(pool.Entries
                    .Where(e => e.EnchantmentDefinitionId.HasValue).Select(e => e.EnchantmentDefinitionId!.Value)
                    .Concat(pool.Entries.SelectMany(e => e.Enchantments).Select(c => c.EnchantmentDefinitionId))));
            if (validation.Count > 0)
                throw new SurvivalMysteryBoxConflictException(SurvivalMysteryBoxConflictException.PoolInvalid,
                    $"Mystery Box pool '{pool.Name}' is not valid: " + string.Join("; ", validation.Select(i => $"{i.Code}: {i.Message}")));

            var outcome = SurvivalMysteryBoxRollEngine.Roll(pool, _random, Math.Max(1, _options.MysteryBoxReelLength));
            var roll = new SurvivalMysteryBoxRoll
            {
                RollId = rollId,
                SiegeMatchId = match.Id,
                SurvivalMysteryBoxPointId = point.Id,
                SurvivalMysteryBoxPoolId = pool.Id,
                UserId = request.UserId,
                Variant = pool.Variant,
                SurvivalMysteryBoxEntryId = outcome.Entry.Id,
                EntryJson = JsonSerializer.Serialize(_mapper.Map<SurvivalMysteryBoxEntryDto>(outcome.Entry), SnapshotJson),
                RolledEnchantmentsJson = JsonSerializer.Serialize(outcome.RolledEnchantments
                    .Select(c => new SurvivalMysteryBoxRolledEnchantmentDto { EnchantmentDefinitionId = c.EnchantmentDefinitionId, Level = c.Level })
                    .ToList(), SnapshotJson),
                ReelJson = JsonSerializer.Serialize(outcome.Reel, SnapshotJson),
                PriceCharged = request.PriceCharged,
                State = SurvivalMysteryBoxRollState.Rolled,
                RolledAt = DateTime.UtcNow
            };

            try
            {
                await _repo.AddRollAsync(roll);
            }
            catch (DbUpdateException)
            {
                // Two concurrent calls with one rollId: the unique index let one through; answer that one.
                var raced = await _repo.GetRollAsync(rollId);
                if (raced == null) throw;
                return (Replay(raced, siegeMatchId, pointId, request), false);
            }

            return (ToDto(roll), true);
        }

        public async Task<SurvivalMysteryBoxRollDto> ClaimAsync(int siegeMatchId, string rollId, SurvivalMysteryBoxClaimRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!Enum.IsDefined(request.DeliveryKind)) throw new ArgumentException("deliveryKind must be Inventory or Drop.");
            var roll = await LoadRollAsync(siegeMatchId, rollId);

            if (roll.State == SurvivalMysteryBoxRollState.Forfeited)
                throw new SurvivalMysteryBoxConflictException(SurvivalMysteryBoxConflictException.RollForfeited,
                    $"Roll {rollId} was forfeited ({roll.ForfeitReason}) and can't be claimed.");
            if (roll.State == SurvivalMysteryBoxRollState.Claimed && roll.Delivery != null) return ToDto(roll);

            var now = DateTime.UtcNow;
            roll.Delivery = new SurvivalMysteryBoxDelivery { SurvivalMysteryBoxRollId = roll.Id, Kind = request.DeliveryKind, DeliveredAt = now };
            roll.State = SurvivalMysteryBoxRollState.Claimed;
            roll.ClaimedAt = now;
            try
            {
                await _repo.AddDeliveryAsync(roll.Delivery);
            }
            catch (DbUpdateException)
            {
                // A concurrent claim won the unique index: answer the stored delivery.
                var stored = await _repo.GetRollAsync(rollId);
                if (stored?.Delivery == null) throw;
                return ToDto(stored);
            }
            return ToDto(roll);
        }

        public async Task<SurvivalMysteryBoxRollDto> ForfeitAsync(int siegeMatchId, string rollId, SurvivalMysteryBoxForfeitRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!Enum.IsDefined(request.Reason)) throw new ArgumentException("reason must be Timeout, RollerDied, RollerLeft or MatchEnded.");
            var roll = await LoadRollAsync(siegeMatchId, rollId);

            if (roll.State == SurvivalMysteryBoxRollState.Claimed)
                throw new SurvivalMysteryBoxConflictException(SurvivalMysteryBoxConflictException.RollClaimed,
                    $"Roll {rollId} was already claimed and can't be forfeited.");
            if (roll.State == SurvivalMysteryBoxRollState.Forfeited) return ToDto(roll);

            roll.State = SurvivalMysteryBoxRollState.Forfeited;
            roll.ForfeitedAt = DateTime.UtcNow;
            roll.ForfeitReason = request.Reason;
            await _repo.SaveChangesAsync();
            return ToDto(roll);
        }

        public async Task<List<SurvivalMysteryBoxRollDto>> ListAsync(int siegeMatchId)
        {
            _ = await _repo.GetMatchAsync(siegeMatchId) ?? throw new KeyNotFoundException($"Siege match {siegeMatchId} not found.");
            return (await _repo.ListRollsAsync(siegeMatchId)).Select(ToDto).ToList();
        }

        // ---- helpers ----

        private async Task<SurvivalMysteryBoxRoll> LoadRollAsync(int siegeMatchId, string rollId)
        {
            var roll = await _repo.GetRollAsync(RequireRollId(rollId));
            if (roll == null || roll.SiegeMatchId != siegeMatchId)
                throw new KeyNotFoundException($"Roll {rollId} not found in siege match {siegeMatchId}.");
            return roll;
        }

        private static SurvivalMysteryBoxRollDto Replay(SurvivalMysteryBoxRoll stored, int siegeMatchId, int pointId, SurvivalMysteryBoxRollRequestDto request)
        {
            if (stored.SiegeMatchId != siegeMatchId || stored.SurvivalMysteryBoxPointId != pointId
                || stored.UserId != request.UserId || stored.PriceCharged != request.PriceCharged)
            {
                throw new SurvivalMysteryBoxConflictException(SurvivalMysteryBoxConflictException.RollIdReused,
                    $"Roll id '{stored.RollId}' was already used for match {stored.SiegeMatchId}, point {stored.SurvivalMysteryBoxPointId}, " +
                    $"user {stored.UserId}, price {stored.PriceCharged}; a roll id identifies one roll.");
            }
            return ToDto(stored);
        }

        private static string RequireRollId(string? rollId)
        {
            var trimmed = rollId?.Trim();
            if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("rollId is required.");
            if (trimmed.Length > SurvivalMysteryBoxRollRequestDto.RollIdMaxLength)
                throw new ArgumentException($"rollId can be at most {SurvivalMysteryBoxRollRequestDto.RollIdMaxLength} characters.");
            return trimmed;
        }

        public static SurvivalMysteryBoxRollDto ToDto(SurvivalMysteryBoxRoll roll) => new()
        {
            RollId = roll.RollId,
            SiegeMatchId = roll.SiegeMatchId,
            PointId = roll.SurvivalMysteryBoxPointId,
            UserId = roll.UserId,
            Variant = roll.Variant,
            Entry = JsonSerializer.Deserialize<SurvivalMysteryBoxEntryDto>(roll.EntryJson, SnapshotJson) ?? new SurvivalMysteryBoxEntryDto(),
            RolledEnchantments = JsonSerializer.Deserialize<List<SurvivalMysteryBoxRolledEnchantmentDto>>(roll.RolledEnchantmentsJson, SnapshotJson) ?? new(),
            Reel = JsonSerializer.Deserialize<List<int>>(roll.ReelJson, SnapshotJson) ?? new(),
            PriceCharged = roll.PriceCharged,
            State = roll.State,
            RolledAt = roll.RolledAt,
            ClaimedAt = roll.ClaimedAt,
            DeliveryKind = roll.Delivery?.Kind,
            ForfeitedAt = roll.ForfeitedAt,
            ForfeitReason = roll.ForfeitReason
        };
    }
}

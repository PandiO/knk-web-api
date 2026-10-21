using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Duel
{
    /// <summary>A lock-in request after the shape checks.</summary>
    internal sealed record DuelCreateRequest
    {
        public required string Token { get; init; }
        public required int ArenaId { get; init; }
        public required int ChallengerUserId { get; init; }
        public required int OpponentUserId { get; init; }
        public required DuelGearMode GearMode { get; init; }
        public int? FixedKitId { get; init; }
        public required long ChallengerCoins { get; init; }
        public required long OpponentCoins { get; init; }
        public required long Gems { get; init; }
        public required List<DuelItemDto> Items { get; init; }
        public required int OfferCount { get; init; }
        public DateTime? LockedAt { get; init; }

        public int ChallengerItemStacks => Items.Count(i => i.OwnerUserId == ChallengerUserId);
        public int OpponentItemStacks => Items.Count(i => i.OwnerUserId == OpponentUserId);

        public bool IsValueMoving => ChallengerCoins > 0 || OpponentCoins > 0 || Items.Count > 0 || GearMode == DuelGearMode.OWN_GEAR_DROP;
    }

    internal sealed record DuelRefusal(DuelErrorCode Code, string Message, object? Details = null);

    // The lock-in rules (arena duels DESIGN.md §6.3, §7.6, §10.2, D14) and the review inputs (§10.3).
    public partial class DuelMatchService
    {
        /// <summary>Matches that count for the abuse limits and the review (DESIGN §10.3 "value-moving").</summary>
        private static IQueryable<DuelMatch> ValueMoving(IQueryable<DuelMatch> query) =>
            query.Where(m => m.ChallengerCoins > 0 || m.OpponentCoins > 0 || m.ChallengerItemStacks > 0
                             || m.OpponentItemStacks > 0 || m.GearMode == DuelGearMode.OWN_GEAR_DROP);

        private static IQueryable<DuelMatch> OfPair(IQueryable<DuelMatch> query, int a, int b) =>
            query.Where(m => (m.ChallengerUserId == a && m.OpponentUserId == b) || (m.ChallengerUserId == b && m.OpponentUserId == a));

        private static DuelCreateRequest Validate(DuelMatchCreateDto? dto)
        {
            if (dto == null) throw Invalid("A body is required.");
            var token = NormalizeToken(dto.PublicToken) ?? throw Invalid("publicToken must be a UUID.");
            if (dto.ChallengerUserId <= 0 || dto.OpponentUserId <= 0) throw Invalid("Both duelists are required.");
            if (!Enum.IsDefined(dto.GearMode)) throw Invalid("Unknown gear mode.");
            if (dto.ChallengerCoins < 0 || dto.OpponentCoins < 0) throw Invalid("Coin stakes can't be negative.");
            if (dto.ChallengerGems < 0 || dto.OpponentGems < 0) throw Invalid("Gem amounts can't be negative.");
            if (dto.OfferCount is < 1 or > 6) throw Invalid("offerCount must be 1–6.");
            var items = (dto.StakeItems ?? new List<DuelItemDto>()).Where(i => i != null).ToList();
            foreach (var item in items)
            {
                if (item.OwnerUserId != dto.ChallengerUserId && item.OwnerUserId != dto.OpponentUserId)
                    throw Invalid($"A stake item names owner {item.OwnerUserId}, who isn't a duelist.");
                if (item.Amount < 1 || string.IsNullOrWhiteSpace(item.Material))
                    throw Invalid("Every stake item needs a material and an amount of at least 1.");
            }
            return new DuelCreateRequest
            {
                Token = token,
                ArenaId = dto.ArenaId,
                ChallengerUserId = dto.ChallengerUserId,
                OpponentUserId = dto.OpponentUserId,
                GearMode = dto.GearMode,
                FixedKitId = dto.FixedKitId,
                ChallengerCoins = dto.ChallengerCoins,
                OpponentCoins = dto.OpponentCoins,
                Gems = dto.ChallengerGems + dto.OpponentGems,
                Items = items,
                OfferCount = dto.OfferCount,
                LockedAt = ToUtc(dto.LockedAt)
            };
        }

        private async Task<Arena> LoadArenaAsync(int arenaId, CancellationToken ct) =>
            await _context.Arenas.AsNoTracking().Include(a => a.EligibleKits).FirstOrDefaultAsync(a => a.Id == arenaId, ct)
            ?? throw new DuelException(DuelErrorCode.ArenaNotFound, $"Arena {arenaId} doesn't exist.");

        private async Task<HashSet<int>> EligibleKitIdsAsync(int arenaId, CancellationToken ct) =>
            (await _context.ArenaKits.AsNoTracking().Where(k => k.ArenaId == arenaId).Select(k => k.KitId).ToListAsync(ct)).ToHashSet();

        private async Task<(User Challenger, User Opponent)> LoadUsersAsync(DuelCreateRequest request, CancellationToken ct)
        {
            var ids = new[] { request.ChallengerUserId, request.OpponentUserId };
            var users = await _context.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
            var challenger = users.FirstOrDefault(u => u.Id == request.ChallengerUserId)
                ?? throw new DuelException(DuelErrorCode.UserNotFound, $"User {request.ChallengerUserId} doesn't exist.");
            var opponent = users.FirstOrDefault(u => u.Id == request.OpponentUserId)
                ?? throw new DuelException(DuelErrorCode.UserNotFound, $"User {request.OpponentUserId} doesn't exist.");
            return (challenger, opponent);
        }

        /// <summary>
        /// Every rule a lock-in breaks, in the order a player should hear about them. Friendly duels (no
        /// stakes, not drop mode) only need the arena, gear and kit rules. <paramref name="includeFunds"/>
        /// adds the balance check for the advisory /check (at lock-in the ledger decides).
        /// </summary>
        private async Task<List<DuelRefusal>> EvaluateAsync(DuelCreateRequest r, Arena arena, (User Challenger, User Opponent) users,
            DateTime now, bool includeFunds, CancellationToken ct)
        {
            var refusals = new List<DuelRefusal>();
            void Refuse(DuelErrorCode code, string message, object? details = null) => refusals.Add(new DuelRefusal(code, message, details));
            var (challenger, opponent) = users;
            string NameOf(int userId) => userId == challenger.Id ? challenger.Username : opponent.Username;

            // ----- Arena, gear and kit -----
            if (r.ChallengerUserId == r.OpponentUserId) Refuse(DuelErrorCode.SelfDuel, "You can't duel yourself.");
            if (!arena.IsDuelEnabled) Refuse(DuelErrorCode.DuelsDisabled, $"Duels are switched off at {arena.Name}.");
            if (!Allows(arena, r.GearMode))
            {
                Refuse(DuelErrorCode.GearModeNotAllowed, $"{arena.Name} doesn't allow {r.GearMode} duels.", new { gearMode = r.GearMode.ToString() });
            }
            var kits = arena.EligibleKits.Select(k => k.KitId).ToHashSet();
            if (r.GearMode == DuelGearMode.FIXED_KIT && (r.FixedKitId == null || !kits.Contains(r.FixedKitId.Value)))
            {
                Refuse(DuelErrorCode.KitNotEligible, "That kit isn't one of this arena's duel kits.", new { kitId = r.FixedKitId });
            }

            // ----- Stakes (§7.6) -----
            if (r.Gems != 0) Refuse(DuelErrorCode.GemsNotStakeable, "Gems can't be staked in a duel.");
            var anyCoins = r.ChallengerCoins > 0 || r.OpponentCoins > 0;
            if (anyCoins && !arena.AllowCoinStakes) Refuse(DuelErrorCode.CoinStakesNotAllowed, $"{arena.Name} doesn't allow coin stakes.");
            foreach (var (userId, coins) in new[] { (r.ChallengerUserId, r.ChallengerCoins), (r.OpponentUserId, r.OpponentCoins) })
            {
                if (coins > arena.MaxCoinStake)
                {
                    Refuse(DuelErrorCode.CoinStakeOutOfRange, $"A side may stake at most {arena.MaxCoinStake:N0} coins here ({NameOf(userId)}: {coins:N0}).",
                        new { userId, coins, max = arena.MaxCoinStake });
                }
            }
            // The ratio compares each side's coins plus the coin value of its items (developer, 2026-10-21, KNG-197):
            // an item is worth its ItemInstance's blueprint BasePriceMin × amount; anything else is worth 0.
            var itemValues = await ItemValuesAsync(r.Items, ct);
            var challengerValue = r.ChallengerCoins + itemValues.GetValueOrDefault(r.ChallengerUserId);
            var opponentValue = r.OpponentCoins + itemValues.GetValueOrDefault(r.OpponentUserId);
            var larger = Math.Max(challengerValue, opponentValue);
            var smaller = Math.Min(challengerValue, opponentValue);
            if (larger > 0 && arena.MinStakeRatioPercent > 0 && smaller * 100 < larger * arena.MinStakeRatioPercent)
            {
                Refuse(DuelErrorCode.StakeRatioTooLow,
                    $"The smaller stake must be worth at least {arena.MinStakeRatioPercent}% of the larger ({smaller:N0} vs {larger:N0} coins, items at their base price).",
                    new
                    {
                        minPercent = arena.MinStakeRatioPercent, smaller, larger, minimum = (larger * arena.MinStakeRatioPercent + 99) / 100,
                        challengerValue, opponentValue
                    });
            }
            if (r.Items.Count > 0 && !arena.AllowItemStakes) Refuse(DuelErrorCode.ItemStakesNotAllowed, $"{arena.Name} doesn't allow item stakes.");
            foreach (var (userId, stacks) in new[] { (r.ChallengerUserId, r.ChallengerItemStacks), (r.OpponentUserId, r.OpponentItemStacks) })
            {
                if (stacks > arena.MaxItemStacks)
                {
                    Refuse(DuelErrorCode.TooManyItemStacks, $"A side may stake at most {arena.MaxItemStacks} item stacks here ({NameOf(userId)}: {stacks}).",
                        new { userId, stacks, max = arena.MaxItemStacks });
                }
            }

            if (!r.IsValueMoving)
            {
                return refusals;
            }

            // ----- Eligibility (D14) -----
            var policies = await _currency.GetPoliciesAsync(ct);
            var coinPolicy = policies.TryGetValue(Currency.Coins, out var found) ? found : null;
            if (anyCoins && coinPolicy is { TransfersEnabled: false })
            {
                Refuse(DuelErrorCode.TransfersDisabled, "Coin transfers are switched off right now, so duels can't be staked with coins.");
            }
            foreach (var user in new[] { challenger, opponent })
            {
                if (!user.IsActive || user.DeletedAt != null || user.IsFrozen || user.TransferLockReason != null)
                {
                    Refuse(DuelErrorCode.AccountLocked, $"{user.Username} can't take part in staked duels right now.", new { userId = user.Id });
                }
            }
            var ageHours = arena.StakeMinAccountAgeHours ?? coinPolicy?.MinSenderAccountAgeHours ?? 0;
            var titleId = arena.StakeMinTitleBracketId ?? coinPolicy?.MinSenderTitleBracketId;
            var title = titleId == null ? null : await _context.TitleBrackets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == titleId, ct);
            var dropMode = r.GearMode == DuelGearMode.OWN_GEAR_DROP;
            foreach (var user in new[] { challenger, opponent })
            {
                var stakes = (user.Id == r.ChallengerUserId ? r.ChallengerCoins : r.OpponentCoins) > 0
                             || r.Items.Any(i => i.OwnerUserId == user.Id);
                if (!stakes && !dropMode) continue;
                var eligibleFrom = ageHours > 0 ? AsUtc(user.CreatedAt).AddHours(ageHours) : (DateTime?)null;
                var titleShort = title != null && user.ExperiencePoints < title.MinExperience;
                if (eligibleFrom > now || titleShort)
                {
                    Refuse(DuelErrorCode.NewAccountRestricted,
                        $"{user.Username}'s account must be {ageHours}h old" + (title != null ? $" and reach the {title.MaleName} title" : "")
                        + " to stake in duels" + (dropMode ? " (drop mode stakes the whole inventory)" : "") + ".",
                        new { userId = user.Id, hours = ageHours, eligibleFrom = eligibleFrom > now ? eligibleFrom : null, title = title?.MaleName });
                }
            }

            // ----- Staff restrictions and open holds (§10.3) -----
            var a = r.ChallengerUserId;
            var b = r.OpponentUserId;
            var restrictions = await _context.DuelRestrictions.AsNoTracking()
                .Where(x => x.LiftedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now)
                            && (x.UserId == a || x.UserId == b || x.OtherUserId == a || x.OtherUserId == b))
                .ToListAsync(ct);
            var restriction = restrictions.Where(x => x.Covers(a, b)).OrderBy(x => x.OtherUserId == null ? 0 : 1).FirstOrDefault();
            if (restriction != null)
            {
                var who = restriction.OtherUserId == null ? NameOf(restriction.UserId) : $"{challenger.Username} and {opponent.Username}";
                Refuse(DuelErrorCode.DuelRestricted,
                    $"{who} can't play staked duels {(restriction.ExpiresAt == null ? "until staff lift it" : $"until {AsUtc(restriction.ExpiresAt.Value):yyyy-MM-dd HH:mm} UTC")}: {restriction.Reason}",
                    new { restrictionId = restriction.Id, userId = restriction.UserId, otherUserId = restriction.OtherUserId, expiresAt = restriction.ExpiresAt, reason = restriction.Reason });
            }
            var hold = await _context.DuelMatches.AsNoTracking()
                .Where(m => m.Review == DuelReviewState.Held
                            && (((m.ChallengerUserId == a && m.OpponentUserId == b) || (m.ChallengerUserId == b && m.OpponentUserId == a))
                                || m.WinnerUserId == a || m.WinnerUserId == b))
                .OrderBy(m => m.Id).FirstOrDefaultAsync(ct);
            if (hold != null)
            {
                Refuse(DuelErrorCode.ReviewHoldOpen,
                    hold.Involves(a) && hold.Involves(b)
                        ? "An earlier duel between you is under staff review; staked duels between you wait until it is resolved."
                        : $"A duel {NameOf(hold.WinnerUserId!.Value)} won is under staff review; their staked duels wait until it is resolved.",
                    new { matchId = hold.Id, winnerUserId = hold.WinnerUserId });
            }

            // ----- Pair and daily limits (§10.2) -----
            var counted = ValueMoving(OfPair(_context.DuelMatches.AsNoTracking(), a, b))
                .Where(m => m.Status != DuelMatchStatus.Cancelled && m.Status != DuelMatchStatus.Aborted);
            if (arena.MaxStakedDuelsPerPairPerDay > 0)
            {
                var since = now.AddHours(-24);
                var today = await counted.CountAsync(m => m.CreatedAt >= since, ct);
                if (today >= arena.MaxStakedDuelsPerPairPerDay)
                {
                    Refuse(DuelErrorCode.PairDailyLimit,
                        $"{challenger.Username} and {opponent.Username} have played {today} staked duels in the last 24 hours (limit {arena.MaxStakedDuelsPerPairPerDay}).",
                        new { count = today, limit = arena.MaxStakedDuelsPerPairPerDay });
                }
            }
            if (arena.PairCooldownMinutes > 0)
            {
                var last = await counted.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstOrDefaultAsync(ct);
                if (last != null)
                {
                    var retryAt = last.EndedAt == null ? (DateTime?)null : AsUtc(last.EndedAt.Value).AddMinutes(arena.PairCooldownMinutes);
                    if (last.EndedAt == null || retryAt > now)
                    {
                        Refuse(DuelErrorCode.PairCooldown,
                            last.EndedAt == null
                                ? "Your previous staked duel together hasn't finished yet."
                                : $"Wait {Math.Max(1, (int)Math.Ceiling((retryAt!.Value - now).TotalMinutes))} min before another staked duel together.",
                            new { minutes = arena.PairCooldownMinutes, retryAt });
                    }
                }
            }
            if (arena.DailyCoinWinCap > 0 && anyCoins)
            {
                var since = now.AddHours(-24);
                var wins = await _context.DuelMatches.AsNoTracking()
                    .Where(m => m.Status == DuelMatchStatus.Completed && m.Review != DuelReviewState.Voided
                                && (m.WinnerUserId == a || m.WinnerUserId == b) && m.EndedAt >= since)
                    .Select(m => new { m.WinnerUserId, m.ChallengerUserId, m.ChallengerCoins, m.OpponentCoins })
                    .ToListAsync(ct);
                foreach (var user in new[] { challenger, opponent })
                {
                    var couldWin = user.Id == a ? r.OpponentCoins : r.ChallengerCoins;
                    if (couldWin <= 0) continue;
                    var won = wins.Where(w => w.WinnerUserId == user.Id)
                        .Sum(w => w.WinnerUserId == w.ChallengerUserId ? w.OpponentCoins : w.ChallengerCoins);
                    if (won + couldWin > arena.DailyCoinWinCap)
                    {
                        Refuse(DuelErrorCode.DailyWinCap,
                            $"{user.Username} can win at most {arena.DailyCoinWinCap:N0} coins from duels a day ({won:N0} won in the last 24 hours, this duel could add {couldWin:N0}).",
                            new { userId = user.Id, cap = arena.DailyCoinWinCap, wonLast24h = won, couldWin, remaining = Math.Max(0, arena.DailyCoinWinCap - won) });
                    }
                }
            }

            // ----- Funds (advisory; the ledger decides at lock-in) -----
            if (includeFunds)
            {
                foreach (var (user, coins) in new[] { (challenger, r.ChallengerCoins), (opponent, r.OpponentCoins) })
                {
                    if (coins > 0 && user.Coins < coins)
                    {
                        Refuse(DuelErrorCode.InsufficientFunds, $"{user.Username} doesn't have {coins:N0} coins.", new { userId = user.Id, required = coins });
                    }
                }
            }
            return refusals;
        }

        /// <summary>
        /// The coin value of each owner's stake items: BasePriceMin of the item instance's blueprint × amount, rounded
        /// down (developer decision 2026-10-21; reversible default, BasePriceMax unused). An item without an instance
        /// id, or whose instance or blueprint is gone, is worth 0.
        /// </summary>
        private async Task<Dictionary<int, long>> ItemValuesAsync(List<DuelItemDto> items, CancellationToken ct)
        {
            var ids = items.Where(i => i.InstanceId.HasValue).Select(i => i.InstanceId!.Value).Distinct().ToList();
            var prices = ids.Count == 0
                ? new Dictionary<long, decimal>()
                : await _context.ItemInstances.AsNoTracking().Where(i => ids.Contains(i.Id))
                    .Select(i => new { i.Id, i.ItemBlueprint.BasePriceMin })
                    .ToDictionaryAsync(i => i.Id, i => i.BasePriceMin, ct);
            return items
                .GroupBy(i => i.OwnerUserId)
                .ToDictionary(g => g.Key, g => g.Sum(i =>
                    i.InstanceId is long id && prices.TryGetValue(id, out var price) && price > 0
                        ? (long)Math.Floor(price * i.Amount)
                        : 0L));
        }

        private static bool Allows(Arena arena, DuelGearMode mode) => mode switch
        {
            DuelGearMode.OWN_GEAR_KEEP => arena.AllowOwnGearKeep,
            DuelGearMode.OWN_GEAR_DROP => arena.AllowOwnGearDrop,
            DuelGearMode.FIXED_KIT => arena.AllowFixedKit,
            DuelGearMode.KIT_CHOICE => arena.AllowKitChoice,
            _ => false
        };

        private static DateTime AsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        // ----- Review inputs (§10.3) -----

        private async Task<DuelPolicy> PolicyAsync(CancellationToken ct) =>
            await _context.DuelPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == DuelPolicy.SingletonId, ct) ?? new DuelPolicy();

        private async Task<DuelReviewOutcome> ReviewAsync(DuelMatch match, DuelTelemetryDto? telemetry, DateTime now, CancellationToken ct)
        {
            var policy = await PolicyAsync(ct);
            var winner = match.WinnerUserId!.Value;
            var loser = match.LoserUserId!.Value;
            var decided = ValueMoving(_context.DuelMatches.AsNoTracking())
                .Where(m => m.Id != match.Id && m.Status == DuelMatchStatus.Completed && m.WinnerUserId != null);

            var priorWinners = await OfPair(decided, winner, loser)
                .OrderByDescending(m => m.EndedAt).ThenByDescending(m => m.Id)
                .Select(m => m.WinnerUserId!.Value)
                .Take(Math.Max(1, policy.S5PairDuels))
                .ToListAsync(ct);

            var funnelSince = now.AddDays(-Math.Max(1, policy.S6WindowDays));
            var losers = await decided.Where(m => m.WinnerUserId == winner && m.EndedAt >= funnelSince)
                .Select(m => m.ChallengerUserId == winner ? m.OpponentUserId : m.ChallengerUserId)
                .ToListAsync(ct);
            losers.Add(loser);
            var youngSince = now.AddDays(-Math.Max(1, policy.S6YoungAccountDays));
            var distinctLosers = losers.Distinct().ToList();
            var young = await _context.Users.AsNoTracking().CountAsync(u => distinctLosers.Contains(u.Id) && u.CreatedAt >= youngSince, ct);

            var (pairFlags, beneficiaryFlags) = await CountingFlagsAsync(match.Id, winner, loser, policy, now, ct);

            return DuelReviewRules.Evaluate(new DuelReviewInput
            {
                Policy = policy,
                GearMode = match.GearMode,
                Telemetry = telemetry,
                WinnerIsChallenger = winner == match.ChallengerUserId,
                WinnerUserId = winner,
                PriorPairWinners = priorWinners,
                YoungOpponents = young,
                PriorPairFlags = pairFlags,
                PriorBeneficiaryFlags = beneficiaryFlags
            });
        }

        /// <summary>
        /// Earlier flags that still count within EscalationWindowDays (DESIGN §10.3): a voided result always
        /// counts (confirmed); a flagged, held or released one counts while its alert is not acknowledged (an
        /// acknowledged flag is a false positive). Pair = either direction; beneficiary = the same winner.
        /// </summary>
        private async Task<(int Pair, int Beneficiary)> CountingFlagsAsync(int excludeMatchId, int winner, int loser, DuelPolicy policy,
            DateTime now, CancellationToken ct)
        {
            var since = now.AddDays(-Math.Max(1, policy.EscalationWindowDays));
            var rows = await _context.DuelMatches.AsNoTracking()
                .Where(m => m.Id != excludeMatchId && m.EndedAt >= since
                            && (m.Review == DuelReviewState.Flagged || m.Review == DuelReviewState.Held
                                || m.Review == DuelReviewState.Released || m.Review == DuelReviewState.Voided)
                            && (m.WinnerUserId == winner
                                || (m.ChallengerUserId == winner && m.OpponentUserId == loser)
                                || (m.ChallengerUserId == loser && m.OpponentUserId == winner)))
                .Select(m => new { m.ChallengerUserId, m.OpponentUserId, m.WinnerUserId, m.Review, m.AlertId })
                .ToListAsync(ct);
            var alertIds = rows.Where(r => r.AlertId.HasValue).Select(r => r.AlertId!.Value).Distinct().ToList();
            var acked = alertIds.Count == 0
                ? new HashSet<long>()
                : (await _context.CurrencyAlerts.AsNoTracking().Where(x => alertIds.Contains(x.Id) && x.AckedAt != null)
                    .Select(x => x.Id).ToListAsync(ct)).ToHashSet();
            var counting = rows.Where(r => r.Review == DuelReviewState.Voided || r.AlertId == null || !acked.Contains(r.AlertId.Value)).ToList();
            var pair = counting.Count(r => (r.ChallengerUserId == winner && r.OpponentUserId == loser) || (r.ChallengerUserId == loser && r.OpponentUserId == winner));
            var beneficiary = counting.Count(r => r.WinnerUserId == winner);
            return (pair, beneficiary);
        }
    }
}

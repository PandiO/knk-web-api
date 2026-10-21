using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Duel
{
    /// <summary>
    /// See <see cref="IDuelMatchService"/>. The shape of SiegeMatchService: the plugin is authoritative for the
    /// live match, this service for the money and the result. Every state change after lock-in runs inside
    /// <see cref="RunLockedAsync{T}"/> (one transaction holding the match row FOR UPDATE), and the ledger postings
    /// it makes enlist in that transaction, so a result is settled exactly once.
    /// <para>
    /// Money (DESIGN.md §7.1; the fee choice is recorded in the chain report's P2 section): DUEL_STAKE debits
    /// both stakes into SYS_DUEL; a paid result is a DUEL_PAYOUT of pot − fee to the winner plus a DUEL_FEE move
    /// of the fee from SYS_DUEL to SYS_FEES in the same transaction; every other end reverses the stake. A
    /// revert reverses the payout (and the fee) and pays the recovered coins back with DUEL_REFUND; a
    /// confiscation moves them to SYS_CONFISCATED (DUEL_CONFISCATION). Σ SYS_DUEL over a closed match is 0.
    /// </para>
    /// </summary>
    public partial class DuelMatchService : IDuelMatchService
    {
        public const string Component = nameof(DuelMatchService);
        public const string SourceType = "DuelMatch";
        public const int DefaultHistoryLimit = 50;
        public const int MaxHistoryLimit = 200;

        private static readonly JsonSerializerOptions Json = new();

        private readonly KnKDbContext _context;
        private readonly ICurrencyService _currency;
        private readonly IUserRepository _users;
        private readonly ICurrencyAlertService _alerts;
        private readonly IAuditLogService? _audit;
        private readonly ILogger<DuelMatchService>? _logger;
        private readonly TimeProvider _clock;

        public DuelMatchService(
            KnKDbContext context,
            ICurrencyService currency,
            IUserRepository users,
            ICurrencyAlertService alerts,
            IAuditLogService? audit = null,
            ILogger<DuelMatchService>? logger = null,
            TimeProvider? clock = null)
        {
            _context = context;
            _currency = currency;
            _users = users;
            _alerts = alerts;
            _audit = audit;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ===== Reads =====

        public async Task<DuelMatchDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var match = await _context.DuelMatches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
            return match == null ? null : await ToDtoAsync(match, ct);
        }

        public async Task<DuelMatchDto?> GetByTokenAsync(string publicToken, CancellationToken ct = default)
        {
            var token = NormalizeToken(publicToken);
            if (token == null) return null;
            var match = await _context.DuelMatches.AsNoTracking().FirstOrDefaultAsync(m => m.PublicToken == token, ct);
            return match == null ? null : await ToDtoAsync(match, ct);
        }

        public async Task<List<DuelMatchDto>> QueryAsync(int? userId, int? arenaId, DuelMatchStatus? status, DuelReviewState? review,
            int limit, CancellationToken ct = default)
        {
            if (limit <= 0) limit = DefaultHistoryLimit;
            limit = Math.Min(limit, MaxHistoryLimit);
            var query = _context.DuelMatches.AsNoTracking().AsQueryable();
            if (userId.HasValue) query = query.Where(m => m.ChallengerUserId == userId || m.OpponentUserId == userId);
            if (arenaId.HasValue) query = query.Where(m => m.ArenaId == arenaId);
            if (status.HasValue) query = query.Where(m => m.Status == status);
            if (review.HasValue) query = query.Where(m => m.Review == review);
            var matches = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(limit).ToListAsync(ct);
            return await ToDtosAsync(matches, ct);
        }

        // ===== Lock-in =====

        public async Task<DuelCheckResultDto> CheckAsync(DuelMatchCreateDto dto, CancellationToken ct = default)
        {
            var request = Validate(dto);
            var arena = await LoadArenaAsync(request.ArenaId, ct);
            var users = await LoadUsersAsync(request, ct);
            var refusals = await EvaluateAsync(request, arena, users, Now, includeFunds: true, ct);
            return new DuelCheckResultDto
            {
                Allowed = refusals.Count == 0,
                ValueMoving = request.IsValueMoving,
                FeePercent = arena.FeePercent,
                FeeCoins = Fee(request.ChallengerCoins + request.OpponentCoins, arena.FeePercent),
                Refusals = refusals.Select(r => new DuelRefusalDto { Code = r.Code.ToString(), Message = r.Message, Details = r.Details }).ToList()
            };
        }

        public async Task<(DuelMatchDto Match, bool Created)> CreateAsync(DuelMatchCreateDto dto, CancellationToken ct = default)
        {
            var request = Validate(dto);

            // A retry of a lock-in that landed (the plugin's timeout path asks by token first, DESIGN §6.5).
            var stored = await FindReplayAsync(request, ct);
            if (stored != null) return (await ToDtoAsync(stored, ct), false);

            var arena = await LoadArenaAsync(request.ArenaId, ct);
            DuelMatch? match = null;
            DuelMatch? replay = null;
            try
            {
                // Both users locked for the whole lock-in, so two lock-ins of the same players serialize and
                // the pair limits, the daily cap and the balances are read after the other one committed.
                await _users.RunWithUsersLockedAsync(new[] { request.ChallengerUserId, request.OpponentUserId }, async () =>
                {
                    replay = await FindReplayAsync(request, ct);
                    if (replay != null) return;

                    var now = Now;
                    var users = await LoadUsersAsync(request, ct);
                    var refusals = await EvaluateAsync(request, arena, users, now, includeFunds: false, ct);
                    if (refusals.Count > 0)
                    {
                        var first = refusals[0];
                        throw new DuelException(first.Code, first.Message, first.Details);
                    }

                    match = new DuelMatch
                    {
                        PublicToken = request.Token,
                        ArenaId = arena.Id,
                        ChallengerUserId = request.ChallengerUserId,
                        OpponentUserId = request.OpponentUserId,
                        GearMode = request.GearMode,
                        FixedKitId = request.GearMode == DuelGearMode.FIXED_KIT ? request.FixedKitId : null,
                        ChallengerKitId = request.GearMode == DuelGearMode.FIXED_KIT ? request.FixedKitId : null,
                        OpponentKitId = request.GearMode == DuelGearMode.FIXED_KIT ? request.FixedKitId : null,
                        Status = DuelMatchStatus.Created,
                        ChallengerCoins = request.ChallengerCoins,
                        OpponentCoins = request.OpponentCoins,
                        FeePercent = arena.FeePercent,
                        StakeItemsJson = request.Items.Count == 0 ? null : JsonSerializer.Serialize(request.Items, Json),
                        ChallengerItemStacks = request.ChallengerItemStacks,
                        OpponentItemStacks = request.OpponentItemStacks,
                        OfferCount = request.OfferCount,
                        CreatedAt = now,
                        LockedAt = request.LockedAt ?? now
                    };
                    _context.DuelMatches.Add(match);
                    await _context.SaveChangesAsync(ct);

                    var legs = new List<CurrencyLeg>();
                    if (request.ChallengerCoins > 0) legs.Add(new CurrencyLeg(request.ChallengerUserId, Currency.Coins, -request.ChallengerCoins));
                    if (request.OpponentCoins > 0) legs.Add(new CurrencyLeg(request.OpponentUserId, Currency.Coins, -request.OpponentCoins));
                    if (legs.Count > 0)
                    {
                        var posting = await _currency.PostAsync(legs, SystemContext(CurrencyReasons.DuelStake,
                            LockKey(request.Token), $"Duel {match.Id} stakes", match.Id), ct);
                        match.StakeTransactionId = posting.TransactionId;
                        await _context.SaveChangesAsync(ct);
                    }
                });
            }
            catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.InsufficientFunds)
            {
                await DiscardAsync(match);
                var userId = DetailInt(ex.Details, "userId");
                var name = userId == request.ChallengerUserId ? "The challenger" : userId == request.OpponentUserId ? "The opponent" : "A duelist";
                throw new DuelException(DuelErrorCode.InsufficientFunds,
                    $"{name} doesn't have enough coins for the stake.", new { userId });
            }
            catch
            {
                await DiscardAsync(match);
                throw;
            }

            if (replay != null) return (await ToDtoAsync(replay, ct), false);
            _logger?.LogInformation("Duel match {MatchId} ({Token}) locked in at arena {ArenaId}: {Challenger} vs {Opponent}, coins {A}+{B}",
                match!.Id, match.PublicToken, match.ArenaId, match.ChallengerUserId, match.OpponentUserId, match.ChallengerCoins, match.OpponentCoins);
            return (await ToDtoAsync(match!, ct), true);
        }

        /// <summary>
        /// A refused lock-in leaves nothing: with a relational database the transaction was rolled back and
        /// the row only needs to leave the change tracker; on EF InMemory (tests) it is deleted again.
        /// </summary>
        private async Task DiscardAsync(DuelMatch? match)
        {
            if (match == null) return;
            var entry = _context.Entry(match);
            if (!_context.Database.IsRelational() && entry.State != EntityState.Detached && match.Id > 0)
            {
                _context.DuelMatches.Remove(match);
                await _context.SaveChangesAsync();
                return;
            }
            entry.State = EntityState.Detached;
        }

        private async Task<DuelMatch?> FindReplayAsync(DuelCreateRequest request, CancellationToken ct)
        {
            var existing = await _context.DuelMatches.AsNoTracking().FirstOrDefaultAsync(m => m.PublicToken == request.Token, ct);
            if (existing == null) return null;
            var same = existing.ArenaId == request.ArenaId
                       && existing.ChallengerUserId == request.ChallengerUserId
                       && existing.OpponentUserId == request.OpponentUserId
                       && existing.GearMode == request.GearMode
                       && existing.ChallengerCoins == request.ChallengerCoins
                       && existing.OpponentCoins == request.OpponentCoins
                       && existing.ChallengerItemStacks == request.ChallengerItemStacks
                       && existing.OpponentItemStacks == request.OpponentItemStacks
                       && (request.GearMode != DuelGearMode.FIXED_KIT || existing.FixedKitId == request.FixedKitId);
            if (!same)
            {
                throw new DuelException(DuelErrorCode.TokenReused,
                    $"Match token {request.Token} was already used for a different duel (match {existing.Id}).", new { matchId = existing.Id });
            }
            return existing;
        }

        // ===== Lifecycle =====

        public async Task<DuelMatchDto> StartFightAsync(int id, DuelStartFightDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw Invalid("A body is required.");
            var match = await RunLockedAsync(id, async () =>
            {
                var match = await LockedMatchAsync(id, ct);
                if (match.Status == DuelMatchStatus.InProgress) return match; // a retried start
                if (match.Status != DuelMatchStatus.Created)
                {
                    throw WrongState($"Duel {id} is {match.Status}; only a Created match can start its fight.");
                }

                var battleground = await _context.ArenaBattlegrounds.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == dto.BattlegroundId, ct);
                if (battleground == null || battleground.ArenaId != match.ArenaId)
                {
                    throw Invalid($"Battleground {dto.BattlegroundId} is not a battleground of arena {match.ArenaId}.");
                }
                if (match.GearMode == DuelGearMode.KIT_CHOICE)
                {
                    var eligible = await EligibleKitIdsAsync(match.ArenaId, ct);
                    foreach (var kit in new[] { dto.ChallengerKitId, dto.OpponentKitId })
                    {
                        if (kit.HasValue && !eligible.Contains(kit.Value))
                        {
                            throw new DuelException(DuelErrorCode.KitNotEligible, $"Kit {kit} isn't one of this arena's duel kits.", new { kitId = kit });
                        }
                    }
                    match.ChallengerKitId = dto.ChallengerKitId;
                    match.OpponentKitId = dto.OpponentKitId;
                }
                match.BattlegroundId = battleground.Id;
                match.Status = DuelMatchStatus.InProgress;
                match.FightStartedAt = ToUtc(dto.StartedAt) ?? Now;
                await _context.SaveChangesAsync(ct);
                return match;
            });
            return await ToDtoAsync(match, ct);
        }

        public async Task<DuelResultDto> CompleteAsync(int id, DuelMatchCompleteDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw Invalid("A body is required.");
            if (dto.EndReason is not (DuelEndReason.Win or DuelEndReason.Forfeit))
            {
                throw Invalid($"{dto.EndReason} isn't a decisive result; send draws, cancels and aborts to POST {{id}}/cancel.");
            }

            var (match, settlement, done) = await RunLockedAsync(id, async () =>
            {
                var match = await LockedMatchAsync(id, ct);
                if (match.Status == DuelMatchStatus.Completed && match.WinnerUserId == dto.WinnerUserId)
                {
                    return (match, SettlementOf(match), true); // a retried result: what was settled, nothing more
                }
                if (match.Status is not (DuelMatchStatus.Created or DuelMatchStatus.InProgress))
                {
                    throw WrongState($"Duel {id} is already {match.Status}{(match.WinnerUserId == null ? "" : $" with winner {match.WinnerUserId}")}; it can't be completed.");
                }
                if (!match.Involves(dto.WinnerUserId))
                {
                    throw Invalid($"User {dto.WinnerUserId} isn't a duelist of match {id}.");
                }

                var now = Now;
                match.Status = DuelMatchStatus.Completed;
                match.EndReason = dto.EndReason;
                match.WinnerUserId = dto.WinnerUserId;
                match.EndedAt = ToUtc(dto.EndedAt) ?? now;
                match.FightTelemetryJson = dto.Telemetry == null ? null : JsonSerializer.Serialize(dto.Telemetry, Json);
                var spoils = (dto.SpoilsItems ?? new List<DuelItemDto>()).Where(i => i != null).ToList();
                match.SpoilsItemsJson = spoils.Count == 0 ? null : JsonSerializer.Serialize(spoils, Json);

                DuelReviewOutcome? outcome = null;
                if (match.IsValueMoving)
                {
                    outcome = await ReviewAsync(match, dto.Telemetry, now, ct);
                    match.ReviewTier = outcome.Tier;
                    match.ReviewSignals = outcome.Signals.Count == 0 ? null : string.Join(",", outcome.Signals);
                    match.ReviewDetailsJson = JsonSerializer.Serialize(new
                    {
                        signals = outcome.Signals,
                        escalatedByPair = outcome.EscalatedByPair,
                        escalatedByBeneficiary = outcome.EscalatedByBeneficiary,
                        values = outcome.Details
                    }, Json);
                }

                DuelSettlement settlement;
                if (outcome is { Tier: 2 })
                {
                    // Tier 2: the coins stay in SYS_DUEL until staff release or void it (DESIGN §7.4).
                    match.Review = DuelReviewState.Held;
                    settlement = DuelSettlement.Held;
                    await _context.SaveChangesAsync(ct);
                    match.AlertId = await RaiseAlertAsync(match, outcome, hold: true, ct);
                }
                else
                {
                    await PayAsync(match, ct);
                    match.Review = outcome is { Tier: 1 } ? DuelReviewState.Flagged : DuelReviewState.None;
                    settlement = DuelSettlement.Paid;
                    await _context.SaveChangesAsync(ct);
                    if (outcome is { Tier: 1 })
                    {
                        match.AlertId = await RaiseAlertAsync(match, outcome, hold: false, ct);
                    }
                }
                await _context.SaveChangesAsync(ct);
                return (match, settlement, false);
            });

            if (!done)
            {
                _logger?.LogInformation("Duel match {MatchId} completed ({Reason}): winner {Winner}, pot {Pot}, fee {Fee}, review {Review} tier {Tier} {Signals}",
                    id, match.EndReason, match.WinnerUserId, match.Pot, match.FeeCoins, match.Review, match.ReviewTier, match.ReviewSignals);
            }
            return new DuelResultDto { Match = await ToDtoAsync(match, ct), Settlement = settlement, AlreadyDone = done };
        }

        public async Task<DuelResultDto> CancelAsync(int id, DuelMatchCancelDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw Invalid("A body is required.");
            var target = TargetStatusOf(dto.EndReason)
                ?? throw Invalid($"{dto.EndReason} is a decisive result; send it to POST {{id}}/complete.");

            var (match, done) = await RunLockedAsync(id, async () =>
            {
                var match = await LockedMatchAsync(id, ct);
                if (match.Status is DuelMatchStatus.Cancelled or DuelMatchStatus.Aborted
                    || match.Status == DuelMatchStatus.Completed && match.WinnerUserId == null)
                {
                    return (match, true); // refunded once already
                }
                if (match.Status == DuelMatchStatus.Completed)
                {
                    throw WrongState($"Duel {id} was won by user {match.WinnerUserId}; a decided result is changed by staff (void, revert).");
                }

                await RefundStakeAsync(match, $"Duel {id} {DescribeEnd(dto.EndReason)}: stakes returned", null, ct);
                match.Status = target;
                match.EndReason = dto.EndReason;
                match.EndedAt = ToUtc(dto.EndedAt) ?? Now;
                if (dto.Telemetry != null) match.FightTelemetryJson = JsonSerializer.Serialize(dto.Telemetry, Json);
                await _context.SaveChangesAsync(ct);
                return (match, false);
            });

            if (!done)
            {
                _logger?.LogInformation("Duel match {MatchId} ended without a winner ({Reason}); stakes returned", id, dto.EndReason);
            }
            return new DuelResultDto { Match = await ToDtoAsync(match, ct), Settlement = DuelSettlement.Refunded, AlreadyDone = done };
        }

        public async Task<DuelAbortUnfinishedResultDto> AbortUnfinishedAsync(DuelEndReason reason, CancellationToken ct = default)
        {
            if (reason is not (DuelEndReason.ServerRestart or DuelEndReason.ApiOutage))
            {
                throw Invalid("Unfinished matches are aborted with ServerRestart or ApiOutage.");
            }
            var ids = await _context.DuelMatches.AsNoTracking()
                .Where(m => m.Status == DuelMatchStatus.Created || m.Status == DuelMatchStatus.InProgress)
                .OrderBy(m => m.Id).Select(m => m.Id).ToListAsync(ct);
            var aborted = new List<int>();
            foreach (var id in ids)
            {
                try
                {
                    var result = await CancelAsync(id, new DuelMatchCancelDto { EndReason = reason }, ct);
                    if (!result.AlreadyDone) aborted.Add(id);
                }
                catch (DuelException ex) when (ex.Code == DuelErrorCode.WrongState)
                {
                    // Completed between the query and the lock: nothing to recover.
                }
            }
            if (aborted.Count > 0)
            {
                _logger?.LogWarning("Aborted {Count} unfinished duel match(es) as {Reason}: {Ids}", aborted.Count, reason, string.Join(",", aborted));
            }
            return new DuelAbortUnfinishedResultDto { AbortedMatchIds = aborted };
        }

        // ===== Money =====

        /// <summary>The fee of a decisive result: FeePercent of the pot, rounded down (D15).</summary>
        public static long Fee(long pot, int feePercent) => pot <= 0 || feePercent <= 0 ? 0 : pot * Math.Min(feePercent, 100) / 100;

        /// <summary>
        /// Pays a decisive result: pot − fee to the winner (DUEL_PAYOUT) and the fee from SYS_DUEL to SYS_FEES
        /// (DUEL_FEE), both in the caller's transaction. Items-only and drop-only matches move no coins.
        /// </summary>
        private async Task PayAsync(DuelMatch match, CancellationToken ct)
        {
            var pot = match.Pot;
            if (pot <= 0) return;
            var fee = Fee(pot, match.FeePercent);
            var payout = pot - fee;
            var paid = await _currency.GrantAsync(match.WinnerUserId!.Value, Currency.Coins, payout,
                SystemContext(CurrencyReasons.DuelPayout, $"duel-payout:{match.Id}", $"Duel {match.Id} winnings", match.Id), ct);
            match.SettlementTransactionId = paid.TransactionId;
            if (fee > 0)
            {
                var moved = await _currency.MoveBetweenSystemAccountsAsync(Currency.Coins, fee,
                    SystemContext(CurrencyReasons.DuelFee, $"duel-fee:{match.Id}", $"Duel {match.Id} arena fee ({match.FeePercent}%)", match.Id), ct);
                match.FeeTransactionId = moved.TransactionId;
            }
            match.FeeCoins = fee;
        }

        /// <summary>Reverses the DUEL_STAKE posting once (key reverse:{txId}); nothing to do for a match without coins.</summary>
        private async Task RefundStakeAsync(DuelMatch match, string text, int? actorUserId, CancellationToken ct)
        {
            if (match.StakeTransactionId is not long stakeTx || match.RefundTransactionId != null) return;
            var reversal = await _currency.ReverseAsync(stakeTx, new ReversalOptions(),
                Context(CurrencyReasons.Reversal, $"reverse:{stakeTx}", text, match.Id, actorUserId), ct);
            match.RefundTransactionId = reversal.TransactionId;
        }

        private static CurrencyContext SystemContext(string reasonCode, string key, string text, int matchId) =>
            Context(reasonCode, key, text, matchId, null);

        /// <summary>A deterministic-key posting for a match, by the service itself or for a staff member.</summary>
        private static CurrencyContext Context(string reasonCode, string key, string text, int matchId, int? actorUserId) => new()
        {
            IdempotencyKey = key,
            IdempotencyScope = CurrencyIdempotencyScopes.System,
            ReasonCode = reasonCode,
            Reason = text.Length <= 500 ? text : text[..500],
            Initiator = actorUserId is > 0 ? CurrencyInitiator.Admin : CurrencyInitiator.System,
            InitiatorUserId = actorUserId is > 0 ? actorUserId : null,
            InitiatorComponent = Component,
            SourceType = SourceType,
            SourceRef = matchId.ToString()
        };

        public static string LockKey(string token) => $"duel-lock:{token}";

        // ===== Locking =====

        /// <summary>
        /// Runs <paramref name="work"/> in one transaction that first locks the match row (SELECT … FOR UPDATE),
        /// so concurrent result calls for one match (plugin retries, a spooled replay, a staff action) serialize
        /// and the second sees the first one's status. On EF InMemory it just runs the work.
        /// </summary>
        private async Task<T> RunLockedAsync<T>(int matchId, Func<Task<T>> work)
        {
            if (!_context.Database.IsRelational())
            {
                return await work();
            }
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM duel_matches WHERE Id = {matchId} FOR UPDATE");
                var result = await work();
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                foreach (var entry in _context.ChangeTracker.Entries<DuelMatch>().Where(e => e.Entity.Id == matchId).ToList())
                {
                    entry.State = EntityState.Detached;
                }
                throw;
            }
        }

        private async Task<DuelMatch> LockedMatchAsync(int id, CancellationToken ct)
        {
            var match = await _context.DuelMatches.FirstOrDefaultAsync(m => m.Id == id, ct)
                ?? throw new DuelException(DuelErrorCode.MatchNotFound, $"Duel match {id} doesn't exist.");
            if (_context.Database.IsRelational())
            {
                await _context.Entry(match).ReloadAsync(ct);
            }
            return match;
        }

        // ===== Helpers =====

        private static DuelMatchStatus? TargetStatusOf(DuelEndReason reason) => reason switch
        {
            DuelEndReason.TimeLimit or DuelEndReason.AgreedDraw or DuelEndReason.SimultaneousDeath => DuelMatchStatus.Completed,
            DuelEndReason.TeleportFailed or DuelEndReason.PlayerLeftLobby or DuelEndReason.NoBattleground
                or DuelEndReason.AdminCancelled => DuelMatchStatus.Cancelled,
            DuelEndReason.ServerRestart or DuelEndReason.ApiOutage => DuelMatchStatus.Aborted,
            _ => null
        };

        private static string DescribeEnd(DuelEndReason reason) => reason switch
        {
            DuelEndReason.TimeLimit or DuelEndReason.AgreedDraw or DuelEndReason.SimultaneousDeath => $"drawn ({reason})",
            DuelEndReason.ServerRestart or DuelEndReason.ApiOutage => $"aborted ({reason})",
            _ => $"cancelled ({reason})"
        };

        /// <summary>The money side of a match as it stands.</summary>
        public static DuelSettlement SettlementOf(DuelMatch match) => match.Status switch
        {
            DuelMatchStatus.Created or DuelMatchStatus.InProgress => DuelSettlement.None,
            DuelMatchStatus.Completed when match.WinnerUserId != null => match.Review switch
            {
                DuelReviewState.Held => DuelSettlement.Held,
                DuelReviewState.Voided => DuelSettlement.Refunded,
                _ => DuelSettlement.Paid
            },
            _ => DuelSettlement.Refunded
        };

        private static DateTime? ToUtc(DateTime? value) => value == null ? null
            : value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime()
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

        private static string? NormalizeToken(string? token) =>
            Guid.TryParse(token?.Trim(), out var guid) ? guid.ToString("D") : null;

        private static int? DetailInt(object? details, string property)
        {
            if (details == null) return null;
            try
            {
                var json = JsonSerializer.SerializeToElement(details);
                return json.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static DuelException Invalid(string message) => new(DuelErrorCode.InvalidRequest, message);

        private static DuelException WrongState(string message) => new(DuelErrorCode.WrongState, message);
    }
}

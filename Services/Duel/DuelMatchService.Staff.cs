using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Duel
{
    // Staff review actions, alerts, the review policy and duel restrictions (arena duels DESIGN.md §7.4,
    // §10.3, §13.3). Every staff action is idempotent and audited with the staff member.
    public partial class DuelMatchService
    {
        // ===== Held results =====

        public async Task<DuelResultDto> ReleaseAsync(int id, int? actorUserId, string? reason, CancellationToken ct = default)
        {
            var (match, done) = await RunLockedAsync(id, async () =>
            {
                var match = await LockedMatchAsync(id, ct);
                if (match.Review == DuelReviewState.Released) return (match, true);
                if (match.Review != DuelReviewState.Held)
                {
                    throw WrongState($"Duel {id} isn't held for review (review: {match.Review}).");
                }
                await PayAsync(match, ct);
                MarkReviewed(match, DuelReviewState.Released, actorUserId, reason);
                await _context.SaveChangesAsync(ct);
                await AuditAsync(actorUserId, match, AuditAction.DuelResultReleased, reason, null);
                return (match, false);
            });
            return new DuelResultDto { Match = await ToDtoAsync(match, ct), Settlement = SettlementOf(match), AlreadyDone = done };
        }

        public async Task<DuelResultDto> VoidAsync(int id, int? actorUserId, string reason, CancellationToken ct = default)
        {
            var text = RequireReason(reason);
            var (match, done) = await RunLockedAsync(id, async () =>
            {
                var match = await LockedMatchAsync(id, ct);
                if (match.Review == DuelReviewState.Voided && match.RevertResultJson == null) return (match, true);
                if (match.Review != DuelReviewState.Held)
                {
                    throw WrongState(match.Review == DuelReviewState.Voided
                        ? $"Duel {id} was already reverted or confiscated."
                        : $"Duel {id} isn't held for review (review: {match.Review}); revert or confiscate a paid result instead.");
                }
                await RefundStakeAsync(match, $"Duel {id} voided by staff: {text}", actorUserId, ct);
                match.UndeliveredItems = DuelUndeliveredItems.ReturnToOwners;
                MarkReviewed(match, DuelReviewState.Voided, actorUserId, text);
                await _context.SaveChangesAsync(ct);
                await AuditAsync(actorUserId, match, AuditAction.DuelResultVoided, text, null);
                return (match, false);
            });
            return new DuelResultDto { Match = await ToDtoAsync(match, ct), Settlement = DuelSettlement.Refunded, AlreadyDone = done };
        }

        // ===== Paid results =====

        public async Task<DuelResultDto> RevertAsync(int id, int? actorUserId, string reason, bool confiscate, CancellationToken ct = default)
        {
            var text = RequireReason(reason);
            var (match, revert, done) = await RunLockedAsync(id, async () =>
            {
                var match = await LockedMatchAsync(id, ct);
                if (match.Review == DuelReviewState.Voided && match.RevertResultJson != null)
                {
                    var stored = JsonSerializer.Deserialize<DuelRevertResultDto>(match.RevertResultJson, Json)!;
                    if (stored.Confiscated == confiscate) return (match, stored, true);
                    throw WrongState($"Duel {id} was already {(stored.Confiscated ? "confiscated" : "reverted")}.");
                }
                if (match.Status != DuelMatchStatus.Completed || match.WinnerUserId == null
                    || match.Review is not (DuelReviewState.None or DuelReviewState.Flagged or DuelReviewState.Released))
                {
                    throw WrongState(match.Review == DuelReviewState.Held
                        ? $"Duel {id} is held, not paid; release or void it."
                        : $"Duel {id} has no paid result to {(confiscate ? "confiscate" : "revert")}.");
                }

                var result = await UndoPayoutAsync(match, actorUserId, text, confiscate, ct);
                match.UndeliveredItems = result.UndeliveredItems;
                match.RevertResultJson = JsonSerializer.Serialize(result, Json);
                MarkReviewed(match, DuelReviewState.Voided, actorUserId, text);
                await _context.SaveChangesAsync(ct);
                await AuditAsync(actorUserId, match, confiscate ? AuditAction.DuelResultConfiscated : AuditAction.DuelResultReverted, text, result);
                return (match, result, false);
            });

            if (!done)
            {
                _logger?.LogWarning("Duel match {MatchId} {Action} by staff {Actor}: recovered {Recovered} of {Payout}, missing {Missing}",
                    id, confiscate ? "confiscated" : "reverted", actorUserId, revert.RecoveredCoins, revert.PayoutCoins, revert.MissingCoins);
            }
            return new DuelResultDto
            {
                Match = await ToDtoAsync(match, ct),
                Settlement = DuelSettlement.Refunded,
                AlreadyDone = done,
                Revert = revert
            };
        }

        /// <summary>
        /// The money side of a revert or confiscation (DESIGN §10.3). The payout is reversed with AllowPartial, so
        /// coins the winner already spent are recorded as missing instead of driving the balance negative.
        /// Revert: the fee is reversed too, and the recovered pool goes to the loser first (up to their stake),
        /// then to the winner (up to theirs) as one DUEL_REFUND posting. Confiscate: the recovered coins go from
        /// SYS_DUEL to SYS_CONFISCATED; the fee stays in SYS_FEES. Either way SYS_DUEL nets to zero for the match.
        /// </summary>
        private async Task<DuelRevertResultDto> UndoPayoutAsync(DuelMatch match, int? actorUserId, string text, bool confiscate, CancellationToken ct)
        {
            var winner = match.WinnerUserId!.Value;
            var loser = match.LoserUserId!.Value;
            var verb = confiscate ? "confiscated" : "reverted";
            var result = new DuelRevertResultDto
            {
                Confiscated = confiscate,
                PayoutCoins = match.Pot - match.FeeCoins,
                UndeliveredItems = confiscate ? DuelUndeliveredItems.Destroy : DuelUndeliveredItems.ReturnToOwners
            };

            if (match.SettlementTransactionId is long payoutTx)
            {
                var reversal = await _currency.ReverseAsync(payoutTx, new ReversalOptions(AllowPartial: true),
                    Context(CurrencyReasons.Reversal, $"reverse:{payoutTx}", $"Duel {match.Id} {verb} by staff: {text}", match.Id, actorUserId), ct);
                result.RecoveredCoins = -reversal.Entries.Where(e => e.UserId == winner && e.Currency == nameof(Currency.Coins)).Sum(e => e.Amount);
                result.TransactionIds.Add(reversal.TransactionId);
            }

            if (confiscate)
            {
                if (result.RecoveredCoins > 0)
                {
                    var moved = await _currency.MoveBetweenSystemAccountsAsync(Currency.Coins, result.RecoveredCoins,
                        Context(CurrencyReasons.DuelConfiscation, $"duel-confiscate:{match.Id}", $"Duel {match.Id} confiscated by staff: {text}", match.Id, actorUserId), ct);
                    result.ConfiscatedCoins = result.RecoveredCoins;
                    result.TransactionIds.Add(moved.TransactionId);
                }
                result.MissingCoins = result.PayoutCoins - result.RecoveredCoins;
                return result;
            }

            if (match.FeeTransactionId is long feeTx)
            {
                var feeBack = await _currency.ReverseAsync(feeTx, new ReversalOptions(),
                    Context(CurrencyReasons.Reversal, $"reverse:{feeTx}", $"Duel {match.Id} fee reversed by staff: {text}", match.Id, actorUserId), ct);
                result.FeeReversedCoins = match.FeeCoins;
                result.TransactionIds.Add(feeBack.TransactionId);
            }

            var pool = result.RecoveredCoins + result.FeeReversedCoins;
            result.ReturnedToLoserCoins = Math.Min(pool, match.CoinsOf(loser));
            result.ReturnedToWinnerCoins = Math.Min(pool - result.ReturnedToLoserCoins, match.CoinsOf(winner));
            var legs = new List<CurrencyLeg>();
            if (result.ReturnedToLoserCoins > 0) legs.Add(new CurrencyLeg(loser, Currency.Coins, result.ReturnedToLoserCoins));
            if (result.ReturnedToWinnerCoins > 0) legs.Add(new CurrencyLeg(winner, Currency.Coins, result.ReturnedToWinnerCoins));
            if (legs.Count > 0)
            {
                var refund = await _currency.PostAsync(legs,
                    Context(CurrencyReasons.DuelRefund, $"duel-revert:{match.Id}", $"Duel {match.Id} reverted by staff: {text}", match.Id, actorUserId), ct);
                result.TransactionIds.Add(refund.TransactionId);
            }
            result.MissingCoins = match.Pot - result.ReturnedToLoserCoins - result.ReturnedToWinnerCoins;
            return result;
        }

        private void MarkReviewed(DuelMatch match, DuelReviewState state, int? actorUserId, string? reason)
        {
            match.Review = state;
            match.ReviewedByUserId = actorUserId;
            match.ReviewedAt = Now;
            match.ReviewReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        }

        private static string RequireReason(string? reason)
        {
            var text = reason?.Trim();
            if (string.IsNullOrEmpty(text)) throw Invalid("A reason is required.");
            if (text.Length > 500) throw Invalid("The reason may be at most 500 characters.");
            return text;
        }

        private async Task AuditAsync(int? actorUserId, DuelMatch match, AuditAction action, string? reason, object? extra)
        {
            if (_audit == null) return;
            await _audit.RecordAsync(actorUserId, match.WinnerUserId ?? match.ChallengerUserId, action, JsonSerializer.Serialize(new
            {
                matchId = match.Id,
                token = match.PublicToken,
                challengerUserId = match.ChallengerUserId,
                opponentUserId = match.OpponentUserId,
                winnerUserId = match.WinnerUserId,
                pot = match.Pot,
                reason,
                result = extra
            }, Json));
        }

        // ===== Alerts (through the currency alert pipeline) =====

        private async Task<long?> RaiseAlertAsync(DuelMatch match, DuelReviewOutcome outcome, bool hold, CancellationToken ct)
        {
            var ids = new[] { match.ChallengerUserId, match.OpponentUserId };
            var names = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
            var arenaKey = await _context.Arenas.AsNoTracking().Where(a => a.Id == match.ArenaId).Select(a => a.Key).FirstOrDefaultAsync(ct);
            var winner = match.WinnerUserId!.Value;
            var loser = match.LoserUserId!.Value;
            string Name(int id) => names.GetValueOrDefault(id, $"#{id}");
            var stakes = match.Pot > 0 ? $"{match.Pot:N0}-coin pot" : "item stakes";
            var why = string.Join(", ", outcome.Signals);
            var summary = hold
                ? $"Duel #{match.Id} at {arenaKey}: {Name(winner)} beat {Name(loser)} ({why}); {stakes} HELD for review"
                  + (outcome.EscalatedByPair ? " (repeat flags for this pair)" : " (repeat flags for this winner)") + "."
                : $"Duel #{match.Id} at {arenaKey}: {Name(winner)} beat {Name(loser)} ({why}); {stakes} paid and flagged.";
            var rule = hold ? CurrencyAlertRules.DuelHold : CurrencyAlertRules.DuelFlag;
            var alert = await _alerts.RaiseAsync(new CurrencyAlertDraft(rule,
                hold ? CurrencyAlertSeverity.High : CurrencyAlertSeverity.Medium, summary, $"{rule}:match:{match.Id}", CurrencyAlertRules.Forever)
            {
                UserId = winner,
                TransactionId = match.SettlementTransactionId,
                Details = new
                {
                    matchId = match.Id,
                    token = match.PublicToken,
                    arenaId = match.ArenaId,
                    winnerUserId = winner,
                    loserUserId = loser,
                    gearMode = match.GearMode.ToString(),
                    pot = match.Pot,
                    itemStacks = match.ChallengerItemStacks + match.OpponentItemStacks,
                    tier = outcome.Tier,
                    signals = outcome.Signals,
                    escalatedByPair = outcome.EscalatedByPair,
                    escalatedByBeneficiary = outcome.EscalatedByBeneficiary,
                    values = outcome.Details
                }
            }, ct);
            return alert?.Id;
        }

        public async Task<DuelEscalationClearResultDto> ClearEscalationAsync(DuelEscalationClearDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null || dto.UserId <= 0) throw Invalid("userId is required.");
            if (dto.OtherUserId == dto.UserId) throw Invalid("A pair needs two different players.");
            if (actorUserId is not > 0) throw Invalid("Clearing an escalation needs a staff member (X-Acting-User-Id from the game server).");
            var a = dto.UserId;
            var other = dto.OtherUserId;
            var query = _context.DuelMatches.AsNoTracking()
                .Where(m => m.AlertId != null && (m.Review == DuelReviewState.Flagged || m.Review == DuelReviewState.Released));
            query = other is int b
                ? OfPair(query, a, b)
                : query.Where(m => m.WinnerUserId == a);
            var alertIds = await query.Select(m => m.AlertId!.Value).ToListAsync(ct);
            var open = await _context.CurrencyAlerts.AsNoTracking()
                .Where(x => alertIds.Contains(x.Id) && x.AckedAt == null).Select(x => x.Id).ToListAsync(ct);
            foreach (var alertId in open)
            {
                await _alerts.AcknowledgeAsync(alertId, actorUserId.Value, ct);
            }
            if (_audit != null)
            {
                await _audit.RecordAsync(actorUserId, a, AuditAction.DuelEscalationCleared, JsonSerializer.Serialize(new
                {
                    userId = a,
                    otherUserId = other,
                    acknowledgedAlertIds = open,
                    reason = dto.Reason
                }, Json));
            }
            return new DuelEscalationClearResultDto { AcknowledgedAlertIds = open };
        }

        // ===== Policy =====

        public async Task<DuelPolicyDto> GetPolicyAsync(CancellationToken ct = default) => ToPolicyDto(await PolicyAsync(ct));

        public async Task<DuelPolicyDto> UpdatePolicyAsync(DuelPolicyDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null) throw Invalid("A body is required.");
            ValidatePolicy(dto);
            var policy = await _context.DuelPolicies.FirstOrDefaultAsync(p => p.Id == DuelPolicy.SingletonId, ct);
            if (policy == null)
            {
                policy = new DuelPolicy();
                _context.DuelPolicies.Add(policy);
            }
            else if (CurrencyPolicy.VersionStamp(AsUtc(dto.UpdatedAt)).Ticks != CurrencyPolicy.VersionStamp(AsUtc(policy.UpdatedAt)).Ticks)
            {
                throw new DuelException(DuelErrorCode.PolicyChanged, "The duel policy changed since you loaded it; reload and try again.", ToPolicyDto(policy));
            }
            var before = ToPolicyDto(policy);
            policy.S1Enabled = dto.S1Enabled;
            policy.S1LoserDamagePercent = dto.S1LoserDamagePercent;
            policy.S2Enabled = dto.S2Enabled;
            policy.S2ShortFightSeconds = dto.S2ShortFightSeconds;
            policy.S3Enabled = dto.S3Enabled;
            policy.S4Enabled = dto.S4Enabled;
            policy.S4MaxLoserAttackAttempts = dto.S4MaxLoserAttackAttempts;
            policy.S5Enabled = dto.S5Enabled;
            policy.S5PairDuels = dto.S5PairDuels;
            policy.S6Enabled = dto.S6Enabled;
            policy.S6MinYoungOpponents = dto.S6MinYoungOpponents;
            policy.S6YoungAccountDays = dto.S6YoungAccountDays;
            policy.S6WindowDays = dto.S6WindowDays;
            policy.EscalationFlagCount = dto.EscalationFlagCount;
            policy.EscalationWindowDays = dto.EscalationWindowDays;
            policy.UpdatedAt = CurrencyPolicy.VersionStamp(Now);
            policy.UpdatedByUserId = actorUserId;
            await _context.SaveChangesAsync(ct);
            if (_audit != null && actorUserId is > 0)
            {
                await _audit.RecordAsync(actorUserId, actorUserId.Value, AuditAction.DuelPolicyChanged,
                    JsonSerializer.Serialize(new { before, after = ToPolicyDto(policy) }, Json));
            }
            return ToPolicyDto(policy);
        }

        private static void ValidatePolicy(DuelPolicyDto p)
        {
            void Require(bool ok, string message)
            {
                if (!ok) throw Invalid(message);
            }
            Require(p.S1LoserDamagePercent is >= 0 and <= 100, "S1: the damage share must be 0–100%.");
            Require(p.S2ShortFightSeconds is >= 0 and <= 3_600, "S2: the short-fight limit must be 0–3,600 seconds.");
            Require(p.S4MaxLoserAttackAttempts is >= 0 and <= 1_000, "S4: the attack attempts must be 0–1,000.");
            Require(p.S5PairDuels is >= 2 and <= 20, "S5: the one-way streak must be 2–20 duels.");
            Require(p.S6MinYoungOpponents is >= 1 and <= 100, "S6: the young opponents must be 1–100.");
            Require(p.S6YoungAccountDays is >= 1 and <= 365, "S6: a young account is 1–365 days old.");
            Require(p.S6WindowDays is >= 1 and <= 90, "S6: the window must be 1–90 days.");
            Require(p.EscalationFlagCount is >= 2 and <= 20, "Escalation needs 2–20 flags (a first flag never holds).");
            Require(p.EscalationWindowDays is >= 1 and <= 90, "The escalation window must be 1–90 days.");
        }

        private static DuelPolicyDto ToPolicyDto(DuelPolicy p) => new()
        {
            S1Enabled = p.S1Enabled,
            S1LoserDamagePercent = p.S1LoserDamagePercent,
            S2Enabled = p.S2Enabled,
            S2ShortFightSeconds = p.S2ShortFightSeconds,
            S3Enabled = p.S3Enabled,
            S4Enabled = p.S4Enabled,
            S4MaxLoserAttackAttempts = p.S4MaxLoserAttackAttempts,
            S5Enabled = p.S5Enabled,
            S5PairDuels = p.S5PairDuels,
            S6Enabled = p.S6Enabled,
            S6MinYoungOpponents = p.S6MinYoungOpponents,
            S6YoungAccountDays = p.S6YoungAccountDays,
            S6WindowDays = p.S6WindowDays,
            EscalationFlagCount = p.EscalationFlagCount,
            EscalationWindowDays = p.EscalationWindowDays,
            UpdatedAt = AsUtc(p.UpdatedAt),
            UpdatedByUserId = p.UpdatedByUserId
        };

        // ===== Restrictions =====

        public async Task<List<DuelRestrictionDto>> GetRestrictionsAsync(int? userId, bool activeOnly, CancellationToken ct = default)
        {
            var now = Now;
            var query = _context.DuelRestrictions.AsNoTracking().AsQueryable();
            if (userId.HasValue) query = query.Where(r => r.UserId == userId || r.OtherUserId == userId);
            if (activeOnly) query = query.Where(r => r.LiftedAt == null && (r.ExpiresAt == null || r.ExpiresAt > now));
            var rows = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(500).ToListAsync(ct);
            return await ToRestrictionDtosAsync(rows, ct);
        }

        public async Task<DuelRestrictionDto> CreateRestrictionAsync(DuelRestrictionCreateDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null) throw Invalid("A body is required.");
            var reason = RequireReason(dto.Reason);
            if (dto.OtherUserId == dto.UserId) throw Invalid("A pair restriction needs two different players.");
            var ids = new[] { dto.UserId, dto.OtherUserId ?? dto.UserId };
            var found = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
            foreach (var id in ids.Distinct())
            {
                if (!found.Contains(id)) throw new DuelException(DuelErrorCode.UserNotFound, $"User {id} doesn't exist.");
            }
            var expires = ToUtc(dto.ExpiresAt);
            if (expires != null && expires <= Now) throw Invalid("The end date must be in the future (or empty for no end date).");

            var restriction = new DuelRestriction
            {
                UserId = dto.UserId,
                OtherUserId = dto.OtherUserId,
                Reason = reason,
                ExpiresAt = expires,
                CreatedAt = Now,
                CreatedByUserId = actorUserId
            };
            _context.DuelRestrictions.Add(restriction);
            await _context.SaveChangesAsync(ct);
            await AuditRestrictionAsync(actorUserId, restriction, AuditAction.DuelRestrictionCreated, reason);
            return (await ToRestrictionDtosAsync(new List<DuelRestriction> { restriction }, ct)).Single();
        }

        public async Task<DuelRestrictionDto> LiftRestrictionAsync(int restrictionId, int? actorUserId, string? reason, CancellationToken ct = default)
        {
            var restriction = await _context.DuelRestrictions.FirstOrDefaultAsync(r => r.Id == restrictionId, ct)
                ?? throw new DuelException(DuelErrorCode.RestrictionNotFound, $"Duel restriction {restrictionId} doesn't exist.");
            if (restriction.LiftedAt == null)
            {
                restriction.LiftedAt = Now;
                restriction.LiftedByUserId = actorUserId;
                restriction.LiftReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(500, reason.Trim().Length)];
                await _context.SaveChangesAsync(ct);
                await AuditRestrictionAsync(actorUserId, restriction, AuditAction.DuelRestrictionLifted, restriction.LiftReason);
            }
            return (await ToRestrictionDtosAsync(new List<DuelRestriction> { restriction }, ct)).Single();
        }

        private async Task AuditRestrictionAsync(int? actorUserId, DuelRestriction r, AuditAction action, string? reason)
        {
            if (_audit == null) return;
            var details = JsonSerializer.Serialize(new { restrictionId = r.Id, userId = r.UserId, otherUserId = r.OtherUserId, expiresAt = r.ExpiresAt, reason }, Json);
            await _audit.RecordAsync(actorUserId, r.UserId, action, details);
            if (r.OtherUserId is int other)
            {
                await _audit.RecordAsync(actorUserId, other, action, details);
            }
        }

        private async Task<List<DuelRestrictionDto>> ToRestrictionDtosAsync(List<DuelRestriction> rows, CancellationToken ct)
        {
            var ids = rows.Select(r => r.UserId).Concat(rows.Where(r => r.OtherUserId.HasValue).Select(r => r.OtherUserId!.Value)).Distinct().ToList();
            var names = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
            var now = Now;
            return rows.Select(r => new DuelRestrictionDto
            {
                Id = r.Id,
                UserId = r.UserId,
                Username = names.GetValueOrDefault(r.UserId),
                OtherUserId = r.OtherUserId,
                OtherUsername = r.OtherUserId.HasValue ? names.GetValueOrDefault(r.OtherUserId.Value) : null,
                Reason = r.Reason,
                ExpiresAt = r.ExpiresAt.HasValue ? AsUtc(r.ExpiresAt.Value) : null,
                CreatedAt = AsUtc(r.CreatedAt),
                CreatedByUserId = r.CreatedByUserId,
                LiftedAt = r.LiftedAt.HasValue ? AsUtc(r.LiftedAt.Value) : null,
                LiftedByUserId = r.LiftedByUserId,
                LiftReason = r.LiftReason,
                Active = r.IsActive(now)
            }).ToList();
        }

        // ===== Mapping =====

        private async Task<DuelMatchDto> ToDtoAsync(DuelMatch match, CancellationToken ct) =>
            (await ToDtosAsync(new List<DuelMatch> { match }, ct)).Single();

        private async Task<List<DuelMatchDto>> ToDtosAsync(List<DuelMatch> matches, CancellationToken ct)
        {
            var userIds = matches.SelectMany(m => new[] { m.ChallengerUserId, m.OpponentUserId }).Distinct().ToList();
            var names = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
            var arenaIds = matches.Select(m => m.ArenaId).Distinct().ToList();
            var arenas = await _context.Arenas.AsNoTracking().Where(a => arenaIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Key, a.Name }).ToDictionaryAsync(a => a.Id, ct);
            var groundIds = matches.Where(m => m.BattlegroundId.HasValue).Select(m => m.BattlegroundId!.Value).Distinct().ToList();
            var grounds = groundIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.ArenaAreas.AsNoTracking().Where(b => groundIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Key, ct);

            return matches.Select(m => new DuelMatchDto
            {
                Id = m.Id,
                PublicToken = m.PublicToken,
                ArenaId = m.ArenaId,
                ArenaKey = arenas.TryGetValue(m.ArenaId, out var arena) ? arena.Key : null,
                ArenaName = arena?.Name,
                BattlegroundId = m.BattlegroundId,
                BattlegroundKey = m.BattlegroundId.HasValue ? grounds.GetValueOrDefault(m.BattlegroundId.Value) : null,
                ChallengerUserId = m.ChallengerUserId,
                ChallengerUsername = names.GetValueOrDefault(m.ChallengerUserId),
                OpponentUserId = m.OpponentUserId,
                OpponentUsername = names.GetValueOrDefault(m.OpponentUserId),
                GearMode = m.GearMode,
                FixedKitId = m.FixedKitId,
                ChallengerKitId = m.ChallengerKitId,
                OpponentKitId = m.OpponentKitId,
                Status = m.Status,
                EndReason = m.EndReason,
                WinnerUserId = m.WinnerUserId,
                ChallengerCoins = m.ChallengerCoins,
                OpponentCoins = m.OpponentCoins,
                FeePercent = m.FeePercent,
                FeeCoins = m.FeeCoins,
                ValueMoving = m.IsValueMoving,
                StakeItems = Items(m.StakeItemsJson),
                SpoilsItems = Items(m.SpoilsItemsJson),
                StakeTransactionId = m.StakeTransactionId,
                SettlementTransactionId = m.SettlementTransactionId,
                FeeTransactionId = m.FeeTransactionId,
                RefundTransactionId = m.RefundTransactionId,
                Review = m.Review,
                ReviewTier = m.ReviewTier,
                ReviewSignals = string.IsNullOrEmpty(m.ReviewSignals) ? new List<string>() : m.ReviewSignals.Split(',').ToList(),
                ReviewDetails = Element(m.ReviewDetailsJson),
                AlertId = m.AlertId,
                ReviewedByUserId = m.ReviewedByUserId,
                ReviewedAt = m.ReviewedAt.HasValue ? AsUtc(m.ReviewedAt.Value) : null,
                ReviewReason = m.ReviewReason,
                RevertResult = Element(m.RevertResultJson),
                UndeliveredItems = m.UndeliveredItems,
                Telemetry = string.IsNullOrEmpty(m.FightTelemetryJson) ? null : JsonSerializer.Deserialize<DuelTelemetryDto>(m.FightTelemetryJson, Json),
                OfferCount = m.OfferCount,
                CreatedAt = AsUtc(m.CreatedAt),
                LockedAt = AsUtc(m.LockedAt),
                FightStartedAt = m.FightStartedAt.HasValue ? AsUtc(m.FightStartedAt.Value) : null,
                EndedAt = m.EndedAt.HasValue ? AsUtc(m.EndedAt.Value) : null
            }).ToList();
        }

        private static List<DuelItemDto> Items(string? json) =>
            string.IsNullOrEmpty(json) ? new List<DuelItemDto>() : JsonSerializer.Deserialize<List<DuelItemDto>>(json, Json) ?? new List<DuelItemDto>();

        private static JsonElement? Element(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}

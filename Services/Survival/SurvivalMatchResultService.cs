using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services.Survival
{
    // Siege Survival phase 5 (knk-workspace docs/specs/siege-survival/DESIGN.md §3 reached/completed wave, §9 v1
    // per-kill rewards "Accumulate exact values ... round down once at settlement", "Settle once at match end through
    // the existing ledger and idempotency ... for every player still in the match, ghosts included. Leavers forfeit",
    // S32, S62 (no reward on leave, admin abort or restart), §10 "API owns ... durable match results and permanent
    // ledger grants ... no client-supplied ... payout is trusted"; PandiO/knk-workspace#41, link S8a).
    //
    // The plugin posts the S4 SurvivalMatchResultDto once at match end. The API stores one result row per match,
    // recomputes every settled integer from the exact accruals (floor once per player), refuses a body whose integers,
    // totals, flag or key differ (409 SettlementMismatch), and posts ONE multi-leg SURVIVAL_REWARD ledger posting with
    // the deterministic key siege-survival:{matchId} for the players present at the end — all in the match's lock and
    // transaction, so a retry either replays the stored row (same body) or is a 409 ResultAlreadyStored. The ledger's
    // own idempotency makes a repeated posting safe. The PvP SIEGE_REWARD posting never pays for a survival scenario
    // (SiegeMatchService.CompleteAsync asks IsSurvivalScenarioAsync).
    public class SurvivalMatchResultService : ISurvivalMatchResultService
    {
        private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;

        private readonly ISurvivalMatchResultRepository _repo;
        private readonly ISiegeMatchRepository _matches;
        private readonly ISurvivalMysteryBoxRollRepository _rolls;
        private readonly ISurvivalMysteryBoxRollService _rollService;
        private readonly ICurrencyService? _currency;
        private readonly ITitleProgressionService? _titleProgression;
        private readonly IPlayerNotificationQueue? _notifications;
        private readonly ILogger<SurvivalMatchResultService>? _logger;

        public SurvivalMatchResultService(
            ISurvivalMatchResultRepository repo,
            ISiegeMatchRepository matches,
            ISurvivalMysteryBoxRollRepository rolls,
            ISurvivalMysteryBoxRollService rollService,
            ICurrencyService? currency = null,
            ITitleProgressionService? titleProgression = null,
            IPlayerNotificationQueue? notifications = null,
            ILogger<SurvivalMatchResultService>? logger = null)
        {
            _repo = repo;
            _matches = matches;
            _rolls = rolls;
            _rollService = rollService;
            _currency = currency;
            _titleProgression = titleProgression;
            _notifications = notifications;
            _logger = logger;
        }

        public static string LedgerKey(int siegeMatchId) => $"siege-survival:{siegeMatchId}";

        /// <summary>DESIGN §3a + S62: only a game end (team wipe, insta-lose objective, all objectives captured) settles.</summary>
        public static bool Settles(SurvivalEndReason reason) =>
            reason is SurvivalEndReason.TeamWipe or SurvivalEndReason.InstaLoseObjectiveCaptured or SurvivalEndReason.AllObjectivesCaptured;

        /// <summary>The Siege abort reasons mapped onto the survival end reasons (DESIGN §3a "the technical aborts").</summary>
        public static SurvivalEndReason FromAbortReason(SiegeMatchEndReason reason) => reason switch
        {
            SiegeMatchEndReason.AdminStopped => SurvivalEndReason.AdminAbort,
            SiegeMatchEndReason.ServerRestart => SurvivalEndReason.ServerRestart,
            SiegeMatchEndReason.NotEnoughPlayers => SurvivalEndReason.NoPlayersLeft,
            _ => SurvivalEndReason.TechnicalLimit
        };

        // ==== Result ====

        public async Task<(SurvivalMatchResultDto Result, bool Created)> PostResultAsync(int siegeMatchId, SurvivalMatchResultDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            Validate(siegeMatchId, dto);
            var fingerprint = Fingerprint(dto);

            var titleChanges = new List<(User User, TitleChangeResultDto Change)>();
            var outcome = await _matches.RunLockedAsync(siegeMatchId, async () =>
            {
                var match = await _matches.GetByIdAsync(siegeMatchId, includeUsers: false)
                    ?? throw new KeyNotFoundException($"Siege match {siegeMatchId} not found.");

                var existing = await _repo.GetResultAsync(siegeMatchId);
                if (existing != null)
                {
                    if (existing.RequestFingerprint == fingerprint) return (ToDto(existing), false);
                    throw new SurvivalMatchResultConflictException(SurvivalMatchResultConflictException.ResultAlreadyStored,
                        existing.Source == SurvivalMatchResultSource.Abort
                            ? $"Siege match {siegeMatchId} was aborted ({existing.EndReason}) and its result was recorded by the abort; a different result can't be stored."
                            : $"Siege match {siegeMatchId} already has a stored result ({existing.EndReason}, reached wave {existing.ReachedWave}) with a different body; a result is stored once.");
                }

                if (match.Status == SiegeMatchStatus.Created)
                    throw new SurvivalMatchResultConflictException(SurvivalMatchResultConflictException.MatchNotInProgress,
                        $"Siege match {siegeMatchId} never started; it has no result.");
                if (match.Status == SiegeMatchStatus.Aborted)
                    throw new SurvivalMatchResultConflictException(SurvivalMatchResultConflictException.MatchAborted,
                        $"Siege match {siegeMatchId} was aborted ({match.EndReason}); nothing settles (S62).");

                await RequireUsersExistAsync(dto.Players.Select(p => p.UserId));

                var settings = await _repo.GetSurvivalSettingsAsync(match.SiegeScenarioId);
                if (settings == null)
                    _logger?.LogWarning("Survival result posted for siege match {MatchId} whose scenario {ScenarioId} has no survival settings; stored anyway", siegeMatchId, match.SiegeScenarioId);

                var settles = Settles(dto.EndReason);
                var key = LedgerKey(siegeMatchId);
                CheckSettlement(siegeMatchId, dto, settles, key);

                var now = DateTime.UtcNow;
                var rollCounts = await ForfeitOpenRollsAndCountAsync(siegeMatchId);
                var result = new SurvivalMatchResult
                {
                    SiegeMatchId = siegeMatchId,
                    SiegeLobbyId = dto.SiegeLobbyId > 0 ? dto.SiegeLobbyId : match.SiegeLobbyId,
                    SiegeScenarioId = dto.SiegeScenarioId > 0 ? dto.SiegeScenarioId : match.SiegeScenarioId,
                    DifficultyProfileId = dto.DifficultyProfileId,
                    ConfigurationGeneratedAt = dto.ConfigurationGeneratedAt,
                    EquipmentMode = dto.EquipmentMode ?? settings?.EquipmentMode ?? SurvivalEquipmentMode.OwnInventory,
                    EndReason = dto.EndReason,
                    ReachedWave = dto.ReachedWave,
                    CompletedWave = dto.CompletedWave,
                    StartedAt = dto.StartedAt ?? match.StartedAt,
                    EndedAt = dto.EndedAt ?? match.EndedAt ?? now,
                    PlayersAtStart = dto.Players.Count,
                    PlayersAtEnd = dto.Players.Count(p => p.PresentAtEnd),
                    EarnedKills = dto.EarnedKills,
                    SystemKills = dto.SystemKills,
                    RewardPolicyVersion = SurvivalMatchResult.RewardPolicyV1PerKill,
                    SettlesRewards = settles,
                    LedgerKey = settles ? key : null,
                    Source = SurvivalMatchResultSource.Plugin,
                    RequestFingerprint = fingerprint,
                    RecordedAt = now
                };
                foreach (var p in dto.Players.OrderBy(p => p.UserId))
                {
                    var counts = rollCounts.GetValueOrDefault(p.UserId);
                    result.Players.Add(new SurvivalMatchPlayerResult
                    {
                        UserId = p.UserId,
                        PresentAtEnd = p.PresentAtEnd,
                        LeftEarly = !p.PresentAtEnd,
                        LeftAt = p.LeftAt,
                        KitId = p.KitId,
                        Kills = p.Kills,
                        BonusKills = p.BonusKills,
                        Deaths = p.Deaths,
                        RepairWork = p.RepairWork,
                        MatchCurrencyEarned = p.MatchCurrencyEarned,
                        MatchCurrencySpent = p.MatchCurrencySpent,
                        FinalMatchBalance = p.FinalMatchBalance,
                        CoinsAccrued = p.CoinsAccrued,
                        XpAccrued = p.XpAccrued,
                        CoinsSettled = p.CoinsSettled,
                        XpSettled = p.XpSettled,
                        MysteryBoxRolls = counts.Rolls,
                        MysteryBoxClaims = counts.Claims,
                        MysteryBoxForfeits = counts.Forfeits
                    });
                }
                result.TotalCoinsSettled = result.Players.Sum(p => p.CoinsSettled);
                result.TotalXpSettled = result.Players.Sum(p => p.XpSettled);

                if (settles)
                {
                    // One posting for the match (currency DESIGN.md §3.10 step 5; siege-survival DESIGN §9), inside
                    // this transaction. XP goes through the shared title path so brackets advance exactly as for
                    // the PvP reward. No salary/rank multiplier: v1 pays the per-kill table as is (S32).
                    var legs = new List<CurrencyLeg>();
                    foreach (var p in result.Players.Where(p => p.PresentAtEnd))
                    {
                        if (p.CoinsSettled > 0) legs.Add(new CurrencyLeg(p.UserId, Currency.Coins, p.CoinsSettled));
                        if (p.XpSettled > 0) legs.Add(new CurrencyLeg(p.UserId, Currency.Experience, p.XpSettled));
                    }
                    if (legs.Count > 0)
                    {
                        if (_currency == null || _titleProgression == null)
                            throw new InvalidOperationException("SurvivalMatchResultService needs ICurrencyService and ITitleProgressionService to settle rewards.");
                        var posting = await _currency.PostAsync(legs,
                            CurrencyContext.ForSystem(nameof(SurvivalMatchResultService), CurrencyReasons.SurvivalReward, key,
                                $"Siege Survival match {siegeMatchId} per-kill reward") with
                            {
                                SourceType = "SiegeMatch",
                                SourceRef = siegeMatchId.ToString()
                            });
                        if (posting.Replayed)
                            _logger?.LogWarning("Survival settlement {Key} replayed an existing ledger posting {PublicId}", key, posting.PublicId);
                        result.LedgerPostingPublicId = posting.PublicId;
                        var users = (await _matches.GetUsersAsync(legs.Select(l => l.UserId))).ToDictionary(u => u.Id);
                        foreach (var (userId, change) in await _titleProgression.ApplyForPostingAsync(posting, null))
                        {
                            if (users.TryGetValue(userId, out var user)) titleChanges.Add((user, change));
                        }
                    }
                    result.SettledAt = now;
                }

                await _repo.AddResultAsync(result);
                return (ToDto(result), true);
            });

            if (outcome.Item2)
            {
                foreach (var (user, change) in titleChanges)
                    _notifications?.Enqueue(user.Id, user.Uuid, user.Username, PlayerNotificationTypes.TitleChanged, change);
                var r = outcome.Item1;
                _logger?.LogInformation("Survival match {MatchId} result stored ({EndReason}, reached {Reached}, completed {Completed}); settled {Coins} coins / {Xp} XP to {Players} player(s)",
                    siegeMatchId, r.EndReason, r.ReachedWave, r.CompletedWave, r.Settlement.TotalCoins, r.Settlement.TotalXp, r.Settlement.PlayersSettled);
            }
            return outcome;
        }

        public async Task<SurvivalMatchResultDto?> GetResultAsync(int siegeMatchId)
        {
            var result = await _repo.GetResultAsync(siegeMatchId);
            return result == null ? null : ToDto(result);
        }

        public async Task<PagedResultDto<SurvivalMatchResultDto>> QueryResultsAsync(int? siegeScenarioId, int? userId, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
            var (items, total) = await _repo.QueryResultsAsync(siegeScenarioId, userId, page, pageSize);
            return new PagedResultDto<SurvivalMatchResultDto>
            {
                Items = items.Select(ToDto).ToList(),
                TotalCount = total,
                PageNumber = page,
                PageSize = pageSize
            };
        }

        // ==== Checkpoint ====

        public async Task<(SurvivalMatchCheckpointDto Checkpoint, bool Created)> PutCheckpointAsync(int siegeMatchId, SurvivalMatchCheckpointDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.SiegeMatchId.HasValue && dto.SiegeMatchId.Value != siegeMatchId)
                throw new ArgumentException($"siegeMatchId {dto.SiegeMatchId} in the body does not match the route ({siegeMatchId}).");
            if (!Enum.IsDefined(dto.Substate)) throw new ArgumentException("substate is not a survival substate.");
            if (dto.Wave < 0 || dto.CompletedWave < 0 || dto.CompletedWave > dto.Wave)
                throw new ArgumentException("wave and completedWave must be >= 0 with completedWave <= wave.");
            if (dto.AliveEnemies < 0 || dto.QueuedEnemies < 0) throw new ArgumentException("aliveEnemies and queuedEnemies can't be negative.");
            if (dto.CapturedAt == default) throw new ArgumentException("capturedAt is required.");

            _ = await _matches.GetByIdAsync(siegeMatchId, includeUsers: false)
                ?? throw new KeyNotFoundException($"Siege match {siegeMatchId} not found.");

            dto.SiegeMatchId = siegeMatchId;
            dto.UpdatedAt = null;
            dto.Revision = null;
            var now = DateTime.UtcNow;
            var existing = await _repo.GetCheckpointAsync(siegeMatchId);
            if (existing == null)
            {
                var row = new SurvivalMatchCheckpoint { SiegeMatchId = siegeMatchId, CreatedAt = now };
                Apply(row, dto, now);
                row.Revision = 1;
                await _repo.AddCheckpointAsync(row);
                return (ToDto(row), true);
            }
            if (dto.CapturedAt < existing.CapturedAt)
            {
                // An older checkpoint arriving after a newer one (retry, reordering): the stored one stays.
                return (ToDto(existing), false);
            }
            Apply(existing, dto, now);
            existing.Revision++;
            await _repo.SaveChangesAsync();
            return (ToDto(existing), false);
        }

        public async Task<SurvivalMatchCheckpointDto?> GetCheckpointAsync(int siegeMatchId)
        {
            var row = await _repo.GetCheckpointAsync(siegeMatchId);
            return row == null ? null : ToDto(row);
        }

        // ==== Hooks for the Siege match lifecycle ====

        public async Task<bool> IsSurvivalScenarioAsync(int siegeScenarioId)
        {
            var settings = await _repo.GetSurvivalSettingsAsync(siegeScenarioId);
            return settings is { Enabled: true };
        }

        public async Task OnMatchAbortedAsync(SiegeMatch match, SiegeMatchEndReason endReason, DateTime endedAt)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var settings = await _repo.GetSurvivalSettingsAsync(match.SiegeScenarioId);
            if (settings is not { Enabled: true }) return;

            var rollCounts = await ForfeitOpenRollsAndCountAsync(match.Id);
            if (await _repo.GetResultAsync(match.Id) != null) return;

            var reason = FromAbortReason(endReason);
            var now = DateTime.UtcNow;
            var result = new SurvivalMatchResult
            {
                SiegeMatchId = match.Id,
                SiegeLobbyId = match.SiegeLobbyId,
                SiegeScenarioId = match.SiegeScenarioId,
                EquipmentMode = settings.EquipmentMode,
                EndReason = reason,
                ReachedWave = 0,
                CompletedWave = 0,
                StartedAt = match.StartedAt,
                EndedAt = endedAt,
                PlayersAtStart = match.Participants.Count,
                PlayersAtEnd = match.Participants.Count(p => p.LeftAt == null),
                RewardPolicyVersion = SurvivalMatchResult.RewardPolicyV1PerKill,
                SettlesRewards = false,
                Source = SurvivalMatchResultSource.Abort,
                RequestFingerprint = null,
                RecordedAt = now
            };
            foreach (var p in match.Participants.GroupBy(p => p.UserId).Select(g => g.First()).OrderBy(p => p.UserId))
            {
                var counts = rollCounts.GetValueOrDefault(p.UserId);
                result.Players.Add(new SurvivalMatchPlayerResult
                {
                    UserId = p.UserId,
                    PresentAtEnd = p.LeftAt == null,
                    LeftEarly = p.LeftAt != null,
                    LeftAt = p.LeftAt,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                    MysteryBoxRolls = counts.Rolls,
                    MysteryBoxClaims = counts.Claims,
                    MysteryBoxForfeits = counts.Forfeits
                });
            }
            await _repo.AddResultAsync(result);
            _logger?.LogInformation("Survival match {MatchId} aborted ({SiegeReason} -> {Reason}); result recorded without settlement, {Rolls} open Mystery Box roll(s) forfeited",
                match.Id, endReason, reason, rollCounts.Values.Sum(c => c.ForfeitedNow));
        }

        // ==== Settlement checks ====

        private static void Validate(int siegeMatchId, SurvivalMatchResultDto dto)
        {
            if (dto.SiegeMatchId.HasValue && dto.SiegeMatchId.Value != siegeMatchId)
                throw new ArgumentException($"siegeMatchId {dto.SiegeMatchId} in the body does not match the route ({siegeMatchId}).");
            if (!Enum.IsDefined(dto.EndReason)) throw new ArgumentException("endReason is not a survival end reason.");
            if (dto.EquipmentMode.HasValue && !Enum.IsDefined(dto.EquipmentMode.Value)) throw new ArgumentException("equipmentMode is not an equipment mode.");
            if (dto.ReachedWave < 0 || dto.CompletedWave < 0 || dto.CompletedWave > dto.ReachedWave)
                throw new ArgumentException("reachedWave and completedWave must be >= 0 with completedWave <= reachedWave.");
            if (dto.EarnedKills < 0 || dto.SystemKills < 0) throw new ArgumentException("earnedKills and systemKills can't be negative.");
            if (dto.Players == null) throw new ArgumentException("players is required (empty when nobody took part).");
            if (dto.Settlement == null) throw new ArgumentException("settlement is required.");
            if (dto.Players.Select(p => p.UserId).Distinct().Count() != dto.Players.Count)
                throw new ArgumentException("players lists a user more than once.");
            foreach (var p in dto.Players)
            {
                if (p.UserId <= 0) throw new ArgumentException("players[].userId is required.");
                if (p.Kills < 0 || p.BonusKills < 0 || p.Deaths < 0 || p.RepairWork < 0)
                    throw new ArgumentException($"players[{p.UserId}]: kills, bonusKills, deaths and repairWork can't be negative.");
                if (p.BonusKills > p.Kills) throw new ArgumentException($"players[{p.UserId}]: bonusKills can't exceed kills.");
                if (p.MatchCurrencyEarned < 0 || p.MatchCurrencySpent < 0)
                    throw new ArgumentException($"players[{p.UserId}]: matchCurrencyEarned and matchCurrencySpent can't be negative.");
                if (p.CoinsAccrued < 0 || p.XpAccrued < 0) throw new ArgumentException($"players[{p.UserId}]: coinsAccrued and xpAccrued can't be negative.");
                if (p.CoinsAccrued >= int.MaxValue || p.XpAccrued >= int.MaxValue) throw new ArgumentException($"players[{p.UserId}]: accruals are out of range.");
                if (p.CoinsSettled < 0 || p.XpSettled < 0) throw new ArgumentException($"players[{p.UserId}]: coinsSettled and xpSettled can't be negative.");
            }
            if (dto.Settlement.TotalCoins < 0 || dto.Settlement.TotalXp < 0) throw new ArgumentException("settlement totals can't be negative.");
        }

        /// <summary>
        /// DESIGN §9/§10: the API recomputes what is paid — floor of the exact accrual, once per player, for the
        /// players present at the end of a match whose end reason settles; nothing for leavers or aborts — and
        /// refuses a body that claims anything else, so a plugin bug can't pay out.
        /// </summary>
        private static void CheckSettlement(int siegeMatchId, SurvivalMatchResultDto dto, bool settles, string key)
        {
            var problems = new List<string>();
            if (dto.Settlement.SettlesRewards != settles)
                problems.Add($"settlesRewards is {dto.Settlement.SettlesRewards.ToString().ToLowerInvariant()} but end reason {dto.EndReason} {(settles ? "settles" : "does not settle")} (S62)");
            if (dto.Settlement.LedgerKey != null && dto.Settlement.LedgerKey != key)
                problems.Add($"ledgerKey '{dto.Settlement.LedgerKey}' must be '{key}'");

            var totalCoins = 0;
            var totalXp = 0;
            foreach (var p in dto.Players)
            {
                var pays = settles && p.PresentAtEnd;
                var coins = pays ? (int)decimal.Floor(p.CoinsAccrued) : 0;
                var xp = pays ? (int)decimal.Floor(p.XpAccrued) : 0;
                if (p.CoinsSettled != coins || p.XpSettled != xp)
                    problems.Add($"user {p.UserId}: settled {p.CoinsSettled} coins / {p.XpSettled} XP, recomputed {coins} / {xp}" +
                                 (pays ? $" from accrued {p.CoinsAccrued.ToString(CultureInfo.InvariantCulture)} / {p.XpAccrued.ToString(CultureInfo.InvariantCulture)}" : p.PresentAtEnd ? " (nothing settles)" : " (left early, forfeits)"));
                totalCoins += coins;
                totalXp += xp;
            }
            if (dto.Settlement.TotalCoins != totalCoins || dto.Settlement.TotalXp != totalXp)
                problems.Add($"totals {dto.Settlement.TotalCoins} coins / {dto.Settlement.TotalXp} XP, recomputed {totalCoins} / {totalXp}");

            if (problems.Count > 0)
                throw new SurvivalMatchResultConflictException(SurvivalMatchResultConflictException.SettlementMismatch,
                    $"Siege match {siegeMatchId}: the settlement does not match the API's recomputation: " + string.Join("; ", problems) + ".");
        }

        private async Task RequireUsersExistAsync(IEnumerable<int> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return;
            var found = await _matches.GetExistingUserIdsAsync(ids);
            var missing = ids.Where(id => !found.Contains(id)).ToList();
            if (missing.Count > 0) throw new ArgumentException($"User(s) {string.Join(", ", missing)} do not exist.");
        }

        private readonly record struct RollCounts(int Rolls, int Claims, int Forfeits, int ForfeitedNow);

        /// <summary>S59/S7a: every roll of the match still Rolled is forfeited (MatchEnded); then the per-user counts.</summary>
        private async Task<Dictionary<int, RollCounts>> ForfeitOpenRollsAndCountAsync(int siegeMatchId)
        {
            var rolls = await _rolls.ListRollsAsync(siegeMatchId);
            var forfeitedNow = new HashSet<string>();
            foreach (var open in rolls.Where(r => r.State == SurvivalMysteryBoxRollState.Rolled).ToList())
            {
                await _rollService.ForfeitAsync(siegeMatchId, open.RollId, new SurvivalMysteryBoxForfeitRequestDto { Reason = SurvivalMysteryBoxForfeitReason.MatchEnded });
                forfeitedNow.Add(open.RollId);
            }
            return rolls.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => new RollCounts(
                g.Count(),
                g.Count(r => r.State == SurvivalMysteryBoxRollState.Claimed),
                g.Count(r => r.State == SurvivalMysteryBoxRollState.Forfeited || forfeitedNow.Contains(r.RollId)),
                g.Count(r => forfeitedNow.Contains(r.RollId))));
        }

        // ==== Fingerprint and mapping ====

        /// <summary>SHA-256 over the plugin-supplied fields (API-filled fields left out), players by user id.</summary>
        public static string Fingerprint(SurvivalMatchResultDto dto)
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            void Add(object? v) => sb.Append(v switch
            {
                null => "~",
                DateTime d => d.ToUniversalTime().ToString("O", inv),
                IFormattable f => f.ToString(null, inv),
                _ => v.ToString()
            }).Append('|');
            Add(dto.SiegeLobbyId); Add(dto.SiegeScenarioId); Add(dto.DifficultyProfileId); Add(dto.ConfigurationGeneratedAt);
            Add(dto.StartedAt); Add(dto.EndedAt); Add(dto.EndReason); Add(dto.ReachedWave); Add(dto.CompletedWave);
            Add(dto.EarnedKills); Add(dto.SystemKills); Add(dto.EquipmentMode);
            Add(dto.Settlement?.SettlesRewards); Add(dto.Settlement?.TotalCoins); Add(dto.Settlement?.TotalXp); Add(dto.Settlement?.LedgerKey);
            foreach (var p in (dto.Players ?? new()).OrderBy(p => p.UserId))
            {
                sb.Append('[');
                Add(p.UserId); Add(p.PresentAtEnd); Add(p.LeftAt); Add(p.KitId); Add(p.Kills); Add(p.BonusKills); Add(p.Deaths); Add(p.RepairWork);
                Add(p.MatchCurrencyEarned); Add(p.MatchCurrencySpent); Add(p.FinalMatchBalance);
                Add(p.CoinsAccrued); Add(p.XpAccrued); Add(p.CoinsSettled); Add(p.XpSettled);
                sb.Append(']');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }

        public static SurvivalMatchResultDto ToDto(SurvivalMatchResult r) => new()
        {
            SiegeMatchId = r.SiegeMatchId,
            SiegeLobbyId = r.SiegeLobbyId,
            SiegeScenarioId = r.SiegeScenarioId,
            DifficultyProfileId = r.DifficultyProfileId,
            ConfigurationGeneratedAt = r.ConfigurationGeneratedAt,
            StartedAt = r.StartedAt,
            EndedAt = r.EndedAt,
            EndReason = r.EndReason,
            ReachedWave = r.ReachedWave,
            CompletedWave = r.CompletedWave,
            EarnedKills = r.EarnedKills,
            SystemKills = r.SystemKills,
            EquipmentMode = r.EquipmentMode,
            RewardPolicyVersion = r.RewardPolicyVersion,
            Source = r.Source,
            RecordedAt = r.RecordedAt,
            Players = r.Players.OrderBy(p => p.UserId).Select(p => new SurvivalPlayerResultDto
            {
                UserId = p.UserId,
                PresentAtEnd = p.PresentAtEnd,
                LeftAt = p.LeftAt,
                KitId = p.KitId,
                Kills = p.Kills,
                BonusKills = p.BonusKills,
                Deaths = p.Deaths,
                RepairWork = p.RepairWork,
                MatchCurrencyEarned = p.MatchCurrencyEarned,
                MatchCurrencySpent = p.MatchCurrencySpent,
                FinalMatchBalance = p.FinalMatchBalance,
                CoinsAccrued = p.CoinsAccrued,
                XpAccrued = p.XpAccrued,
                CoinsSettled = p.CoinsSettled,
                XpSettled = p.XpSettled,
                MysteryBoxRolls = p.MysteryBoxRolls,
                MysteryBoxClaims = p.MysteryBoxClaims,
                MysteryBoxForfeits = p.MysteryBoxForfeits
            }).ToList(),
            Settlement = new SurvivalSettlementDto
            {
                SettlesRewards = r.SettlesRewards,
                TotalCoins = r.TotalCoinsSettled,
                TotalXp = r.TotalXpSettled,
                LedgerKey = r.LedgerKey,
                SettledAt = r.SettledAt,
                LedgerPostingId = r.LedgerPostingPublicId,
                PlayersSettled = r.Players.Count(p => p.CoinsSettled > 0 || p.XpSettled > 0)
            }
        };

        private static void Apply(SurvivalMatchCheckpoint row, SurvivalMatchCheckpointDto dto, DateTime now)
        {
            row.SiegeScenarioId = dto.SiegeScenarioId;
            row.DifficultyProfileId = dto.DifficultyProfileId;
            row.CapturedAt = dto.CapturedAt;
            row.Substate = dto.Substate;
            row.Wave = dto.Wave;
            row.CompletedWave = dto.CompletedWave;
            row.AliveEnemies = dto.AliveEnemies;
            row.QueuedEnemies = dto.QueuedEnemies;
            row.Players = dto.Players?.Count ?? 0;
            row.Ghosts = dto.Players?.Count(p => p.Ghost) ?? 0;
            row.PayloadJson = JsonSerializer.Serialize(dto, PayloadJson);
            row.UpdatedAt = now;
        }

        public static SurvivalMatchCheckpointDto ToDto(SurvivalMatchCheckpoint row)
        {
            var dto = JsonSerializer.Deserialize<SurvivalMatchCheckpointDto>(row.PayloadJson, PayloadJson) ?? new SurvivalMatchCheckpointDto();
            dto.SiegeMatchId = row.SiegeMatchId;
            dto.SiegeScenarioId = row.SiegeScenarioId;
            dto.DifficultyProfileId = row.DifficultyProfileId;
            dto.CapturedAt = row.CapturedAt;
            dto.Substate = row.Substate;
            dto.Wave = row.Wave;
            dto.CompletedWave = row.CompletedWave;
            dto.AliveEnemies = row.AliveEnemies;
            dto.QueuedEnemies = row.QueuedEnemies;
            dto.UpdatedAt = row.UpdatedAt;
            dto.Revision = row.Revision;
            return dto;
        }
    }
}

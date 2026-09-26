using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Siege Phase 6 (docs/specs/siege-minigame/DESIGN.md §3.10, §5.1, §7.5-7.6, §11.2): the match
    /// lifecycle the plugin checkpoints (create at the draw, start, left, complete or abort) and the
    /// history read. The plugin is authoritative for the live match; this service is authoritative
    /// for the result and the rewards, which it grants server-side exactly once per match.
    /// Rewards are not written to the admin AuditLog - the match rows are their audit trail.
    /// </summary>
    public class SiegeMatchService : ISiegeMatchService
    {
        public const int DefaultHistoryLimit = 50;
        public const int MaxHistoryLimit = 200;

        private readonly ISiegeMatchRepository _repo;
        private readonly ITitleService _titleService;
        private readonly IPlayerNotificationQueue? _notificationQueue;
        private readonly ILogger<SiegeMatchService>? _logger;

        public SiegeMatchService(
            ISiegeMatchRepository repo,
            ITitleService titleService,
            IPlayerNotificationQueue? notificationQueue = null,
            ILogger<SiegeMatchService>? logger = null)
        {
            _repo = repo;
            _titleService = titleService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        // ===== Reads =====

        public async Task<SiegeMatchDto?> GetByIdAsync(int id)
        {
            var match = await _repo.GetByIdAsync(id);
            return match == null ? null : ToDto(match);
        }

        public async Task<List<SiegeMatchSummaryDto>> QueryAsync(int? userId, int? siegeLobbyId, SiegeMatchStatus? status, int limit)
        {
            if (limit <= 0) limit = DefaultHistoryLimit;
            limit = Math.Min(limit, MaxHistoryLimit);

            var matches = await _repo.QueryAsync(userId, siegeLobbyId, status, limit);
            return matches.Select(m => new SiegeMatchSummaryDto
            {
                Id = m.Id,
                SiegeLobbyId = m.SiegeLobbyId,
                SiegeLobbyName = m.SiegeLobby?.Name,
                SiegeScenarioId = m.SiegeScenarioId,
                SiegeScenarioName = m.SiegeScenario?.Name,
                Status = m.Status,
                CreatedAt = m.CreatedAt,
                StartedAt = m.StartedAt,
                EndedAt = m.EndedAt,
                EndReason = m.EndReason,
                WinningAllianceGroup = m.WinningAllianceGroup,
                ParticipantCount = m.Participants.Count,
                Participant = userId.HasValue
                    ? m.Participants.Where(p => p.UserId == userId.Value).Select(ToDto).FirstOrDefault()
                    : null
            }).ToList();
        }

        // ===== Lifecycle =====

        public async Task<SiegeMatchDto> CreateAsync(SiegeMatchCreateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (!await _repo.LobbyExistsAsync(dto.SiegeLobbyId))
                throw new ArgumentException($"SiegeLobby {dto.SiegeLobbyId} does not exist.", nameof(dto));
            if (await _repo.GetScenarioAsync(dto.SiegeScenarioId) == null)
                throw new ArgumentException($"SiegeScenario {dto.SiegeScenarioId} does not exist.", nameof(dto));

            var match = new SiegeMatch
            {
                SiegeLobbyId = dto.SiegeLobbyId,
                SiegeScenarioId = dto.SiegeScenarioId,
                Status = SiegeMatchStatus.Created,
                CreatedAt = DateTime.UtcNow
            };
            await _repo.AddAsync(match);

            var created = await _repo.GetByIdAsync(match.Id);
            return ToDto(created!);
        }

        public async Task<SiegeMatchDto> StartAsync(int id, SiegeMatchStartDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            return await _repo.RunLockedAsync(id, async () =>
            {
                var match = await _repo.GetByIdAsync(id, includeUsers: false)
                    ?? throw new KeyNotFoundException($"SiegeMatch {id} not found.");

                if (match.Status == SiegeMatchStatus.InProgress) return ToDto(match); // retried start
                if (match.Status != SiegeMatchStatus.Created)
                    throw new InvalidOperationException($"SiegeMatch {id} is {match.Status}; only a Created match can start.");

                var scenario = await _repo.GetScenarioAsync(match.SiegeScenarioId)
                    ?? throw new InvalidOperationException($"SiegeScenario {match.SiegeScenarioId} no longer exists.");

                // One row per user; a repeated userId keeps its last team.
                var participants = (dto.Participants ?? new List<SiegeMatchParticipantStartDto>())
                    .GroupBy(p => p.UserId).Select(g => g.Last()).ToList();
                RequireTeamsOfScenario(scenario, participants.Select(p => p.SiegeTeamId));
                await RequireUsersExistAsync(participants.Select(p => p.UserId));

                var startedAt = dto.StartedAt ?? DateTime.UtcNow;
                match.Status = SiegeMatchStatus.InProgress;
                match.StartedAt = startedAt;
                foreach (var p in participants)
                {
                    match.Participants.Add(new SiegeMatchParticipant
                    {
                        UserId = p.UserId,
                        SiegeTeamId = p.SiegeTeamId,
                        JoinedAt = startedAt
                    });
                }

                await _repo.SaveChangesAsync();
                return ToDto(match);
            });
        }

        public async Task ParticipantLeftAsync(int id, int userId, SiegeMatchParticipantLeftDto? dto)
        {
            await _repo.RunLockedAsync(id, async () =>
            {
                var match = await _repo.GetByIdAsync(id, includeUsers: false)
                    ?? throw new KeyNotFoundException($"SiegeMatch {id} not found.");
                if (match.Status == SiegeMatchStatus.Completed || match.Status == SiegeMatchStatus.Aborted)
                    throw new InvalidOperationException($"SiegeMatch {id} is already {match.Status}.");

                var participant = match.Participants.FirstOrDefault(p => p.UserId == userId)
                    ?? throw new KeyNotFoundException($"User {userId} is not a participant of SiegeMatch {id}.");

                if (participant.LeftAt == null) // the first report wins; a repeat is a no-op
                {
                    participant.LeftAt = dto?.LeftAt ?? DateTime.UtcNow;
                    await _repo.SaveChangesAsync();
                }
                return true;
            });
        }

        public async Task<SiegeMatchResultDto> CompleteAsync(int id, SiegeMatchCompleteDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.EndReason == SiegeMatchEndReason.AdminStopped || dto.EndReason == SiegeMatchEndReason.ServerRestart)
                throw new ArgumentException($"{dto.EndReason} aborts a match; use POST {{id}}/abort.", nameof(dto));

            var titleChanges = new List<(User User, TitleChangeResultDto Change)>();

            var result = await _repo.RunLockedAsync(id, async () =>
            {
                var match = await _repo.GetByIdAsync(id, includeUsers: false)
                    ?? throw new KeyNotFoundException($"SiegeMatch {id} not found.");
                var scenario = await _repo.GetScenarioAsync(match.SiegeScenarioId)
                    ?? throw new InvalidOperationException($"SiegeScenario {match.SiegeScenarioId} no longer exists.");

                // Idempotency (DESIGN §7.6): a completed match returns what it granted, nothing more.
                if (match.Status == SiegeMatchStatus.Completed) return StoredResult(match, scenario);
                if (match.Status == SiegeMatchStatus.Aborted)
                    throw new InvalidOperationException($"SiegeMatch {id} was aborted; it can't be completed.");

                var reported = (dto.Participants ?? new List<SiegeMatchParticipantResultDto>())
                    .GroupBy(p => p.UserId).Select(g => g.Last()).ToList();
                var objectiveEntries = dto.Objectives ?? new List<SiegeMatchObjectiveResultInputDto>();
                ValidateCompletion(scenario, dto.WinningAllianceGroup, reported, objectiveEntries);
                await RequireUsersExistAsync(reported.Select(p => p.UserId)
                    .Concat(objectiveEntries.Where(o => o.CapturedByUserId.HasValue).Select(o => o.CapturedByUserId!.Value)));

                var endedAt = dto.EndedAt ?? DateTime.UtcNow;

                // Participants: the reported ones are the members still in the match at the end.
                // A row the plugin didn't report (its "left" call was lost) is closed at the end time.
                var byUser = match.Participants.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First());
                foreach (var r in reported)
                {
                    if (!byUser.TryGetValue(r.UserId, out var row))
                    {
                        // The start call was lost (or the match never started): record them now.
                        row = new SiegeMatchParticipant { UserId = r.UserId, JoinedAt = match.StartedAt ?? endedAt };
                        match.Participants.Add(row);
                        byUser[r.UserId] = row;
                    }
                    row.SiegeTeamId = r.SiegeTeamId;
                    row.Kills = r.Kills;
                    row.Deaths = r.Deaths;
                    row.HighestKillStreak = r.HighestKillStreak;
                    row.Captures = r.Captures;
                }
                var reportedIds = reported.Select(r => r.UserId).ToHashSet();
                foreach (var row in match.Participants.Where(p => !reportedIds.Contains(p.UserId) && p.LeftAt == null))
                {
                    row.LeftAt = endedAt;
                }

                foreach (var o in objectiveEntries)
                {
                    match.ObjectiveResults.Add(new SiegeMatchObjectiveResult
                    {
                        SiegeObjectiveId = o.SiegeObjectiveId,
                        FinalHolderTeamId = o.FinalHolderTeamId,
                        CapturedByUserId = o.CapturedByUserId,
                        CapturedAt = o.CapturedAt
                    });
                }

                match.Status = SiegeMatchStatus.Completed;
                match.EndReason = dto.EndReason;
                match.WinningAllianceGroup = dto.WinningAllianceGroup;
                match.EndedAt = endedAt;

                // Rewards (DESIGN §7.6), granted once, in this transaction.
                var outcomes = SiegeRewardCalculator.Outcomes(scenario, objectiveEntries.Select(o =>
                    new SiegeRewardCalculator.ObjectiveEntry(o.SiegeObjectiveId, o.FinalHolderTeamId, o.CapturedByUserId)));
                var rewards = match.Participants
                    .Select(p => (Row: p, Reward: SiegeRewardCalculator.For(
                        new SiegeRewardCalculator.ParticipantInput(p.UserId, p.SiegeTeamId, p.LeftAt == null),
                        scenario, dto.WinningAllianceGroup, outcomes)))
                    .ToList();

                var grantees = rewards.Where(x => x.Reward.Coins > 0 || x.Reward.Experience > 0 || x.Reward.Gems > 0).ToList();
                await _repo.LockUsersAsync(grantees.Select(x => x.Row.UserId));
                var users = (await _repo.GetUsersAsync(grantees.Select(x => x.Row.UserId))).ToDictionary(u => u.Id);
                var brackets = grantees.Any(x => x.Reward.Experience > 0) ? await _titleService.GetAllOrderedAsync() : null;

                var titleChangeByUser = new Dictionary<int, TitleChangeResultDto>();
                foreach (var (row, reward) in grantees)
                {
                    var user = users[row.UserId];
                    var previousExperience = user.ExperiencePoints;
                    user.Coins += reward.Coins;
                    user.Gems += reward.Gems;
                    user.ExperiencePoints += reward.Experience;
                    // XP goes through the shared title path, so brackets advance (and grant their
                    // bonuses) exactly as for any other XP gain.
                    var change = reward.Experience > 0
                        ? TitleProgression.ApplyExperienceChange(user, previousExperience, brackets)
                        : null;
                    if (change != null)
                    {
                        titleChangeByUser[user.Id] = change;
                        titleChanges.Add((user, change));
                    }

                    row.CoinsAwarded = reward.Coins;
                    row.ExpAwarded = reward.Experience;
                    row.GemsAwarded = reward.Gems;
                }

                await _repo.SaveChangesAsync();

                return new SiegeMatchResultDto
                {
                    MatchId = match.Id,
                    Status = match.Status,
                    EndReason = match.EndReason,
                    WinningAllianceGroup = match.WinningAllianceGroup,
                    AlreadyCompleted = false,
                    Rewards = rewards.Select(x => ToRewardDto(x.Reward, titleChangeByUser.GetValueOrDefault(x.Row.UserId))).ToList()
                };
            });

            // After the commit only: the plugin's poller shows the in-game promotion moment.
            foreach (var (user, change) in titleChanges)
            {
                _notificationQueue?.Enqueue(user.Id, user.Uuid, user.Username, PlayerNotificationTypes.TitleChanged, change);
            }

            if (!result.AlreadyCompleted)
            {
                _logger?.LogInformation("Siege match {MatchId} completed ({EndReason}, alliance {Alliance}); rewards granted to {Count} participant(s)",
                    id, dto.EndReason, dto.WinningAllianceGroup, result.Rewards.Count(r => r.Coins > 0 || r.Experience > 0 || r.Gems > 0));
            }
            return result;
        }

        public async Task<SiegeMatchDto> AbortAsync(int id, SiegeMatchAbortDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            return await _repo.RunLockedAsync(id, async () =>
            {
                var match = await _repo.GetByIdAsync(id, includeUsers: false)
                    ?? throw new KeyNotFoundException($"SiegeMatch {id} not found.");

                if (match.Status == SiegeMatchStatus.Aborted) return ToDto(match); // retried/replayed abort
                if (match.Status == SiegeMatchStatus.Completed)
                    throw new InvalidOperationException($"SiegeMatch {id} is already Completed; it can't be aborted.");

                var endedAt = dto.EndedAt ?? DateTime.UtcNow;
                match.Status = SiegeMatchStatus.Aborted;
                match.EndReason = dto.EndReason;
                match.WinningAllianceGroup = null;
                match.EndedAt = endedAt;
                foreach (var p in match.Participants.Where(p => p.LeftAt == null)) p.LeftAt = endedAt;

                await _repo.SaveChangesAsync();
                return ToDto(match);
            });
        }

        public async Task<SiegeMatchAbortUnfinishedResultDto> AbortUnfinishedAsync(SiegeMatchAbortUnfinishedDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var aborted = new List<int>();
            foreach (var id in await _repo.GetUnfinishedIdsAsync())
            {
                try
                {
                    var match = await AbortAsync(id, new SiegeMatchAbortDto { EndReason = dto.EndReason });
                    if (match.Status == SiegeMatchStatus.Aborted) aborted.Add(id);
                }
                catch (InvalidOperationException)
                {
                    // Completed between the query and the lock - nothing to recover.
                }
            }
            if (aborted.Count > 0)
            {
                _logger?.LogWarning("Aborted {Count} unfinished siege match(es) as {Reason}: {Ids}", aborted.Count, dto.EndReason, string.Join(",", aborted));
            }
            return new SiegeMatchAbortUnfinishedResultDto { AbortedMatchIds = aborted };
        }

        // ===== Validation =====

        private static void RequireTeamsOfScenario(SiegeScenario scenario, IEnumerable<int> teamIds)
        {
            var known = scenario.Teams.Select(t => t.Id).ToHashSet();
            var unknown = teamIds.Where(id => !known.Contains(id)).Distinct().ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"SiegeTeam(s) {string.Join(", ", unknown)} are not teams of SiegeScenario {scenario.Id}.");
        }

        private static void ValidateCompletion(
            SiegeScenario scenario,
            int? winningAllianceGroup,
            List<SiegeMatchParticipantResultDto> participants,
            List<SiegeMatchObjectiveResultInputDto> objectives)
        {
            if (winningAllianceGroup.HasValue && scenario.Teams.All(t => t.AllianceGroup != winningAllianceGroup.Value))
                throw new ArgumentException($"No team of SiegeScenario {scenario.Id} is in alliance group {winningAllianceGroup}.");

            RequireTeamsOfScenario(scenario, participants.Select(p => p.SiegeTeamId)
                .Concat(objectives.Where(o => o.FinalHolderTeamId.HasValue).Select(o => o.FinalHolderTeamId!.Value)));

            var objectiveIds = scenario.Objectives.Select(o => o.Id).ToHashSet();
            var unknown = objectives.Select(o => o.SiegeObjectiveId).Where(id => !objectiveIds.Contains(id)).Distinct().ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"SiegeObjective(s) {string.Join(", ", unknown)} are not objectives of SiegeScenario {scenario.Id}.");

            if (participants.Any(p => p.Kills < 0 || p.Deaths < 0 || p.HighestKillStreak < 0 || p.Captures < 0))
                throw new ArgumentException("Participant stats can't be negative.");
        }

        private async Task RequireUsersExistAsync(IEnumerable<int> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return;
            var found = await _repo.GetExistingUserIdsAsync(ids);
            var missing = ids.Where(id => !found.Contains(id)).ToList();
            if (missing.Count > 0)
                throw new ArgumentException($"User(s) {string.Join(", ", missing)} do not exist.");
        }

        // ===== Mapping =====

        // A completed match's breakdown rebuilt from its stored rows (amounts as granted; counts
        // recomputed with the same calculator).
        private static SiegeMatchResultDto StoredResult(SiegeMatch match, SiegeScenario scenario)
        {
            var outcomes = SiegeRewardCalculator.Outcomes(scenario, match.ObjectiveResults
                .OrderBy(r => r.Id)
                .Select(r => new SiegeRewardCalculator.ObjectiveEntry(r.SiegeObjectiveId, r.FinalHolderTeamId, r.CapturedByUserId)));

            return new SiegeMatchResultDto
            {
                MatchId = match.Id,
                Status = match.Status,
                EndReason = match.EndReason,
                WinningAllianceGroup = match.WinningAllianceGroup,
                AlreadyCompleted = true,
                Rewards = match.Participants.OrderBy(p => p.Id).Select(p =>
                {
                    var computed = SiegeRewardCalculator.For(
                        new SiegeRewardCalculator.ParticipantInput(p.UserId, p.SiegeTeamId, p.LeftAt == null),
                        scenario, match.WinningAllianceGroup, outcomes);
                    return new SiegeMatchRewardDto
                    {
                        UserId = p.UserId,
                        SiegeTeamId = p.SiegeTeamId,
                        PresentAtEnd = p.LeftAt == null,
                        Won = computed.Won,
                        HoldingCount = computed.HoldingCount,
                        CaptureCount = computed.CaptureCount,
                        Coins = p.CoinsAwarded,
                        Experience = p.ExpAwarded,
                        Gems = p.GemsAwarded
                    };
                }).ToList()
            };
        }

        private static SiegeMatchRewardDto ToRewardDto(SiegeRewardCalculator.Reward reward, TitleChangeResultDto? titleChange) => new()
        {
            UserId = reward.UserId,
            SiegeTeamId = reward.TeamId,
            PresentAtEnd = reward.PresentAtEnd,
            Won = reward.Won,
            HoldingCount = reward.HoldingCount,
            CaptureCount = reward.CaptureCount,
            Coins = reward.Coins,
            Experience = reward.Experience,
            Gems = reward.Gems,
            TitleChange = titleChange
        };

        private static SiegeMatchDto ToDto(SiegeMatch m) => new()
        {
            Id = m.Id,
            SiegeLobbyId = m.SiegeLobbyId,
            SiegeLobbyName = m.SiegeLobby?.Name,
            SiegeScenarioId = m.SiegeScenarioId,
            SiegeScenarioName = m.SiegeScenario?.Name,
            Status = m.Status,
            CreatedAt = m.CreatedAt,
            StartedAt = m.StartedAt,
            EndedAt = m.EndedAt,
            EndReason = m.EndReason,
            WinningAllianceGroup = m.WinningAllianceGroup,
            Participants = m.Participants.OrderBy(p => p.Id).Select(ToDto).ToList(),
            ObjectiveResults = m.ObjectiveResults.OrderBy(r => r.Id).Select(r => new SiegeMatchObjectiveResultDto
            {
                SiegeObjectiveId = r.SiegeObjectiveId,
                FinalHolderTeamId = r.FinalHolderTeamId,
                CapturedByUserId = r.CapturedByUserId,
                CapturedAt = r.CapturedAt
            }).ToList()
        };

        private static SiegeMatchParticipantDto ToDto(SiegeMatchParticipant p) => new()
        {
            UserId = p.UserId,
            Username = p.User?.Username,
            SiegeTeamId = p.SiegeTeamId,
            JoinedAt = p.JoinedAt,
            LeftAt = p.LeftAt,
            Kills = p.Kills,
            Deaths = p.Deaths,
            HighestKillStreak = p.HighestKillStreak,
            Captures = p.Captures,
            CoinsAwarded = p.CoinsAwarded,
            ExpAwarded = p.ExpAwarded,
            GemsAwarded = p.GemsAwarded
        };
    }
}

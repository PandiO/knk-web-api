using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Alpha run 1 (PandiO/knk-workspace#174): the siege results log from the existing match tables (no new storage).
    /// Match facts are public; a participant's own numbers follow their statistics visibility settings in the
    /// "siege" context, as on their profile: the player themselves and staff see everything, other signed-in players
    /// what the player set to Everyone, anonymous visitors none of it (null = hidden).
    /// </summary>
    public class SiegeMatchHistoryService : ISiegeMatchHistoryService
    {
        public const int DefaultLimit = 20;
        public const int MaxLimit = 50;

        public const string OutcomeWin = "Win";
        public const string OutcomeLoss = "Loss";
        public const string OutcomeDraw = "Draw";
        public const string OutcomeLeft = "Left";
        public const string OutcomeAborted = "Aborted";

        private readonly ISiegeMatchRepository _matches;
        private readonly IStatisticsRepository _statistics;

        public SiegeMatchHistoryService(ISiegeMatchRepository matches, IStatisticsRepository statistics)
        {
            _matches = matches;
            _statistics = statistics;
        }

        public async Task<SiegeMatchPublicDto?> GetMatchAsync(int matchId, StatisticsViewer viewer)
        {
            var match = await _matches.GetFinishedAsync(matchId);
            if (match == null) return null;
            var visibility = await VisibilityAsync(match.Participants.Select(p => p.UserId), viewer);

            var teams = (match.SiegeScenario?.Teams ?? new List<SiegeTeam>()).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            var objectives = (match.SiegeScenario?.Objectives ?? new List<SiegeObjective>()).ToDictionary(o => o.Id);
            return new SiegeMatchPublicDto
            {
                Id = match.Id,
                SiegeLobbyName = match.SiegeLobby?.Name,
                SiegeScenarioName = match.SiegeScenario?.Name,
                Status = match.Status,
                StartedAt = Utc(match.StartedAt),
                EndedAt = Utc(match.EndedAt),
                EndReason = match.EndReason,
                WinningAllianceGroup = match.WinningAllianceGroup,
                Teams = teams.Select(t => Team(match, t)).ToList(),
                Participants = match.Participants
                    .OrderBy(p => TeamOrder(teams, p.SiegeTeamId)).ThenByDescending(p => p.Kills).ThenBy(p => p.User?.Username)
                    .Select(p => Participant(match, p, viewer, visibility))
                    .ToList(),
                Objectives = match.ObjectiveResults
                    .GroupBy(r => r.SiegeObjectiveId)
                    .Select(g => Objective(g.Key, g.OrderBy(r => r.Id).ToList(), objectives))
                    .OrderByDescending(o => o.InstantVictory).ThenBy(o => SortOrder(objectives, o.SiegeObjectiveId))
                    .ToList()
            };
        }

        public async Task<List<SiegeMatchPublicSummaryDto>?> GetPlayerMatchesAsync(int userId, StatisticsViewer viewer, int limit)
        {
            var user = await _statistics.GetUserAsync(userId);
            if (user == null || !user.IsActive) return null;
            if (limit <= 0) limit = DefaultLimit;
            limit = Math.Min(limit, MaxLimit);

            var matches = await _matches.GetFinishedForUserAsync(userId, limit);
            var visibility = await VisibilityAsync(new[] { userId }, viewer);
            var rows = new List<SiegeMatchPublicSummaryDto>();
            foreach (var match in matches)
            {
                var own = match.Participants.First(p => p.UserId == userId);
                var team = match.SiegeScenario?.Teams.FirstOrDefault(t => t.Id == own.SiegeTeamId);
                rows.Add(new SiegeMatchPublicSummaryDto
                {
                    Id = match.Id,
                    SiegeLobbyName = match.SiegeLobby?.Name,
                    SiegeScenarioName = match.SiegeScenario?.Name,
                    Status = match.Status,
                    StartedAt = Utc(match.StartedAt),
                    EndedAt = Utc(match.EndedAt),
                    EndReason = match.EndReason,
                    ParticipantCount = match.Participants.Count,
                    Team = team == null ? null : Team(match, team),
                    Participant = Participant(match, own, viewer, visibility)
                });
            }
            return rows;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Each other participant's visibility rows (the viewer themselves and staff need none).</summary>
        private async Task<Dictionary<int, List<PlayerStatVisibility>>> VisibilityAsync(IEnumerable<int> userIds, StatisticsViewer viewer)
        {
            var result = new Dictionary<int, List<PlayerStatVisibility>>();
            if (viewer.Kind is StatisticsViewerKind.Anonymous or StatisticsViewerKind.Staff) return result;
            foreach (var id in userIds.Distinct())
            {
                if (id == viewer.UserId) continue;
                result[id] = await _statistics.GetVisibilityAsync(id);
            }
            return result;
        }

        /// <summary>
        /// Whether the viewer may see this participant's value of a statistics setting in the siege context. The
        /// viewer was resolved without a target, so "self" is decided here by user id.
        /// </summary>
        internal static bool CanSee(StatisticsViewer viewer, int participantUserId, string settingKey,
            IReadOnlyDictionary<int, List<PlayerStatVisibility>> visibility)
        {
            if (viewer.Kind == StatisticsViewerKind.Anonymous) return false;
            if (viewer.Kind is StatisticsViewerKind.Self or StatisticsViewerKind.Staff) return true;
            if (viewer.UserId == participantUserId) return true;
            return visibility.TryGetValue(participantUserId, out var rows)
                && StatisticsVisibilityRules.IsPublic(rows, settingKey, StatisticsCatalog.Siege);
        }

        private static SiegeMatchPublicParticipantDto Participant(SiegeMatch match, SiegeMatchParticipant p, StatisticsViewer viewer,
            IReadOnlyDictionary<int, List<PlayerStatVisibility>> visibility)
        {
            bool See(string setting) => CanSee(viewer, p.UserId, setting, visibility);
            var team = match.SiegeScenario?.Teams.FirstOrDefault(t => t.Id == p.SiegeTeamId);
            var present = PresentAtEnd(match, p);
            return new SiegeMatchPublicParticipantDto
            {
                UserId = p.UserId,
                Username = p.User?.Username,
                SiegeTeamId = p.SiegeTeamId,
                Outcome = match.Status == SiegeMatchStatus.Aborted ? OutcomeAborted
                    : !present ? OutcomeLeft
                    : TeamOutcome(match, team),
                PresentAtEnd = present,
                LeftAt = present ? null : Utc(p.LeftAt),
                Kills = See(StatisticsCatalog.PvpKills) ? p.Kills : null,
                Deaths = See(StatisticsCatalog.Deaths) ? p.Deaths : null,
                HighestKillStreak = See(StatisticsCatalog.HighestKillstreak) ? p.HighestKillStreak : null,
                Captures = See(StatisticsCatalog.ObjectivesCaptured) ? p.Captures : null,
                GateDamage = See(SettingGateDamage) ? p.GateDamage : null,
                ActiveSeconds = p.ActiveSeconds,
                MatchSeconds = p.MatchSeconds,
                CoinsAwarded = See(StatisticsCatalog.SettingEconomy) ? p.CoinsAwarded : null,
                GemsAwarded = See(StatisticsCatalog.SettingEconomy) ? p.GemsAwarded : null,
                ExpAwarded = p.ExpAwarded
            };
        }

        /// <summary>The statistics setting that governs gate damage (see StatisticsCatalog.BuildSettings).</summary>
        internal const string SettingGateDamage = "gate_damage";

        /// <summary>A completed match: no leave marker, or one at/after the end (aborted rows are all closed at the end).</summary>
        private static bool PresentAtEnd(SiegeMatch match, SiegeMatchParticipant p) =>
            p.LeftAt == null || (match.EndedAt.HasValue && p.LeftAt.Value >= match.EndedAt.Value
                && match.Status == SiegeMatchStatus.Aborted);

        private static SiegeMatchPublicTeamDto Team(SiegeMatch match, SiegeTeam t) => new()
        {
            Id = t.Id,
            Name = SiegeTeamIdentity.ResolveName(t) ?? $"Team {t.Id}",
            ChatColor = SiegeTeamIdentity.ResolveChatColor(t) ?? "WHITE",
            Role = t.Role,
            AllianceGroup = t.AllianceGroup,
            Outcome = match.Status == SiegeMatchStatus.Aborted ? OutcomeAborted : TeamOutcome(match, t)
        };

        private static string TeamOutcome(SiegeMatch match, SiegeTeam? team)
        {
            if (match.WinningAllianceGroup == null) return OutcomeDraw;
            return team != null && team.AllianceGroup == match.WinningAllianceGroup.Value ? OutcomeWin : OutcomeLoss;
        }

        private static SiegeMatchPublicObjectiveDto Objective(int? objectiveId, List<SiegeMatchObjectiveResult> entries,
            IReadOnlyDictionary<int, SiegeObjective> objectives)
        {
            var objective = objectiveId.HasValue && objectives.TryGetValue(objectiveId.Value, out var o) ? o : null;
            return new SiegeMatchPublicObjectiveDto
            {
                SiegeObjectiveId = objectiveId,
                Name = objective?.Name,
                InstantVictory = objective?.InstantVictory ?? false,
                FinalHolderTeamId = entries.Last().FinalHolderTeamId,
                Captures = entries.Where(e => e.CapturedAt.HasValue || e.CapturedByUserId.HasValue)
                    .Select(e => new SiegeMatchPublicCaptureDto
                    {
                        SiegeTeamId = e.FinalHolderTeamId,
                        CapturedByUserId = e.CapturedByUserId,
                        CapturedByUsername = e.CapturedByUser?.Username,
                        CapturedAt = Utc(e.CapturedAt)
                    }).ToList()
            };
        }

        private static int TeamOrder(List<SiegeTeam> teams, int? teamId)
        {
            var index = teams.FindIndex(t => t.Id == teamId);
            return index < 0 ? int.MaxValue : index;
        }

        private static int SortOrder(IReadOnlyDictionary<int, SiegeObjective> objectives, int? objectiveId) =>
            objectiveId.HasValue && objectives.TryGetValue(objectiveId.Value, out var o) ? o.SortOrder : int.MaxValue;

        private static DateTime? Utc(DateTime? value) =>
            value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    }
}

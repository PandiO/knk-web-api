using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Siege Phase 6 DTOs (docs/specs/siege-minigame/DESIGN.md §3.10, §7.5-7.6, §11.2): the plugin's
    // match lifecycle checkpoints (create -> start -> left* -> complete | abort) and the history read.
    // Shapes follow the plugin's core records (knk-core KnkSiegeMatchRecords): participants by userId
    // + siegeTeamId, one objective entry per capture (or one for the final holder when never captured).

    // ---- Requests ----

    public class SiegeMatchCreateDto
    {
        [JsonPropertyName("siegeLobbyId")] public int SiegeLobbyId { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
    }

    public class SiegeMatchParticipantStartDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("siegeTeamId")] public int SiegeTeamId { get; set; }
    }

    public class SiegeMatchStartDto
    {
        [JsonPropertyName("participants")] public List<SiegeMatchParticipantStartDto> Participants { get; set; } = new();
        // Optional; the server clock is used when absent.
        [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
    }

    public class SiegeMatchParticipantLeftDto
    {
        [JsonPropertyName("leftAt")] public DateTime? LeftAt { get; set; }
    }

    public class SiegeMatchParticipantResultDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("siegeTeamId")] public int SiegeTeamId { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
        [JsonPropertyName("deaths")] public int Deaths { get; set; }
        [JsonPropertyName("highestKillStreak")] public int HighestKillStreak { get; set; }
        [JsonPropertyName("captures")] public int Captures { get; set; }
    }

    // One capture of an objective, or - with capturedByUserId/capturedAt null - its final holder when
    // it was never captured. With AllowRecapture an objective can have several entries; the last one
    // (request order) is its final holder.
    public class SiegeMatchObjectiveResultInputDto
    {
        [JsonPropertyName("siegeObjectiveId")] public int SiegeObjectiveId { get; set; }
        [JsonPropertyName("finalHolderTeamId")] public int? FinalHolderTeamId { get; set; }
        [JsonPropertyName("capturedByUserId")] public int? CapturedByUserId { get; set; }
        [JsonPropertyName("capturedAt")] public DateTime? CapturedAt { get; set; }
    }

    public class SiegeMatchCompleteDto
    {
        // InstantVictory / TimeExpired / TeamEliminated / NotEnoughPlayers (AdminStopped and
        // ServerRestart are aborts - use POST {id}/abort).
        [JsonPropertyName("endReason")] public SiegeMatchEndReason EndReason { get; set; }
        // null = draw (no win reward).
        [JsonPropertyName("winningAllianceGroup")] public int? WinningAllianceGroup { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
        // The members still in the match at the end, with their final stats.
        [JsonPropertyName("participants")] public List<SiegeMatchParticipantResultDto> Participants { get; set; } = new();
        [JsonPropertyName("objectives")] public List<SiegeMatchObjectiveResultInputDto> Objectives { get; set; } = new();
    }

    public class SiegeMatchAbortDto
    {
        [JsonPropertyName("endReason")] public SiegeMatchEndReason EndReason { get; set; } = SiegeMatchEndReason.ServerRestart;
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
    }

    public class SiegeMatchAbortUnfinishedDto
    {
        [JsonPropertyName("endReason")] public SiegeMatchEndReason EndReason { get; set; } = SiegeMatchEndReason.ServerRestart;
    }

    // ---- Responses ----

    public class SiegeMatchParticipantDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("siegeTeamId")] public int? SiegeTeamId { get; set; }
        [JsonPropertyName("joinedAt")] public DateTime JoinedAt { get; set; }
        [JsonPropertyName("leftAt")] public DateTime? LeftAt { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
        [JsonPropertyName("deaths")] public int Deaths { get; set; }
        [JsonPropertyName("highestKillStreak")] public int HighestKillStreak { get; set; }
        [JsonPropertyName("captures")] public int Captures { get; set; }
        [JsonPropertyName("coinsAwarded")] public int CoinsAwarded { get; set; }
        [JsonPropertyName("expAwarded")] public int ExpAwarded { get; set; }
        [JsonPropertyName("gemsAwarded")] public int GemsAwarded { get; set; }
    }

    public class SiegeMatchObjectiveResultDto
    {
        [JsonPropertyName("siegeObjectiveId")] public int? SiegeObjectiveId { get; set; }
        [JsonPropertyName("finalHolderTeamId")] public int? FinalHolderTeamId { get; set; }
        [JsonPropertyName("capturedByUserId")] public int? CapturedByUserId { get; set; }
        [JsonPropertyName("capturedAt")] public DateTime? CapturedAt { get; set; }
    }

    public class SiegeMatchDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeLobbyId")] public int SiegeLobbyId { get; set; }
        [JsonPropertyName("siegeLobbyName")] public string? SiegeLobbyName { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeScenarioName")] public string? SiegeScenarioName { get; set; }
        [JsonPropertyName("status")] public SiegeMatchStatus Status { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
        [JsonPropertyName("endReason")] public SiegeMatchEndReason? EndReason { get; set; }
        [JsonPropertyName("winningAllianceGroup")] public int? WinningAllianceGroup { get; set; }
        [JsonPropertyName("participants")] public List<SiegeMatchParticipantDto> Participants { get; set; } = new();
        [JsonPropertyName("objectiveResults")] public List<SiegeMatchObjectiveResultDto> ObjectiveResults { get; set; } = new();
    }

    // History list row (GET ?userId=&lobbyId=). "participant" is the filtered user's own row when
    // userId was given.
    public class SiegeMatchSummaryDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeLobbyId")] public int SiegeLobbyId { get; set; }
        [JsonPropertyName("siegeLobbyName")] public string? SiegeLobbyName { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeScenarioName")] public string? SiegeScenarioName { get; set; }
        [JsonPropertyName("status")] public SiegeMatchStatus Status { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
        [JsonPropertyName("endReason")] public SiegeMatchEndReason? EndReason { get; set; }
        [JsonPropertyName("winningAllianceGroup")] public int? WinningAllianceGroup { get; set; }
        [JsonPropertyName("participantCount")] public int ParticipantCount { get; set; }
        [JsonPropertyName("participant")] public SiegeMatchParticipantDto? Participant { get; set; }
    }

    // One player's rewards (DESIGN §7.6), for the in-game breakdown message.
    public class SiegeMatchRewardDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("siegeTeamId")] public int? SiegeTeamId { get; set; }
        // False for participants who left before the end (they get nothing).
        [JsonPropertyName("presentAtEnd")] public bool PresentAtEnd { get; set; }
        [JsonPropertyName("won")] public bool Won { get; set; }
        // Objectives the team holds at the end and did not hold at the start.
        [JsonPropertyName("holdingCount")] public int HoldingCount { get; set; }
        // Distinct objectives this player captured.
        [JsonPropertyName("captureCount")] public int CaptureCount { get; set; }
        [JsonPropertyName("coins")] public int Coins { get; set; }
        [JsonPropertyName("experience")] public int Experience { get; set; }
        [JsonPropertyName("gems")] public int Gems { get; set; }
        // Only on the call that granted the rewards (a repeat call returns the stored amounts
        // without it). The plugin's notification poller also receives it (TitleChanged).
        [JsonPropertyName("titleChange")] public TitleChangeResultDto? TitleChange { get; set; }
    }

    public class SiegeMatchResultDto
    {
        [JsonPropertyName("matchId")] public int MatchId { get; set; }
        [JsonPropertyName("status")] public SiegeMatchStatus Status { get; set; }
        [JsonPropertyName("endReason")] public SiegeMatchEndReason? EndReason { get; set; }
        [JsonPropertyName("winningAllianceGroup")] public int? WinningAllianceGroup { get; set; }
        // True when this call found the match already completed and granted nothing.
        [JsonPropertyName("alreadyCompleted")] public bool AlreadyCompleted { get; set; }
        [JsonPropertyName("rewards")] public List<SiegeMatchRewardDto> Rewards { get; set; } = new();
    }

    public class SiegeMatchAbortUnfinishedResultDto
    {
        [JsonPropertyName("abortedMatchIds")] public List<int> AbortedMatchIds { get; set; } = new();
    }
}

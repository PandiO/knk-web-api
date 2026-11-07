using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Siege Survival phase 4 (knk-workspace docs/specs/siege-survival/DESIGN.md §8a, S34, S39, S59;
    /// PandiO/knk-workspace#41, link S7a): the authoritative, idempotent Mystery Box roll inside a running match,
    /// its one claim and its forfeit. Error convention as the Siege services: ArgumentException -> 400,
    /// KeyNotFoundException -> 404, InvalidOperationException (SurvivalMysteryBoxConflictException with a code) -> 409.
    /// </summary>
    public interface ISurvivalMysteryBoxRollService
    {
        /// <summary>
        /// Rolls once for rollId and stores it (Created = true); the same rollId with the same match, point, user and
        /// price answers the stored roll (Created = false); with a different body it is a conflict.
        /// </summary>
        Task<(SurvivalMysteryBoxRollDto Roll, bool Created)> RollAsync(int siegeMatchId, int pointId, SurvivalMysteryBoxRollRequestDto request);

        /// <summary>Records the one delivery of a roll; a repeat answers the stored state; after a forfeit it is a conflict.</summary>
        Task<SurvivalMysteryBoxRollDto> ClaimAsync(int siegeMatchId, string rollId, SurvivalMysteryBoxClaimRequestDto request);

        /// <summary>Forfeits a roll (idempotent); after a claim it is a conflict.</summary>
        Task<SurvivalMysteryBoxRollDto> ForfeitAsync(int siegeMatchId, string rollId, SurvivalMysteryBoxForfeitRequestDto request);

        /// <summary>Every roll of the match, oldest first.</summary>
        Task<List<SurvivalMysteryBoxRollDto>> ListAsync(int siegeMatchId);
    }
}

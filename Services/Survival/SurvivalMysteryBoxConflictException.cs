namespace knkwebapi_v2.Services.Survival
{
    /// <summary>
    /// A 409 of the Mystery Box roll endpoints with a stable code the plugin can branch on (PandiO/knk-workspace#41,
    /// link S7a): RollIdReused (same rollId, different body), MatchNotInProgress, ParticipantLeft, PoolInvalid,
    /// RollForfeited (claim after forfeit), RollClaimed (forfeit after claim).
    /// </summary>
    public class SurvivalMysteryBoxConflictException : InvalidOperationException
    {
        public const string RollIdReused = "RollIdReused";
        public const string MatchNotInProgress = "MatchNotInProgress";
        public const string ParticipantLeft = "ParticipantLeft";
        public const string PoolInvalid = "PoolInvalid";
        public const string RollForfeited = "RollForfeited";
        public const string RollClaimed = "RollClaimed";

        public SurvivalMysteryBoxConflictException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

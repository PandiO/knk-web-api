namespace knkwebapi_v2.Services.Survival
{
    /// <summary>
    /// A 409 of the survival result endpoint with a stable code the plugin can branch on (PandiO/knk-workspace#41,
    /// link S8a): ResultAlreadyStored (a result for the match exists with a different body, or was written by the
    /// abort), SettlementMismatch (the plugin's settled integers, totals, flag or key differ from the API's
    /// recomputation), MatchNotInProgress (the match never started), MatchAborted (an aborted match without a row).
    /// </summary>
    public class SurvivalMatchResultConflictException : InvalidOperationException
    {
        public const string ResultAlreadyStored = "ResultAlreadyStored";
        public const string SettlementMismatch = "SettlementMismatch";
        public const string MatchNotInProgress = "MatchNotInProgress";
        public const string MatchAborted = "MatchAborted";

        public SurvivalMatchResultConflictException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

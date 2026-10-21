namespace knkwebapi_v2.Services.Duel
{
    /// <summary>
    /// Why a duel request was refused (arena duels DESIGN.md §6.3, §7, §10; KNG-213). The controller maps
    /// these to <c>{ code, message, details }</c>: 404 for the NotFound codes, 409 for state conflicts,
    /// 400 for a malformed request, 422 for every rule a duel breaks. Messages are written for players.
    /// </summary>
    public enum DuelErrorCode
    {
        InvalidRequest,
        ArenaNotFound,
        MatchNotFound,
        UserNotFound,
        RestrictionNotFound,

        /// <summary>The token was already used for a different lock-in.</summary>
        TokenReused,

        /// <summary>The match isn't in a state that allows this (e.g. complete a cancelled match).</summary>
        WrongState,

        /// <summary>A policy edit was based on an older version (send the current updatedAt).</summary>
        PolicyChanged,

        DuelsDisabled,
        SelfDuel,
        GearModeNotAllowed,
        KitNotEligible,
        CoinStakesNotAllowed,
        ItemStakesNotAllowed,
        CoinStakeOutOfRange,
        TooManyItemStacks,
        StakeRatioTooLow,
        GemsNotStakeable,

        /// <summary>Coin transfers are switched off (the R1 kill switch or staff); item stakes still work.</summary>
        TransfersDisabled,

        /// <summary>Account age or title below the arena's staking minimum (D14).</summary>
        NewAccountRestricted,

        /// <summary>Frozen, transfer-locked or deactivated.</summary>
        AccountLocked,

        PairDailyLimit,
        PairCooldown,
        DailyWinCap,

        /// <summary>A staff duel restriction covers the player or the pair.</summary>
        DuelRestricted,

        /// <summary>A held result of the pair or the beneficiary is waiting for staff.</summary>
        ReviewHoldOpen,

        InsufficientFunds
    }

    public class DuelException : InvalidOperationException
    {
        public DuelException(DuelErrorCode code, string message, object? details = null) : base(message)
        {
            Code = code;
            Details = details;
        }

        public DuelErrorCode Code { get; }

        public object? Details { get; }
    }
}

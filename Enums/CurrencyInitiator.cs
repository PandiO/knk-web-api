namespace knkwebapi_v2.Enums;

/// <summary>
/// Who started a ledger transaction (currency DESIGN.md §3.2, KNG-23). Stored with
/// InitiatorUserId (a person) and/or InitiatorComponent (a server component such as
/// "SalaryService"). Stored as a TINYINT; never renumber.
/// </summary>
public enum CurrencyInitiator : byte
{
    /// <summary>A player acting on their own account (InitiatorUserId set).</summary>
    Player = 0,

    /// <summary>A staff member acting on someone's account, in-game or on the web (InitiatorUserId set).</summary>
    Admin = 1,

    /// <summary>A server component on its own (InitiatorComponent set, e.g. SalaryService, TitlePromotion).</summary>
    System = 2,

    /// <summary>The game server with a valid service key but no named acting player.</summary>
    PluginService = 3
}

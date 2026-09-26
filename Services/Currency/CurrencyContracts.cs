using System;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services
{
    /// <summary>Idempotency scopes: a key is unique within its scope.</summary>
    public static class CurrencyIdempotencyScopes
    {
        /// <summary>Keys the game server sends (Idempotency-Key header on a plugin-service request).</summary>
        public const string Plugin = "plugin";

        /// <summary>Keys a web client sends.</summary>
        public const string Web = "web";

        /// <summary>Deterministic keys built by server code (salary:…, title-bonus:…, siege-match:…).</summary>
        public const string System = "system";
    }

    /// <summary>
    /// Who, why and which idempotency key, for one ledger posting (currency DESIGN.md §3.3, KNG-23).
    /// <list type="bullet">
    /// <item><see cref="IdempotencyKey"/>: required, 1–100 chars of <c>[A-Za-z0-9:_.-]</c>. Use the
    /// deterministic key from CurrencyReasons when the source event has an identity; otherwise the
    /// client's Idempotency-Key header. Unique per <see cref="IdempotencyScope"/>.</item>
    /// <item><see cref="ReasonCode"/>: required, a CurrencyReasons code.</item>
    /// <item><see cref="Reason"/>: free text (≤ 500). Required for staff adjustments and reversals;
    /// otherwise defaults to the reason code's description.</item>
    /// <item>Initiator: <see cref="CurrencyInitiator.System"/> needs <see cref="InitiatorComponent"/>
    /// (e.g. "SalaryService"); Player/Admin need <see cref="InitiatorUserId"/> (from
    /// <c>HttpContext.GetKnkCaller().ActorUserId</c>, never a client-claimed id).</item>
    /// </list>
    /// </summary>
    public sealed record CurrencyContext
    {
        public required string IdempotencyKey { get; init; }

        public string IdempotencyScope { get; init; } = CurrencyIdempotencyScopes.System;

        public required string ReasonCode { get; init; }

        public string? Reason { get; init; }

        public CurrencyInitiator Initiator { get; init; } = CurrencyInitiator.System;

        public int? InitiatorUserId { get; init; }

        public string? InitiatorComponent { get; init; }

        public string? SourceType { get; init; }

        public string? SourceRef { get; init; }

        /// <summary>Optional JSON object/array; must parse.</summary>
        public string? MetadataJson { get; init; }

        /// <summary>Optional id grouping the transactions of one logical operation (≤ 64).</summary>
        public string? CorrelationId { get; init; }

        /// <summary>A server component posting on its own, with a deterministic key in the system scope.</summary>
        public static CurrencyContext ForSystem(string component, string reasonCode, string idempotencyKey, string? reason = null) => new()
        {
            IdempotencyKey = idempotencyKey,
            IdempotencyScope = CurrencyIdempotencyScopes.System,
            ReasonCode = reasonCode,
            Reason = reason,
            Initiator = CurrencyInitiator.System,
            InitiatorComponent = component
        };

        /// <summary>
        /// A posting requested over HTTP by <paramref name="caller"/> with the client's
        /// Idempotency-Key. The initiator is the verified actor (web user, or the plugin's acting
        /// staff member/player); the plugin without an acting user is PluginService. The scope is
        /// the caller type, so plugin and web keys can't collide. <paramref name="staffAction"/>
        /// says whether the actor acts on someone else's account (Admin) or their own (Player).
        /// </summary>
        public static CurrencyContext ForCaller(KnkCaller caller, string reasonCode, string idempotencyKey,
            string component, bool staffAction, string? reason = null)
        {
            if (!caller.IsPluginService && !caller.IsWebUser)
            {
                throw new CurrencyException(CurrencyErrorCode.InvalidRequest, "An anonymous caller can't post to the ledger.");
            }
            var actor = caller.ActorUserId;
            return new CurrencyContext
            {
                IdempotencyKey = idempotencyKey,
                IdempotencyScope = caller.IsWebUser ? CurrencyIdempotencyScopes.Web : CurrencyIdempotencyScopes.Plugin,
                ReasonCode = reasonCode,
                Reason = reason,
                Initiator = actor == null ? CurrencyInitiator.PluginService : staffAction ? CurrencyInitiator.Admin : CurrencyInitiator.Player,
                InitiatorUserId = actor,
                InitiatorComponent = component
            };
        }
    }

    /// <summary>One user's change in a multi-leg posting: signed, non-zero. The counter-entry goes
    /// to the reason's system account.</summary>
    public sealed record CurrencyLeg(int UserId, Currency Currency, long Amount);

    /// <summary>
    /// A staff adjustment (currency DESIGN.md §3.4 admin/adjustments, KNG-23 native set).
    /// <paramref name="Mode"/> Add/Remove take a positive <paramref name="Amount"/>; Set takes the
    /// target balance (0..cap) and the server computes the delta under the row lock.
    /// <paramref name="ExpectedCurrent"/>, when given, must equal the balance found under the lock
    /// (else ExpectedBalanceMismatch), so a set from a stale screen is refused, not applied.
    /// </summary>
    public sealed record AdminAdjustRequest(int UserId, Currency Currency, CurrencyOperation Mode, long Amount, long? ExpectedCurrent = null);

    /// <summary>
    /// Reversal options (DESIGN.md D11). A reversal never takes a balance below zero: with
    /// <paramref name="AllowPartial"/> false it is refused (ReversalWouldGoNegative); with true it
    /// reverses what is there and records the shortfall in the reversal's metadata.
    /// </summary>
    public sealed record ReversalOptions(bool AllowPartial = false);

    /// <summary>
    /// Ledger history query (per-player statement and the staff balance event log, KNG-23). All
    /// filters optional and combined with AND. Only user legs are returned.
    /// </summary>
    public sealed record LedgerQuery
    {
        public int? UserId { get; init; }
        public Currency? Currency { get; init; }
        public string? ReasonCode { get; init; }
        public CurrencyTransactionKind? Kind { get; init; }
        public CurrencyInitiator? Initiator { get; init; }
        public int? InitiatorUserId { get; init; }

        /// <summary>Case-insensitive substring of the initiator component or initiator's username.</summary>
        public string? InitiatorSearch { get; init; }

        /// <summary>Case-insensitive substring of the affected player's username.</summary>
        public string? UserSearch { get; init; }

        public string? SourceType { get; init; }
        public string? SourceRef { get; init; }
        public string? CorrelationId { get; init; }
        public string? TransactionPublicId { get; init; }

        /// <summary>Inclusive, UTC.</summary>
        public DateTime? From { get; init; }

        /// <summary>Exclusive, UTC.</summary>
        public DateTime? To { get; init; }

        public LedgerSort Sort { get; init; } = LedgerSort.CreatedAt;
        public bool Descending { get; init; } = true;
        public int Page { get; init; } = 1;

        /// <summary>1–200.</summary>
        public int PageSize { get; init; } = 50;
    }

    public enum LedgerSort
    {
        CreatedAt,
        Amount,
        UserId,
        Currency,
        ReasonCode,
        Initiator
    }
}

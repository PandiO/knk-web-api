using System;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Why a ledger posting was refused (docs/specs/currency-payments/DESIGN.md §3.3). Controllers
    /// map these to 409/422 with <c>{ code, message, details }</c> (currency Phase 3/4). Refused
    /// postings are never stored, so a retry is evaluated again.
    /// </summary>
    public enum CurrencyErrorCode
    {
        /// <summary>A debit would take a balance below zero.</summary>
        InsufficientFunds,

        /// <summary>A credit would take a balance above its cap (BalanceLimits).</summary>
        BalanceCapExceeded,

        /// <summary>Amount zero, negative where a positive one is needed, or above the cap.</summary>
        AmountOutOfRange,

        NotTransferable,
        SelfTransfer,
        RecipientNotFound,
        AccountLocked,
        CooldownActive,
        DailyCapExceeded,
        NewAccountRestricted,
        ConfirmationRequired,

        /// <summary>The idempotency key was already used for a different request.</summary>
        IdempotencyKeyReuse,

        /// <summary>The transaction has already been reversed.</summary>
        AlreadyReversed,

        /// <summary>A reversal would take a balance below zero and partial reversal wasn't allowed.</summary>
        ReversalWouldGoNegative,

        TransfersDisabled,

        /// <summary>A user named in the posting doesn't exist.</summary>
        UserNotFound,

        /// <summary>The transaction to reverse doesn't exist.</summary>
        TransactionNotFound,

        /// <summary>A Set's expectedCurrent didn't match the balance found under the lock (stale client view).</summary>
        ExpectedBalanceMismatch,

        /// <summary>This transaction can't be reversed (it is itself a reversal).</summary>
        NotReversible,

        /// <summary>The request itself is malformed: unknown or misused reason code, missing
        /// initiator or reason text, duplicate legs, invalid metadata JSON, bad key.</summary>
        InvalidRequest,

        /// <summary>No pending transfer with that id belongs to this sender (currency Phase 3).</summary>
        PendingTransferNotFound,

        /// <summary>The pending transfer's confirmation window has passed; send it again.</summary>
        PendingTransferExpired,

        /// <summary>The pending transfer was already cancelled (or confirmed, for a cancel).</summary>
        PendingTransferClosed,

        /// <summary>The recipient has received their rolling 24 h limit of this currency.</summary>
        RecipientDailyCapExceeded,

        /// <summary>A staff grant would pass the staff member's rolling 24 h grant cap
        /// (CurrencyPolicy.AdminDailyGrantCapPerActor; knk.admin.currency.unlimited bypasses it).</summary>
        AdminDailyCapExceeded,

        /// <summary>A policy edit was based on an older version of the row (its updatedAt no
        /// longer matches) - e.g. the R1 kill switch fired since the form was loaded. Details
        /// carry the current policy.</summary>
        PolicyChanged
    }

    /// <summary>
    /// A refused ledger posting. An InvalidOperationException (like BalanceCapExceededException)
    /// so existing "operation not possible" handling keeps working; the message starts with the
    /// code.
    /// </summary>
    public class CurrencyException : InvalidOperationException
    {
        public CurrencyException(CurrencyErrorCode code, string message, object? details = null)
            : base($"{code}: {message}")
        {
            Code = code;
            Details = details;
        }

        public CurrencyErrorCode Code { get; }

        /// <summary>Optional structured context for the response body (e.g. current balance).</summary>
        public object? Details { get; }
    }
}

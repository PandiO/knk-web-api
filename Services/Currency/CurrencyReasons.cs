using System.Collections.Generic;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Which way a reason's user legs may move a balance.
    /// </summary>
    public enum CurrencyReasonDirection
    {
        /// <summary>Only increases (a system account pays the user).</summary>
        Credit,

        /// <summary>Only decreases (the user pays a system account).</summary>
        Debit,

        /// <summary>Only a target value (ADMIN_SET).</summary>
        Set,

        /// <summary>Decided by the operation itself (reversal mirrors the original, transfer is user to user).</summary>
        Special,

        /// <summary>No user legs: moves funds from the reason's <see cref="CurrencyReasonInfo.FromSystemAccount"/>
        /// to its <see cref="CurrencyReasonInfo.SystemAccount"/> (ICurrencyService.MoveBetweenSystemAccountsAsync).</summary>
        SystemToSystem
    }

    /// <summary>One row of the reason table: the kind of transaction a reason produces and the
    /// system account on the other side of the double entry. A <see cref="CurrencyReasonDirection.SystemToSystem"/>
    /// reason also names the account the funds leave (<paramref name="FromSystemAccount"/>).</summary>
    public sealed record CurrencyReasonInfo(
        string Code,
        CurrencyTransactionKind Kind,
        CurrencyReasonDirection Direction,
        string? SystemAccount,
        string Description,
        string? FromSystemAccount = null);

    /// <summary>
    /// The ledger's reason codes (docs/specs/currency-payments/DESIGN.md §3.3) — the single table
    /// in code. Every posting carries one; it decides the transaction kind, the direction its
    /// user legs may take and the system account that balances them.
    /// <para>
    /// Sibling features add their row here, in the same PR as their first ICurrencyService call.
    /// Codes are stored in the ledger as strings, so a code must never be renamed or reused for
    /// something else once it has been posted. Codes are at most 40 characters.
    /// </para>
    /// Deterministic idempotency keys for system sources are listed next to each code; a
    /// one-time reward uses a key that can only ever be posted once (e.g. title-bonus).
    /// </summary>
    public static class CurrencyReasons
    {
        // ===== System accounts (max 30 characters) =====
        public const string SysSignup = "SYS_SIGNUP";
        public const string SysSalary = "SYS_SALARY";
        public const string SysSiege = "SYS_SIEGE";
        public const string SysTitle = "SYS_TITLE";
        public const string SysKits = "SYS_KITS";
        public const string SysLootbox = "SYS_LOOTBOX";
        public const string SysDiscovery = "SYS_DISCOVERY";
        public const string SysTeleport = "SYS_TELEPORT";
        public const string SysEvent = "SYS_EVENT";
        public const string SysStore = "SYS_STORE";
        public const string SysFees = "SYS_FEES";
        public const string SysAdmin = "SYS_ADMIN";
        public const string SysMerge = "SYS_MERGE";

        /// <summary>Holds both duel stakes from lock-in until the match is settled or refunded
        /// (arena duels DESIGN.md §7.1). Every closed match nets to zero here.</summary>
        public const string SysDuel = "SYS_DUEL";

        // ===== Reason codes =====

        /// <summary>Starting balance of a new account. Key: <c>signup:{userId}</c>.</summary>
        public const string SignupGrant = "SIGNUP_GRANT";

        /// <summary>Hourly title salary (SalaryService). Key: <c>salary:{userId}:{previousLastSalaryPayoutAt:O}</c>.</summary>
        public const string Salary = "SALARY";

        /// <summary>Siege match rewards, one multi-leg posting per match. Key: <c>siege-match:{matchId}</c>.</summary>
        public const string SiegeReward = "SIEGE_REWARD";

        /// <summary>One-time coin/gem/XP bonus of a title reached. Key: <c>title-bonus:{userId}:{bracketId}</c>, one posting with a leg per currency (once ever, DESIGN.md D10; one paid to an account merged into the user's counts as paid).</summary>
        public const string TitleBonus = "TITLE_BONUS";

        /// <summary>Cost of claiming a kit. Key: <c>kit-claim:{clientKey}</c>.</summary>
        public const string KitClaimCost = "KIT_CLAIM_COST";

        /// <summary>Buying a premium kit. Key: <c>kit-purchase:{kitId}:{userId}</c>.</summary>
        public const string KitPurchase = "KIT_PURCHASE";

        /// <summary>Buying/opening a lootbox. Key: <c>lootbox-open:{openingId}</c>.</summary>
        public const string LootboxPurchase = "LOOTBOX_PURCHASE";

        /// <summary>Currency won from a lootbox. Key: <c>lootbox-open:{openingId}:reward</c>.</summary>
        public const string LootboxReward = "LOOTBOX_REWARD";

        /// <summary>First discovery of a domain. Key: <c>discovery:{userId}:{domainId}</c>.</summary>
        public const string DiscoveryReward = "DISCOVERY_REWARD";

        /// <summary>Paid teleport. Key: the client's Idempotency-Key.</summary>
        public const string TeleportFee = "TELEPORT_FEE";

        /// <summary>Event prize. Key: <c>event:{eventId}:{userId}</c>.</summary>
        public const string EventReward = "EVENT_REWARD";

        /// <summary>Real-money gem top-up (future). Key: the payment provider's id.</summary>
        public const string PremiumTopup = "PREMIUM_TOPUP";

        /// <summary>Player to player (/pay, currency Phase 3 — not postable before then).</summary>
        public const string PlayerTransfer = "PLAYER_TRANSFER";

        /// <summary>Fee on a player transfer. Key: <c>{transferKey}:fee</c>.</summary>
        public const string TransferFee = "TRANSFER_FEE";

        /// <summary>Staff add to a balance. Key: the client's Idempotency-Key.</summary>
        public const string AdminGrant = "ADMIN_GRANT";

        /// <summary>Staff remove from a balance. Key: the client's Idempotency-Key.</summary>
        public const string AdminTake = "ADMIN_TAKE";

        /// <summary>Staff set a balance to a target value, delta computed server-side under the lock.</summary>
        public const string AdminSet = "ADMIN_SET";

        /// <summary>Undo of an earlier transaction. Key: <c>reverse:{transactionId}</c>.</summary>
        public const string Reversal = "REVERSAL";

        /// <summary>Secondary account's full balances (coins, gems, XP) zeroed on account merge. Key: <c>merge:{secondaryUserId}</c>.</summary>
        public const string MergeForfeit = "MERGE_FORFEIT";

        /// <summary>
        /// On account merge the survivor keeps the higher balance of each currency (KNG-21): it is
        /// credited secondary − primary where positive, in the same transaction as the
        /// MERGE_FORFEIT. Key: <c>merge-carry:{secondaryUserId}</c>.
        /// </summary>
        public const string MergeCarryover = "MERGE_CARRYOVER";

        // ----- Arena duels (DESIGN.md §7.1, KNG-213). Every leg carries SourceType "DuelMatch",
        // SourceRef = the match id; Σ SYS_DUEL over a closed match's postings is 0. -----

        /// <summary>Both duelists' coin stakes into SYS_DUEL at lock-in, one posting with two legs.
        /// Key: <c>duel-lock:{publicToken}</c>.</summary>
        public const string DuelStake = "DUEL_STAKE";

        /// <summary>The pot minus the fee from SYS_DUEL to the winner of a decisive result (also on a
        /// staff release of a held result). Funded by the stakes, so not minted. Key: <c>duel-payout:{matchId}</c>.</summary>
        public const string DuelPayout = "DUEL_PAYOUT";

        /// <summary>The arena fee (FeePercent of the pot, rounded down) from SYS_DUEL to SYS_FEES, in the
        /// same database transaction as the payout; no user leg. Key: <c>duel-fee:{matchId}</c>.</summary>
        public const string DuelFee = "DUEL_FEE";

        /// <summary>A staff revert of a paid result: what the payout reversal recovered (plus the reversed
        /// fee) back from SYS_DUEL to the loser first, up to their stake, then to the winner up to theirs.
        /// Key: <c>duel-revert:{matchId}</c>.</summary>
        public const string DuelRefund = "DUEL_REFUND";

        /// <summary>A staff confiscation of a paid result: what the payout reversal recovered, from
        /// SYS_DUEL to the SYS_FEES sink; no user leg. Key: <c>duel-confiscate:{matchId}</c>.</summary>
        public const string DuelConfiscation = "DUEL_CONFISCATION";

        public static string MergeForfeitKey(int secondaryUserId) => $"merge:{secondaryUserId}";

        public static string MergeCarryoverKey(int secondaryUserId) => $"merge-carry:{secondaryUserId}";

        private static readonly Dictionary<string, CurrencyReasonInfo> Table = new()
        {
            [SignupGrant] = new(SignupGrant, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysSignup, "Starting balance"),
            [Salary] = new(Salary, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysSalary, "Salary payout"),
            [SiegeReward] = new(SiegeReward, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysSiege, "Siege match reward"),
            [TitleBonus] = new(TitleBonus, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysTitle, "Title promotion bonus"),
            [KitClaimCost] = new(KitClaimCost, CurrencyTransactionKind.Spend, CurrencyReasonDirection.Debit, SysKits, "Kit claim cost"),
            [KitPurchase] = new(KitPurchase, CurrencyTransactionKind.Spend, CurrencyReasonDirection.Debit, SysKits, "Kit purchase"),
            [LootboxPurchase] = new(LootboxPurchase, CurrencyTransactionKind.Spend, CurrencyReasonDirection.Debit, SysLootbox, "Lootbox purchase"),
            [LootboxReward] = new(LootboxReward, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysLootbox, "Lootbox reward"),
            [DiscoveryReward] = new(DiscoveryReward, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysDiscovery, "Domain discovery reward"),
            [TeleportFee] = new(TeleportFee, CurrencyTransactionKind.Spend, CurrencyReasonDirection.Debit, SysTeleport, "Teleport fee"),
            [EventReward] = new(EventReward, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysEvent, "Event reward"),
            [PremiumTopup] = new(PremiumTopup, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysStore, "Premium top-up"),
            [PlayerTransfer] = new(PlayerTransfer, CurrencyTransactionKind.Transfer, CurrencyReasonDirection.Special, null, "Player transfer"),
            [TransferFee] = new(TransferFee, CurrencyTransactionKind.Spend, CurrencyReasonDirection.Debit, SysFees, "Transfer fee"),
            [AdminGrant] = new(AdminGrant, CurrencyTransactionKind.AdminAdjust, CurrencyReasonDirection.Credit, SysAdmin, "Staff grant"),
            [AdminTake] = new(AdminTake, CurrencyTransactionKind.AdminAdjust, CurrencyReasonDirection.Debit, SysAdmin, "Staff removal"),
            [AdminSet] = new(AdminSet, CurrencyTransactionKind.AdminAdjust, CurrencyReasonDirection.Set, SysAdmin, "Staff set balance"),
            [Reversal] = new(Reversal, CurrencyTransactionKind.Reversal, CurrencyReasonDirection.Special, null, "Reversal"),
            [MergeForfeit] = new(MergeForfeit, CurrencyTransactionKind.Merge, CurrencyReasonDirection.Debit, SysMerge, "Balance forfeited on account merge"),
            [MergeCarryover] = new(MergeCarryover, CurrencyTransactionKind.Merge, CurrencyReasonDirection.Credit, SysMerge, "Higher balance kept on account merge"),
            [DuelStake] = new(DuelStake, CurrencyTransactionKind.Spend, CurrencyReasonDirection.Debit, SysDuel, "Duel stake"),
            [DuelPayout] = new(DuelPayout, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysDuel, "Duel winnings"),
            [DuelFee] = new(DuelFee, CurrencyTransactionKind.Spend, CurrencyReasonDirection.SystemToSystem, SysFees, "Arena fee", SysDuel),
            [DuelRefund] = new(DuelRefund, CurrencyTransactionKind.Grant, CurrencyReasonDirection.Credit, SysDuel, "Duel stake returned after a staff revert"),
            [DuelConfiscation] = new(DuelConfiscation, CurrencyTransactionKind.Spend, CurrencyReasonDirection.SystemToSystem, SysFees, "Duel winnings confiscated by staff", SysDuel),
        };

        /// <summary>Every known reason, for docs, dropdowns and tests.</summary>
        public static IReadOnlyCollection<CurrencyReasonInfo> All => Table.Values;

        /// <summary>The reason's row, or null for an unknown code.</summary>
        public static CurrencyReasonInfo? Find(string? code) =>
            code != null && Table.TryGetValue(code, out var info) ? info : null;

        /// <summary>The admin reason code for an adjustment mode.</summary>
        public static string ForAdminMode(CurrencyOperation mode) => mode switch
        {
            CurrencyOperation.Add => AdminGrant,
            CurrencyOperation.Remove => AdminTake,
            _ => AdminSet
        };
    }
}

using System;
using System.Collections.Generic;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Header of one ledger posting (docs/specs/currency-payments/DESIGN.md §3.2, KNG-23 folded in).
/// Its entries (<see cref="CurrencyEntry"/>) sum to zero per currency: what a user gains a
/// system account loses, and the other way round.
/// <para>
/// Append-only and kept forever: written only by CurrencyService through CurrencyRepository,
/// which has no update or delete methods; MySQL BEFORE UPDATE/DELETE triggers refuse changes
/// (migration AddCurrencyLedgerImmutabilityTriggers); RetentionPolicyService never purges it.
/// Corrections are new transactions (a reversal or an admin adjustment).
/// </para>
/// Not [FormConfigurableEntity]: never edited through the generic FormWizard CRUD.
/// </summary>
public class CurrencyTransaction
{
    public long Id { get; set; }

    /// <summary>ULID shown to players and staff ("TX 01J…"); unique.</summary>
    public string PublicId { get; set; } = null!;

    public CurrencyTransactionKind Kind { get; set; }

    /// <summary>Mandatory reason code from CurrencyReasons (e.g. SALARY, ADMIN_SET).</summary>
    public string ReasonCode { get; set; } = null!;

    /// <summary>Free-text reason: the staff member's explanation, or the reason code's default
    /// description for system postings. Never empty.</summary>
    public string Reason { get; set; } = null!;

    /// <summary>What caused it, e.g. "SiegeMatch", "Kit", "Lootbox". Optional.</summary>
    public string? SourceType { get; set; }

    /// <summary>The source's id, e.g. "123". Optional.</summary>
    public string? SourceRef { get; set; }

    /// <summary>"plugin" | "web" | "system"; (IdempotencyScope, IdempotencyKey) is unique.</summary>
    public string IdempotencyScope { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;

    /// <summary>SHA-256 (hex) of the request's financial content. The same key with a
    /// different hash is refused (IdempotencyKeyReuse) instead of replayed.</summary>
    public string RequestHash { get; set; } = null!;

    public CurrencyInitiator Initiator { get; set; }

    /// <summary>The person who started it: a verified web user or the plugin's acting staff
    /// member (HttpContext.GetKnkCaller().ActorUserId), never a client-claimed id. No FK to
    /// users, same reason as AuditLogEntry: the ledger must outlive a deleted account.</summary>
    public int? InitiatorUserId { get; set; }

    /// <summary>The server component that started it, e.g. "SalaryService", "TitlePromotion",
    /// "KitClaim", "DomainDiscovery", "PluginStaffCommand", "WebAppProfile".</summary>
    public string? InitiatorComponent { get; set; }

    /// <summary>Sender / recipient of a player transfer (currency Phase 3); null otherwise.</summary>
    public int? FromUserId { get; set; }

    public int? ToUserId { get; set; }

    /// <summary>Optional JSON with extra context (multipliers, crossed titles, reversal shortfall…).</summary>
    public string? MetadataJson { get; set; }

    /// <summary>Groups the transactions of one logical operation, e.g. an XP grant and the
    /// title bonuses it triggers. Optional.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>The transaction this one reverses; unique, so a transaction is reversed at most once.</summary>
    public long? ReversesTransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CurrencyEntry> Entries { get; set; } = new List<CurrencyEntry>();
}

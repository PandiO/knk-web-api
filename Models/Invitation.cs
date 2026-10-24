using System;
using System.Collections.Generic;

namespace knkwebapi_v2.Models;

/// <summary>Personal = one named tester, email-bound and single-use; Shared = a capped code for a group
/// (docs/specs/invitations/DESIGN.md § 3.1, KNG-212).</summary>
public enum InvitationKind
{
    Personal = 0,
    Shared = 1
}

/// <summary>Derived, never stored: Revoked wins over Exhausted, Exhausted over Expired, Expired over Paused.</summary>
public enum InvitationStatus
{
    Active,
    Paused,
    Exhausted,
    Expired,
    Revoked
}

/// <summary>
/// An invitation to create a web account during the closed alpha (KNG-212, DESIGN.md § 3). Only the
/// SHA-256 of the code is stored (<see cref="CodeHash"/>); the raw link or code is shown once, at
/// creation, resend or rotate. A use is taken in one conditional UPDATE at account creation
/// (InvitationRepository.TryTakeUseAsync), so two sign-ups can't share the last use.
/// </summary>
public class Invitation
{
    public int Id { get; set; }

    public InvitationKind Kind { get; set; }

    /// <summary>Lower-case hex SHA-256 of the normalised code (InvitationCodes.Hash).</summary>
    public string CodeHash { get; set; } = null!;

    /// <summary>The last 4 characters of the code, so staff can tell codes apart in the list.</summary>
    public string CodeHint { get; set; } = null!;

    /// <summary>Personal invites only: the bound address, lower case.</summary>
    public string? Email { get; set; }

    /// <summary>Shared codes: required, so a leaked code can be traced ("Discord #announcements 21 Oct").</summary>
    public string? Label { get; set; }

    /// <summary>Personal invites: an optional note for staff (not mailed).</summary>
    public string? Note { get; set; }

    /// <summary>A group from Invitations:InvitableGroups the account joins on redemption.</summary>
    public int? GrantGroupId { get; set; }
    public PermissionGroup? GrantGroup { get; set; }

    public int MaxUses { get; set; }
    public int UseCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public int? RevokedByUserId { get; set; }

    /// <summary>Mails queued for this invite; part of the outbox dedupe key invite:{id}:{SendCount}.</summary>
    public int SendCount { get; set; }
    public DateTime? LastSentAt { get; set; }

    /// <summary>Start of the rolling 24-hour window for Invitations:ResendPerDay, and the resends in it.</summary>
    public DateTime? ResendWindowStartedAt { get; set; }
    public int ResendsInWindow { get; set; }

    public ICollection<InvitationRedemption> Redemptions { get; set; } = new List<InvitationRedemption>();

    public InvitationStatus StatusAt(DateTime now) =>
        RevokedAt.HasValue ? InvitationStatus.Revoked
        : UseCount >= MaxUses ? InvitationStatus.Exhausted
        : ExpiresAt <= now ? InvitationStatus.Expired
        : PausedAt.HasValue ? InvitationStatus.Paused
        : InvitationStatus.Active;
}

/// <summary>One account created with an invitation (DESIGN.md § 3.4): who brought whom.</summary>
public class InvitationRedemption
{
    public int Id { get; set; }
    public int InvitationId { get; set; }
    public Invitation Invitation { get; set; } = null!;
    public int UserId { get; set; }
    public DateTime RedeemedAt { get; set; }
}

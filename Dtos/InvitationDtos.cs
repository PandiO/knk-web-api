using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // api/Invitations (KNG-212, knk-workspace docs/specs/invitations/DESIGN.md § 3, § 6). Raw links and
    // codes appear only in the create, resend and rotate responses, once.

    public class CreatePersonalInvitationsRequestDto
    {
        /// <summary>One or more addresses; the web app splits its textarea by line.</summary>
        [JsonPropertyName("emails")]
        public List<string> Emails { get; set; } = new();

        [JsonPropertyName("expiryDays")]
        public int? ExpiryDays { get; set; }

        [JsonPropertyName("groupId")]
        public int? GroupId { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        /// <summary>Default on; off = "just give me the links".</summary>
        [JsonPropertyName("sendEmail")]
        public bool SendEmail { get; set; } = true;
    }

    public class CreateSharedInvitationRequestDto
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("maxUses")]
        public int? MaxUses { get; set; }

        [JsonPropertyName("expiryDays")]
        public int? ExpiryDays { get; set; }

        [JsonPropertyName("groupId")]
        public int? GroupId { get; set; }
    }

    public class ResendInvitationRequestDto
    {
        [JsonPropertyName("sendEmail")]
        public bool SendEmail { get; set; } = true;
    }

    public class InvitationDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>Personal or Shared.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = null!;

        /// <summary>Active, Paused, Exhausted, Expired or Revoked.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = null!;

        [JsonPropertyName("codeHint")]
        public string CodeHint { get; set; } = null!;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        [JsonPropertyName("groupId")]
        public int? GroupId { get; set; }

        [JsonPropertyName("groupName")]
        public string? GroupName { get; set; }

        [JsonPropertyName("maxUses")]
        public int MaxUses { get; set; }

        [JsonPropertyName("useCount")]
        public int UseCount { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("createdByUserId")]
        public int CreatedByUserId { get; set; }

        [JsonPropertyName("createdByName")]
        public string? CreatedByName { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("pausedAt")]
        public DateTime? PausedAt { get; set; }

        [JsonPropertyName("revokedAt")]
        public DateTime? RevokedAt { get; set; }

        [JsonPropertyName("sendCount")]
        public int SendCount { get; set; }

        [JsonPropertyName("lastSentAt")]
        public DateTime? LastSentAt { get; set; }
    }

    public class InvitationPageDto
    {
        [JsonPropertyName("items")]
        public List<InvitationDto> Items { get; set; } = new();

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }
    }

    /// <summary>One address of a personal create (or a resend): the link once, and the mail's status.</summary>
    public class PersonalInvitationResultDto
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = null!;

        /// <summary>Created, or Skipped with <see cref="Reason"/>.</summary>
        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = null!;

        /// <summary>InvalidEmail, EmailInUse or Duplicate (the address appeared twice in the request).</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("invitation")]
        public InvitationDto? Invitation { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        /// <summary>The outbox row's status (Pending, Suppressed, ...) or null when no mail was queued.</summary>
        [JsonPropertyName("mailStatus")]
        public string? MailStatus { get; set; }

        /// <summary>An older active invite for the same address that this one replaced (revoked).</summary>
        [JsonPropertyName("replacedInvitationId")]
        public int? ReplacedInvitationId { get; set; }
    }

    public class CreatePersonalInvitationsResponseDto
    {
        [JsonPropertyName("results")]
        public List<PersonalInvitationResultDto> Results { get; set; } = new();
    }

    /// <summary>A shared code after create or rotate: the code and link once.</summary>
    public class SharedInvitationResultDto
    {
        [JsonPropertyName("invitation")]
        public InvitationDto Invitation { get; set; } = null!;

        [JsonPropertyName("code")]
        public string Code { get; set; } = null!;

        [JsonPropertyName("link")]
        public string Link { get; set; } = null!;
    }

    public class InvitationRedemptionDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        /// <summary>The Minecraft name once linked; the internal web-… name before.</summary>
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("emailMasked")]
        public string? EmailMasked { get; set; }

        [JsonPropertyName("redeemedAt")]
        public DateTime RedeemedAt { get; set; }

        [JsonPropertyName("minecraftLinked")]
        public bool MinecraftLinked { get; set; }
    }

    public class InvitableGroupDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
    }

    /// <summary>Defaults and caps for the create forms, so the web app doesn't hard-code them.</summary>
    public class InvitationOptionsDto
    {
        [JsonPropertyName("groups")]
        public List<InvitableGroupDto> Groups { get; set; } = new();

        [JsonPropertyName("defaultExpiryDays")]
        public int DefaultExpiryDays { get; set; }

        [JsonPropertyName("maxExpiryDays")]
        public int MaxExpiryDays { get; set; }

        [JsonPropertyName("sharedDefaultMaxUses")]
        public int SharedDefaultMaxUses { get; set; }

        [JsonPropertyName("sharedMaxUsesCap")]
        public int SharedMaxUsesCap { get; set; }

        [JsonPropertyName("registrationMode")]
        public string RegistrationMode { get; set; } = null!;
    }

    /// <summary>POST api/Invitations/check: a personal token or a shared code.</summary>
    public class CheckInvitationRequestDto
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }

    public class CheckInvitationResponseDto
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        /// <summary>InvalidInvite for every failure, so a revoked code looks like a wrong one.</summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        /// <summary>Personal invites: the bound address, so the sign-up page can show it locked. Only the
        /// holder of the 256-bit token (sent to that address) gets it.</summary>
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("emailHint")]
        public string? EmailHint { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
    }
}

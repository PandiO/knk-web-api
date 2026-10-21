using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Email;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Invitations
{
    /// <summary>The result of a staff action: a value, or an error code the controller maps to a status.</summary>
    public sealed record InvitationOutcome<T>(T? Value, string? Error = null, string? Message = null)
    {
        public bool Ok => Error == null;
        public static InvitationOutcome<T> Fail(string error, string message) => new(default, error, message);
    }

    /// <summary>Error codes of <see cref="InvitationOutcome{T}"/>.</summary>
    public static class InvitationErrors
    {
        public const string NotFound = "NotFound";
        public const string Invalid = "InvalidRequest";
        public const string GroupNotInvitable = "GroupNotInvitable";
        public const string Escalation = "Forbidden";
        public const string WrongKind = "WrongKind";
        public const string NotActive = "NotActive";
        public const string ResendLimit = "ResendLimit";
        public const string InvalidInvite = "InvalidInvite";
    }

    /// <summary>An account created from an invitation (AuthService.RegisterAsync, Mode=Invite).</summary>
    public sealed record InviteSignup(Invitation Invitation, string Email, string PasswordHash, bool EmailVerified);

    public interface IInvitationService
    {
        Task<InvitationOutcome<CreatePersonalInvitationsResponseDto>> CreatePersonalAsync(int actorId, CreatePersonalInvitationsRequestDto request);
        Task<InvitationOutcome<SharedInvitationResultDto>> CreateSharedAsync(int actorId, CreateSharedInvitationRequestDto request);
        Task<InvitationOutcome<PersonalInvitationResultDto>> ResendAsync(int actorId, int invitationId, bool sendEmail);
        Task<InvitationOutcome<SharedInvitationResultDto>> RotateAsync(int actorId, int invitationId);
        Task<InvitationOutcome<InvitationDto>> PauseAsync(int actorId, int invitationId);
        Task<InvitationOutcome<InvitationDto>> ResumeAsync(int actorId, int invitationId);
        Task<InvitationOutcome<InvitationDto>> RevokeAsync(int actorId, int invitationId);
        Task<InvitationPageDto> ListAsync(int pageNumber, int pageSize, string? status, string? kind);
        Task<InvitationOutcome<List<InvitationRedemptionDto>>> GetRedemptionsAsync(int invitationId);
        Task<InvitationOptionsDto> GetOptionsAsync();

        /// <summary>Anonymous check from the sign-up pages. Every failure is the same InvalidInvite.</summary>
        Task<CheckInvitationResponseDto> CheckAsync(string? token, string? code);

        /// <summary>The invitation a sign-up names, if it can be used right now; null otherwise.</summary>
        Task<Invitation?> FindUsableAsync(string? token, string? code);

        /// <summary>
        /// Creates the web shell account for <paramref name="signup"/> in one transaction: takes the use
        /// (null when that fails: another sign-up took the last use, or the invite changed), creates the
        /// user with the Default group and the invite's group, admitted now, writes the redemption and the
        /// audit entry and queues the Welcome mail. No SIGNUP_GRANT: that is posted when Minecraft is
        /// linked (KNG-214).
        /// </summary>
        Task<User?> RedeemAsync(InviteSignup signup);
    }

    public class InvitationService : IInvitationService
    {
        public const string DefaultGroupName = UserService.DefaultGroupName;

        private readonly IInvitationRepository _repo;
        private readonly IUserRepository _users;
        private readonly IPermissionGroupRepository _groups;
        private readonly IPermissionEscalationGuard _guard;
        private readonly IAuditLogService _audit;
        private readonly IEmailComposer _email;
        private readonly InvitationSettings _settings;
        private readonly SecuritySettings _security;
        private readonly string _baseUrl;
        private readonly TimeProvider _time;
        private readonly ILogger _logger;

        public InvitationService(
            IInvitationRepository repo,
            IUserRepository users,
            IPermissionGroupRepository groups,
            IPermissionEscalationGuard guard,
            IAuditLogService audit,
            IEmailComposer email,
            IOptions<InvitationSettings> settings,
            IOptions<SecuritySettings> security,
            IOptions<AppSettings>? app = null,
            TimeProvider? time = null,
            ILogger<InvitationService>? logger = null)
        {
            _repo = repo;
            _users = users;
            _groups = groups;
            _guard = guard;
            _audit = audit;
            _email = email;
            _settings = settings.Value;
            _security = security.Value;
            _baseUrl = AppSettings.ResolveBaseUrl(app?.Value.PublicBaseUrl, _security.PasswordResetFrontendBaseUrl);
            _time = time ?? TimeProvider.System;
            _logger = logger ?? (ILogger)NullLogger.Instance;
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        public string PersonalLink(string token) => $"{_baseUrl}/invite/{token}";

        public string SharedLink(string displayCode) => $"{_baseUrl}/join?code={displayCode}";

        // ---------------------------------------------------------------- create

        public async Task<InvitationOutcome<CreatePersonalInvitationsResponseDto>> CreatePersonalAsync(int actorId, CreatePersonalInvitationsRequestDto request)
        {
            var emails = (request?.Emails ?? new List<string>())
                .SelectMany(e => (e ?? string.Empty).Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(e => e.Trim())
                .Where(e => e.Length > 0)
                .ToList();
            if (emails.Count == 0)
            {
                return InvitationOutcome<CreatePersonalInvitationsResponseDto>.Fail(InvitationErrors.Invalid, "Enter at least one email address.");
            }
            if (emails.Count > _settings.MaxEmailsPerRequest)
            {
                return InvitationOutcome<CreatePersonalInvitationsResponseDto>.Fail(InvitationErrors.Invalid,
                    $"At most {_settings.MaxEmailsPerRequest} addresses at once.");
            }
            var note = Trim(request!.Note, 500);
            var groupCheck = await CheckGroupAsync(actorId, request.GroupId);
            if (groupCheck != null)
            {
                return InvitationOutcome<CreatePersonalInvitationsResponseDto>.Fail(groupCheck.Value.Error, groupCheck.Value.Message);
            }

            var expiryDays = ClampExpiry(request.ExpiryDays);
            var inviterName = await InviterNameAsync(actorId);
            var response = new CreatePersonalInvitationsResponseDto();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var raw in emails)
            {
                var email = raw.ToLowerInvariant();
                var result = new PersonalInvitationResultDto { Email = email, Outcome = "Skipped" };
                response.Results.Add(result);
                if (!IsValidEmail(email))
                {
                    result.Reason = "InvalidEmail";
                    continue;
                }
                if (!seen.Add(email))
                {
                    result.Reason = "Duplicate";
                    continue;
                }
                if (await _users.IsEmailTakenAsync(email))
                {
                    result.Reason = "EmailInUse";
                    continue;
                }

                // One transaction per address: the replaced invite, the new one and its mail go together.
                var created = await _repo.RunInTransactionAsync(async () =>
                {
                    var now = Now;
                    int? replaced = null;
                    foreach (var old in await _repo.GetActivePersonalForEmailAsync(email, now))
                    {
                        old.RevokedAt = now;
                        old.RevokedByUserId = actorId;
                        replaced = old.Id;
                    }
                    var token = InvitationCodes.NewPersonalToken();
                    var invitation = new Invitation
                    {
                        Kind = InvitationKind.Personal,
                        CodeHash = InvitationCodes.Hash(token),
                        CodeHint = InvitationCodes.Hint(token),
                        Email = email,
                        Note = note,
                        GrantGroupId = request.GroupId,
                        MaxUses = 1,
                        CreatedAt = now,
                        CreatedByUserId = actorId,
                        ExpiresAt = now.AddDays(expiryDays)
                    };
                    await _repo.AddAsync(invitation);
                    var mailStatus = request.SendEmail ? await QueueInvitationMailAsync(invitation, token, inviterName) : null;
                    await _audit.RecordAsync(actorId, actorId, AuditAction.InvitationCreated, Details(invitation, replaced));
                    return (invitation, token, mailStatus, replaced);
                });

                result.Outcome = "Created";
                result.Invitation = await ToDtoAsync(created.invitation);
                result.Link = PersonalLink(created.token);
                result.MailStatus = created.mailStatus;
                result.ReplacedInvitationId = created.replaced;
            }

            _logger.LogInformation("Staff {ActorId} created {Count} personal invitation(s)", actorId,
                response.Results.Count(r => r.Outcome == "Created"));
            return new InvitationOutcome<CreatePersonalInvitationsResponseDto>(response);
        }

        public async Task<InvitationOutcome<SharedInvitationResultDto>> CreateSharedAsync(int actorId, CreateSharedInvitationRequestDto request)
        {
            var label = Trim(request?.Label, 120);
            if (string.IsNullOrEmpty(label))
            {
                return InvitationOutcome<SharedInvitationResultDto>.Fail(InvitationErrors.Invalid,
                    "A label is required, so a leaked code can be traced (for example \"Discord #announcements 21 Oct\").");
            }
            var groupCheck = await CheckGroupAsync(actorId, request!.GroupId);
            if (groupCheck != null)
            {
                return InvitationOutcome<SharedInvitationResultDto>.Fail(groupCheck.Value.Error, groupCheck.Value.Message);
            }

            var maxUses = Math.Clamp(request.MaxUses ?? _settings.SharedDefaultMaxUses, 1, Math.Max(1, _settings.SharedMaxUsesCap));
            var now = Now;
            var code = InvitationCodes.NewSharedCode();
            var normalized = InvitationCodes.NormalizeShared(code)!;
            var invitation = new Invitation
            {
                Kind = InvitationKind.Shared,
                CodeHash = InvitationCodes.Hash(normalized),
                CodeHint = InvitationCodes.Hint(normalized),
                Label = label,
                GrantGroupId = request.GroupId,
                MaxUses = maxUses,
                CreatedAt = now,
                CreatedByUserId = actorId,
                ExpiresAt = now.AddDays(ClampExpiry(request.ExpiryDays))
            };
            await _repo.RunInTransactionAsync(async () =>
            {
                await _repo.AddAsync(invitation);
                await _audit.RecordAsync(actorId, actorId, AuditAction.InvitationCreated, Details(invitation, null));
                return true;
            });
            _logger.LogInformation("Staff {ActorId} created shared invitation {InvitationId} ({MaxUses} uses)", actorId, invitation.Id, maxUses);
            return new InvitationOutcome<SharedInvitationResultDto>(new SharedInvitationResultDto
            {
                Invitation = await ToDtoAsync(invitation),
                Code = code,
                Link = SharedLink(code)
            });
        }

        // ---------------------------------------------------------------- lifecycle

        public async Task<InvitationOutcome<PersonalInvitationResultDto>> ResendAsync(int actorId, int invitationId, bool sendEmail)
        {
            var invitation = await _repo.GetByIdAsync(invitationId);
            if (invitation == null)
            {
                return InvitationOutcome<PersonalInvitationResultDto>.Fail(InvitationErrors.NotFound, "Invitation not found.");
            }
            if (invitation.Kind != InvitationKind.Personal)
            {
                return InvitationOutcome<PersonalInvitationResultDto>.Fail(InvitationErrors.WrongKind, "Only personal invitations can be resent; rotate a shared code instead.");
            }
            // Expired invites may be resent (that restarts them); used or revoked ones may not.
            if (invitation.RevokedAt.HasValue || invitation.UseCount >= invitation.MaxUses)
            {
                return InvitationOutcome<PersonalInvitationResultDto>.Fail(InvitationErrors.NotActive, "This invitation was already used or revoked.");
            }
            if (await _users.IsEmailTakenAsync(invitation.Email!))
            {
                return InvitationOutcome<PersonalInvitationResultDto>.Fail(InvitationErrors.NotActive, "An account already uses this address.");
            }

            var now = Now;
            if (invitation.ResendWindowStartedAt == null || now - invitation.ResendWindowStartedAt.Value >= TimeSpan.FromDays(1))
            {
                invitation.ResendWindowStartedAt = now;
                invitation.ResendsInWindow = 0;
            }
            if (invitation.ResendsInWindow >= _settings.ResendPerDay)
            {
                return InvitationOutcome<PersonalInvitationResultDto>.Fail(InvitationErrors.ResendLimit,
                    $"This invitation was resent {_settings.ResendPerDay} times in the last 24 hours. Try again later.");
            }

            var inviterName = await InviterNameAsync(invitation.CreatedByUserId);
            var (token, mailStatus) = await _repo.RunInTransactionAsync(async () =>
            {
                // The old link dies with the rotation, so a stale mail can't be used (DESIGN.md § 3.2).
                var newToken = InvitationCodes.NewPersonalToken();
                invitation.CodeHash = InvitationCodes.Hash(newToken);
                invitation.CodeHint = InvitationCodes.Hint(newToken);
                invitation.ExpiresAt = now.AddDays(ClampExpiry(null));
                invitation.ResendsInWindow++;
                await _repo.SaveAsync();
                var status = sendEmail ? await QueueInvitationMailAsync(invitation, newToken, inviterName) : null;
                return (newToken, status);
            });

            _logger.LogInformation("Staff {ActorId} resent personal invitation {InvitationId}", actorId, invitation.Id);
            return new InvitationOutcome<PersonalInvitationResultDto>(new PersonalInvitationResultDto
            {
                Email = invitation.Email!,
                Outcome = "Created",
                Invitation = await ToDtoAsync(invitation),
                Link = PersonalLink(token),
                MailStatus = mailStatus
            });
        }

        public async Task<InvitationOutcome<SharedInvitationResultDto>> RotateAsync(int actorId, int invitationId)
        {
            var invitation = await _repo.GetByIdAsync(invitationId);
            if (invitation == null)
            {
                return InvitationOutcome<SharedInvitationResultDto>.Fail(InvitationErrors.NotFound, "Invitation not found.");
            }
            if (invitation.Kind != InvitationKind.Shared)
            {
                return InvitationOutcome<SharedInvitationResultDto>.Fail(InvitationErrors.WrongKind, "Only shared codes can be rotated; resend a personal invitation instead.");
            }
            if (invitation.RevokedAt.HasValue)
            {
                return InvitationOutcome<SharedInvitationResultDto>.Fail(InvitationErrors.NotActive, "This code was revoked.");
            }
            var code = InvitationCodes.NewSharedCode();
            var normalized = InvitationCodes.NormalizeShared(code)!;
            invitation.CodeHash = InvitationCodes.Hash(normalized);
            invitation.CodeHint = InvitationCodes.Hint(normalized);
            await _repo.SaveAsync();
            _logger.LogInformation("Staff {ActorId} rotated shared invitation {InvitationId}", actorId, invitation.Id);
            return new InvitationOutcome<SharedInvitationResultDto>(new SharedInvitationResultDto
            {
                Invitation = await ToDtoAsync(invitation),
                Code = code,
                Link = SharedLink(code)
            });
        }

        public Task<InvitationOutcome<InvitationDto>> PauseAsync(int actorId, int invitationId) =>
            ChangeSharedPauseAsync(actorId, invitationId, pause: true);

        public Task<InvitationOutcome<InvitationDto>> ResumeAsync(int actorId, int invitationId) =>
            ChangeSharedPauseAsync(actorId, invitationId, pause: false);

        private async Task<InvitationOutcome<InvitationDto>> ChangeSharedPauseAsync(int actorId, int invitationId, bool pause)
        {
            var invitation = await _repo.GetByIdAsync(invitationId);
            if (invitation == null)
            {
                return InvitationOutcome<InvitationDto>.Fail(InvitationErrors.NotFound, "Invitation not found.");
            }
            if (invitation.Kind != InvitationKind.Shared)
            {
                return InvitationOutcome<InvitationDto>.Fail(InvitationErrors.WrongKind, "Only shared codes can be paused; revoke a personal invitation instead.");
            }
            if (invitation.RevokedAt.HasValue)
            {
                return InvitationOutcome<InvitationDto>.Fail(InvitationErrors.NotActive, "This code was revoked.");
            }
            invitation.PausedAt = pause ? invitation.PausedAt ?? Now : null;
            await _repo.SaveAsync();
            _logger.LogInformation("Staff {ActorId} {Action} shared invitation {InvitationId}", actorId, pause ? "paused" : "resumed", invitation.Id);
            return new InvitationOutcome<InvitationDto>(await ToDtoAsync(invitation));
        }

        public async Task<InvitationOutcome<InvitationDto>> RevokeAsync(int actorId, int invitationId)
        {
            var invitation = await _repo.GetByIdAsync(invitationId);
            if (invitation == null)
            {
                return InvitationOutcome<InvitationDto>.Fail(InvitationErrors.NotFound, "Invitation not found.");
            }
            if (!invitation.RevokedAt.HasValue)
            {
                await _repo.RunInTransactionAsync(async () =>
                {
                    invitation.RevokedAt = Now;
                    invitation.RevokedByUserId = actorId;
                    await _repo.SaveAsync();
                    await _audit.RecordAsync(actorId, actorId, AuditAction.InvitationRevoked, Details(invitation, null));
                    return true;
                });
                _logger.LogInformation("Staff {ActorId} revoked invitation {InvitationId}", actorId, invitation.Id);
            }
            return new InvitationOutcome<InvitationDto>(await ToDtoAsync(invitation));
        }

        // ---------------------------------------------------------------- reads

        public async Task<InvitationPageDto> ListAsync(int pageNumber, int pageSize, string? status, string? kind)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 100);
            InvitationStatus? statusFilter = Enum.TryParse<InvitationStatus>(status, true, out var s) ? s : null;
            InvitationKind? kindFilter = Enum.TryParse<InvitationKind>(kind, true, out var k) ? k : null;
            var now = Now;
            var (items, total) = await _repo.SearchAsync(statusFilter, kindFilter, now, (pageNumber - 1) * pageSize, pageSize);
            var names = new Dictionary<int, string?>();
            var page = new InvitationPageDto { TotalCount = total, PageNumber = pageNumber, PageSize = pageSize };
            foreach (var invitation in items)
            {
                page.Items.Add(await ToDtoAsync(invitation, names, now));
            }
            return page;
        }

        public async Task<InvitationOutcome<List<InvitationRedemptionDto>>> GetRedemptionsAsync(int invitationId)
        {
            if (await _repo.GetByIdAsync(invitationId) == null)
            {
                return InvitationOutcome<List<InvitationRedemptionDto>>.Fail(InvitationErrors.NotFound, "Invitation not found.");
            }
            var list = new List<InvitationRedemptionDto>();
            foreach (var redemption in await _repo.GetRedemptionsAsync(invitationId))
            {
                var user = await _users.GetByIdAsync(redemption.UserId);
                list.Add(new InvitationRedemptionDto
                {
                    UserId = redemption.UserId,
                    Username = user?.Username,
                    EmailMasked = user?.Email == null ? null : EmailMask.Mask(user.Email),
                    RedeemedAt = DateTime.SpecifyKind(redemption.RedeemedAt, DateTimeKind.Utc),
                    MinecraftLinked = !string.IsNullOrEmpty(user?.Uuid)
                });
            }
            return new InvitationOutcome<List<InvitationRedemptionDto>>(list);
        }

        public async Task<InvitationOptionsDto> GetOptionsAsync()
        {
            var options = new InvitationOptionsDto
            {
                DefaultExpiryDays = ClampExpiry(null),
                MaxExpiryDays = Math.Max(1, _settings.MaxExpiryDays),
                SharedDefaultMaxUses = Math.Clamp(_settings.SharedDefaultMaxUses, 1, Math.Max(1, _settings.SharedMaxUsesCap)),
                SharedMaxUsesCap = Math.Max(1, _settings.SharedMaxUsesCap),
                RegistrationMode = _security.Registration.Mode.ToString()
            };
            foreach (var name in _settings.InvitableGroups.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var group = await _groups.GetByNameAsync(name);
                if (group != null)
                {
                    options.Groups.Add(new InvitableGroupDto { Id = group.Id, Name = group.Name });
                }
            }
            return options;
        }

        // ---------------------------------------------------------------- redemption

        public async Task<CheckInvitationResponseDto> CheckAsync(string? token, string? code)
        {
            var invitation = await FindUsableAsync(token, code);
            if (invitation == null)
            {
                return new CheckInvitationResponseDto { Valid = false, Error = InvitationErrors.InvalidInvite };
            }
            return new CheckInvitationResponseDto
            {
                Valid = true,
                Kind = invitation.Kind.ToString(),
                Email = invitation.Kind == InvitationKind.Personal ? invitation.Email : null,
                EmailHint = invitation.Kind == InvitationKind.Personal && invitation.Email != null ? EmailMask.Mask(invitation.Email) : null,
                ExpiresAt = DateTime.SpecifyKind(invitation.ExpiresAt, DateTimeKind.Utc)
            };
        }

        public async Task<Invitation?> FindUsableAsync(string? token, string? code)
        {
            Invitation? invitation = null;
            if (!string.IsNullOrWhiteSpace(token))
            {
                var normalized = InvitationCodes.NormalizePersonal(token);
                invitation = normalized == null ? null : await _repo.GetByHashAsync(InvitationKind.Personal, InvitationCodes.Hash(normalized));
            }
            else if (!string.IsNullOrWhiteSpace(code))
            {
                var normalized = InvitationCodes.NormalizeShared(code);
                invitation = normalized == null ? null : await _repo.GetByHashAsync(InvitationKind.Shared, InvitationCodes.Hash(normalized));
            }
            return invitation != null && invitation.StatusAt(Now) == InvitationStatus.Active ? invitation : null;
        }

        public async Task<User?> RedeemAsync(InviteSignup signup)
        {
            var invitation = signup.Invitation;
            var user = await _repo.RunInTransactionAsync<User?>(async () =>
            {
                var now = Now;
                if (!await _repo.TryTakeUseAsync(invitation.Id, now))
                {
                    return null;
                }

                string username;
                do
                {
                    username = InvitationCodes.NewShellUsername();
                } while (await _users.IsUsernameTakenAsync(username));

                var shell = new User
                {
                    Username = username,
                    Email = signup.Email,
                    PasswordHash = signup.PasswordHash,
                    LastPasswordChangeAt = now,
                    CreatedAt = now,
                    AccountCreatedVia = AccountCreationMethod.WebApp,
                    EmailVerified = signup.EmailVerified,
                    EmailVerifiedAt = signup.EmailVerified ? now : null,
                    AdmittedAt = now,
                    InvitedViaInvitationId = invitation.Id,
                    InvitedByUserId = invitation.CreatedByUserId
                };
                await _users.AddUserAsync(shell);

                var defaultGroup = await _groups.GetByNameAsync(DefaultGroupName);
                if (defaultGroup != null)
                {
                    await _repo.AddMembershipAsync(shell.Id, defaultGroup.Id);
                }
                else
                {
                    _logger.LogWarning("\"{DefaultGroupName}\" PermissionGroup not found — invited user {UserId} was not assigned a default group.", DefaultGroupName, shell.Id);
                }
                if (invitation.GrantGroupId.HasValue)
                {
                    await _repo.AddMembershipAsync(shell.Id, invitation.GrantGroupId.Value);
                }

                await _repo.AddRedemptionAsync(new InvitationRedemption { InvitationId = invitation.Id, UserId = shell.Id, RedeemedAt = now });
                await _audit.RecordAsync(shell.Id, shell.Id, AuditAction.InvitationRedeemed,
                    JsonSerializer.Serialize(new { invitationId = invitation.Id, kind = invitation.Kind.ToString(), invitedBy = invitation.CreatedByUserId }));
                await _email.QueueAsync(EmailKind.Welcome, shell.Email!,
                    new { username = "there", linkUrl = $"{_baseUrl}/account/link" },
                    $"welcome:{shell.Id}", userId: shell.Id);
                return shell;
            });

            if (user != null)
            {
                _logger.LogInformation("Web account {UserId} created with {Kind} invitation {InvitationId}", user.Id, invitation.Kind, invitation.Id);
            }
            return user;
        }

        // ---------------------------------------------------------------- helpers

        private int ClampExpiry(int? days) =>
            Math.Clamp(days ?? _settings.DefaultExpiryDays, 1, Math.Max(1, _settings.MaxExpiryDays));

        /// <summary>Null when the group is fine (or none); otherwise the refusal.</summary>
        private async Task<(string Error, string Message)?> CheckGroupAsync(int actorId, int? groupId)
        {
            if (!groupId.HasValue)
            {
                return null;
            }
            var group = await _groups.GetByIdAsync(groupId.Value);
            if (group == null || !_settings.InvitableGroups.Contains(group.Name, StringComparer.OrdinalIgnoreCase))
            {
                return (InvitationErrors.GroupNotInvitable, "This group can't be granted by an invitation.");
            }
            // The future account isn't known yet (0), so only the actor's rank and held nodes count.
            var check = await _guard.CanAssignGroupAsync(actorId, 0, group.Id);
            return check.Allowed ? null : (InvitationErrors.Escalation, check.Message ?? "You can't hand out this group.");
        }

        private async Task<string?> QueueInvitationMailAsync(Invitation invitation, string token, string inviterName)
        {
            invitation.SendCount++;
            invitation.LastSentAt = Now;
            await _repo.SaveAsync();
            var row = await _email.QueueAsync(EmailKind.Invitation, invitation.Email!,
                new { inviterName, inviteUrl = PersonalLink(token), expiresAt = invitation.ExpiresAt },
                $"invite:{invitation.Id}:{invitation.SendCount}", invitation.ExpiresAt);
            return row.Status.ToString();
        }

        private async Task<string> InviterNameAsync(int actorId)
        {
            var actor = await _users.GetByIdAsync(actorId);
            return string.IsNullOrWhiteSpace(actor?.Username) || actor.Username.StartsWith("web-", StringComparison.Ordinal)
                ? "The Knights & Kings team"
                : actor.Username;
        }

        private static string Details(Invitation invitation, int? replaced) =>
            JsonSerializer.Serialize(new
            {
                invitationId = invitation.Id,
                kind = invitation.Kind.ToString(),
                maxUses = invitation.MaxUses,
                expiresAt = invitation.ExpiresAt,
                groupId = invitation.GrantGroupId,
                label = invitation.Label,
                replacedInvitationId = replaced
            });

        private Task<InvitationDto> ToDtoAsync(Invitation invitation) =>
            ToDtoAsync(invitation, new Dictionary<int, string?>(), Now);

        private async Task<InvitationDto> ToDtoAsync(Invitation invitation, Dictionary<int, string?> names, DateTime now)
        {
            if (!names.TryGetValue(invitation.CreatedByUserId, out var createdBy))
            {
                createdBy = (await _users.GetByIdAsync(invitation.CreatedByUserId))?.Username;
                names[invitation.CreatedByUserId] = createdBy;
            }
            string? groupName = invitation.GrantGroup?.Name;
            if (groupName == null && invitation.GrantGroupId.HasValue)
            {
                groupName = (await _groups.GetByIdAsync(invitation.GrantGroupId.Value))?.Name;
            }
            return new InvitationDto
            {
                Id = invitation.Id,
                Kind = invitation.Kind.ToString(),
                Status = invitation.StatusAt(now).ToString(),
                CodeHint = invitation.CodeHint,
                Email = invitation.Email,
                Label = invitation.Label,
                Note = invitation.Note,
                GroupId = invitation.GrantGroupId,
                GroupName = groupName,
                MaxUses = invitation.MaxUses,
                UseCount = invitation.UseCount,
                CreatedAt = Utc(invitation.CreatedAt),
                CreatedByUserId = invitation.CreatedByUserId,
                CreatedByName = createdBy,
                ExpiresAt = Utc(invitation.ExpiresAt),
                PausedAt = invitation.PausedAt.HasValue ? Utc(invitation.PausedAt.Value) : null,
                RevokedAt = invitation.RevokedAt.HasValue ? Utc(invitation.RevokedAt.Value) : null,
                SendCount = invitation.SendCount,
                LastSentAt = invitation.LastSentAt.HasValue ? Utc(invitation.LastSentAt.Value) : null
            };
        }

        private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static string? Trim(string? value, int max)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
            {
                return false;
            }
            try
            {
                return new System.Net.Mail.MailAddress(email).Address == email;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}

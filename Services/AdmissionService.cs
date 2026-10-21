using System.Text.RegularExpressions;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The API side of the game server's admission gate (KNG-214, knk-workspace
    /// docs/specs/invitations/DESIGN.md § 5.2, plan § 6.1).
    /// </summary>
    public interface IAdmissionService
    {
        /// <summary>
        /// May this Mojang-verified player join? Outside Security:Registration:Mode=Invite always yes
        /// (the plugin keeps today's create-on-join). In Invite mode: an active admitted account →
        /// yes; a deactivated or deleted one → no, without a code; anyone else → no, with the
        /// UUID's claim code (one active per UUID, shown again on a rejoin).
        /// </summary>
        /// <exception cref="ArgumentException">Not a UUID, or not a Minecraft name.</exception>
        Task<AdmissionResultDto> CheckAsync(string uuid, string? name);

        /// <summary>For /link before login: is this an active claim code, and for which name?</summary>
        Task<ClaimCheckResponseDto> CheckClaimAsync(string? code);

        /// <summary>
        /// Queues one FinishLinking mail (dedupe key <c>finish-linking:{userId}</c>) for each invited
        /// account still without Minecraft Admission:FinishLinkingReminderDays after sign-up. Looks
        /// back a week from there, so a sweep that missed a day still finds them.
        /// </summary>
        /// <returns>How many accounts were looked at.</returns>
        Task<int> QueueFinishLinkingRemindersAsync(CancellationToken ct = default);
    }

    public class AdmissionService : IAdmissionService
    {
        private static readonly Regex MinecraftName = new("^[A-Za-z0-9_]{1,16}$", RegexOptions.Compiled);

        private readonly IUserRepository _users;
        private readonly ILinkCodeService _codes;
        private readonly IEmailComposer? _email;
        private readonly AdmissionSettings _settings;
        private readonly RegistrationMode _mode;
        private readonly string _baseUrl;
        private readonly TimeProvider _time;
        private readonly ILogger _logger;

        public AdmissionService(
            IUserRepository users,
            ILinkCodeService codes,
            IOptions<AdmissionSettings> settings,
            IOptions<SecuritySettings> security,
            IEmailComposer? email = null,
            IOptions<AppSettings>? app = null,
            TimeProvider? time = null,
            ILogger<AdmissionService>? logger = null)
        {
            _users = users;
            _codes = codes;
            _email = email;
            _settings = settings.Value;
            _mode = security.Value.Registration.Mode;
            _baseUrl = AppSettings.ResolveBaseUrl(app?.Value.PublicBaseUrl, security.Value.PasswordResetFrontendBaseUrl);
            _time = time ?? TimeProvider.System;
            _logger = logger ?? (ILogger)NullLogger.Instance;
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        /// <summary>The UUID as the plugin sends it and accounts store it: lower case with hyphens.</summary>
        public static bool TryNormalizeUuid(string? uuid, out string normalized)
        {
            normalized = string.Empty;
            if (!Guid.TryParse(uuid?.Trim(), out var parsed))
            {
                return false;
            }
            normalized = parsed.ToString("D");
            return true;
        }

        public async Task<AdmissionResultDto> CheckAsync(string uuid, string? name)
        {
            if (!TryNormalizeUuid(uuid, out var normalized))
            {
                throw new ArgumentException("Not a UUID.", nameof(uuid));
            }
            var trimmedName = name?.Trim() ?? string.Empty;
            if (!MinecraftName.IsMatch(trimmedName))
            {
                throw new ArgumentException("Not a Minecraft name.", nameof(name));
            }

            if (_mode != RegistrationMode.Invite)
            {
                return new AdmissionResultDto { Admitted = true };
            }

            var user = await _users.GetByUuidAsync(normalized);
            if (user != null && (!user.IsActive || user.DeletedAt.HasValue))
            {
                return new AdmissionResultDto { Admitted = false, Reason = AdmissionReasons.Deactivated };
            }
            if (user?.AdmittedAt != null)
            {
                await FollowNameChangeAsync(user, trimmedName);
                return new AdmissionResultDto { Admitted = true };
            }

            var code = await _codes.CreateClaimCodeAsync(normalized, trimmedName, TimeSpan.FromMinutes(Math.Max(1, _settings.ClaimCodeMinutes)));
            // The code itself is never logged.
            _logger.LogInformation("Admission: {Uuid} ({Name}) is not admitted; claim code until {ExpiresAt:u}", normalized, trimmedName, code.ExpiresAt);
            return new AdmissionResultDto
            {
                Admitted = false,
                Reason = AdmissionReasons.NotAdmitted,
                Code = LinkCodeService.FormatClaimCode(code.Code),
                ExpiresAt = code.ExpiresAt
            };
        }

        /// <summary>
        /// The account's name follows the Mojang name, as on join today. A name another account
        /// still holds is left alone here (the plugin's own join handling deals with it).
        /// </summary>
        private async Task FollowNameChangeAsync(User user, string name)
        {
            if (string.Equals(user.Username, name, StringComparison.Ordinal))
            {
                return;
            }
            if (await _users.IsUsernameTakenAsync(name, user.Id))
            {
                _logger.LogWarning("Admission: {Uuid} now plays as {Name}, which another account holds; name not changed", user.Uuid, name);
                return;
            }
            user.Username = name;
            await _users.UpdateUserAsync(user);
        }

        public async Task<ClaimCheckResponseDto> CheckClaimAsync(string? code)
        {
            var normalized = LinkCodeService.NormalizeClaimCode(code ?? string.Empty);
            if (normalized.Length != LinkCodeService.CodeLength)
            {
                return new ClaimCheckResponseDto { Valid = false };
            }
            var (valid, linkCode, _) = await _codes.ValidateLinkCodeAsync(normalized);
            if (!valid || linkCode == null || linkCode.Purpose != LinkCodePurpose.Claim || string.IsNullOrWhiteSpace(linkCode.MinecraftUuid))
            {
                return new ClaimCheckResponseDto { Valid = false };
            }
            return new ClaimCheckResponseDto { Valid = true, MinecraftName = linkCode.MinecraftName, ExpiresAt = linkCode.ExpiresAt };
        }

        public async Task<int> QueueFinishLinkingRemindersAsync(CancellationToken ct = default)
        {
            if (_email == null || _settings.FinishLinkingReminderDays <= 0)
            {
                return 0;
            }
            var before = Now.AddDays(-_settings.FinishLinkingReminderDays);
            var users = await _users.GetUnlinkedInvitedUsersAsync(before.AddDays(-7), before);
            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();
                // "there", as in the Welcome mail: the web-… placeholder name means nothing to the player.
                await _email.QueueAsync(EmailKind.FinishLinking, user.Email!,
                    new { username = "there", linkUrl = $"{_baseUrl}/account/link" },
                    $"finish-linking:{user.Id}", userId: user.Id, ct: ct);
            }
            return users.Count;
        }
    }
}

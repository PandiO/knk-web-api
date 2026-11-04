using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Email;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service for managing user accounts, authentication, and account linking.
    /// Implements OWASP 2023 password guidelines and secure account management.
    /// </summary>
    public class UserService : IUserService
    {
        private readonly IUserRepository _repo;
        private readonly IMapper _mapper;
        private readonly IPasswordService _passwordService;
        private readonly ILinkCodeService _linkCodeService;
        private readonly ITitleService _titleService;
        private readonly IUserPermissionGroupService _membershipService;
        private readonly IAuditLogService _auditLogService;
        private readonly IPermissionGroupRepository _permissionGroupRepo;
        private readonly ILogger<UserService> _logger;
        private readonly ICurrencyService _currency;
        private readonly ITitleProgressionService _titleProgression;
        private readonly IPlayerNotificationQueue? _notificationQueue;
        private readonly ISessionRevocationService? _sessions;
        private readonly ILinkAttemptLimiter? _linkAttempts;
        private readonly RegistrationMode _registrationMode;
        private readonly IInvitationRepository? _invitations;
        private readonly IEmailComposer? _email;
        private readonly knkwebapi_v2.Services.CloudflareAccess.ICloudflareAccessSync? _accessSync;
        private readonly IRealtimeEventSink? _realtime;

        public UserService(
            IUserRepository repo,
            IMapper mapper,
            IPasswordService passwordService,
            ILinkCodeService linkCodeService,
            ITitleService titleService,
            IUserPermissionGroupService membershipService,
            IAuditLogService auditLogService,
            IPermissionGroupRepository permissionGroupRepo,
            ILogger<UserService> logger,
            ICurrencyService currency,
            ITitleProgressionService titleProgression,
            IPlayerNotificationQueue? notificationQueue = null,
            ISessionRevocationService? sessions = null,
            ILinkAttemptLimiter? linkAttempts = null,
            IOptions<SecuritySettings>? security = null,
            IInvitationRepository? invitations = null,
            IEmailComposer? email = null,
            knkwebapi_v2.Services.CloudflareAccess.ICloudflareAccessSync? accessSync = null,
            IRealtimeEventSink? realtime = null)
        {
            // PandiO/knk-workspace#179: user-state changes reach the game server over the realtime hub.
            _realtime = realtime;
            // KNG-216: account changes that affect the Cloudflare Access list wake its sync.
            _accessSync = accessSync;
            // KNG-214: the MinecraftLinked mail; optional like the ones above.
            _email = email;
            _invitations = invitations;
            _registrationMode = security?.Value.Registration.Mode ?? RegistrationMode.LinkCode;
            _notificationQueue = notificationQueue;
            _sessions = sessions;
            _linkAttempts = linkAttempts;
            _currency = currency;
            _titleProgression = titleProgression;
            _repo = repo;
            _mapper = mapper;
            _passwordService = passwordService;
            _linkCodeService = linkCodeService;
            _titleService = titleService;
            _membershipService = membershipService;
            _auditLogService = auditLogService;
            _permissionGroupRepo = permissionGroupRepo;
            _logger = logger;
        }

        /// <summary>
        /// Name of the standard group every account is assigned to on creation (developer
        /// request, 2026-09-25 — "the standard group a player should be put in on first
        /// join/account creation"), seeded by migration SeedDefaultPermissionGroup. Matched by
        /// name rather than a hardcoded id since the seed migration lets MySQL assign the id.
        /// </summary>
        public const string DefaultGroupName = "Default";

        /// <summary>
        /// Maps a User to a UserDto and fills in the title fields resolved from its current
        /// ExperiencePoints (docs/specs/user-features/IMPLEMENTATION_PLAN.md §4) and its current
        /// premium tier (§5). Every code path that returns a UserDto goes through this instead of
        /// _mapper.Map directly, so both stay in sync everywhere rather than needing every call
        /// site updated by hand.
        /// </summary>
        private async Task<UserDto> MapToUserDtoAsync(User user)
        {
            var dto = _mapper.Map<UserDto>(user);
            var title = await _titleService.ResolveAsync(user.ExperiencePoints, user.Gender);
            dto.TitleBracketId = title.TitleBracketId;
            dto.TitleName = title.TitleName;
            dto.PrestigeExperience = title.PrestigeExperience;
            // A brand-new, not-yet-saved user has Id 0 and can't hold memberships.
            if (user.Id > 0)
            {
                var tier = await _membershipService.GetActivePremiumTierAsync(user.Id);
                dto.PremiumTierGroupId = tier?.PermissionGroupId;
                dto.PremiumTierName = tier?.PermissionGroupName;
                dto.PremiumTierExpiresAt = tier?.ExpiresAt;
                var defaultGroup = await GetDefaultGroupAsync();
                dto.ChatPrimaryColor = tier?.ChatPrimaryColor ?? defaultGroup?.ChatPrimaryColor;
                dto.ChatSecondaryColor = tier?.ChatSecondaryColor ?? defaultGroup?.ChatSecondaryColor;
                dto.NameColor = tier?.NameColor ?? defaultGroup?.NameColor;
                dto.PermissionGroups = await GetPermissionGroupsInPrecedenceAsync(user.Id);
            }
            return dto;
        }

        /// <summary>
        /// The user's effective groups (unexpired memberships plus inherited parents) in Game
        /// Settings precedence order (KNG-52), which is the teleport fee order (KNG-41). The
        /// repository returns the whole parent chain, so the parents are found among the returned groups.
        /// </summary>
        private async Task<List<PermissionGroupRefDto>> GetPermissionGroupsInPrecedenceAsync(int userId)
        {
            var groups = await _permissionGroupRepo.GetActiveGroupsForUserAsync(userId, DateTime.UtcNow)
                ?? new List<PermissionGroup>();
            var byId = groups.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
            return PermissionGroupPrecedence.Order(byId.Values, byId)
                .Select(g => new PermissionGroupRefDto { Id = g.Id, Name = g.Name })
                .ToList();
        }

        private PermissionGroup? _defaultGroup;
        private bool _defaultGroupLoaded;

        /// <summary>
        /// The "Default" group supplies the chat/tab-list colors (KNG-7) for players without a
        /// premium tier. Looked up by name, not membership, since it is the fallback look for
        /// everyone. Loaded once per service instance (scoped = once per request) so list
        /// endpoints mapping many users don't query it per user.
        /// </summary>
        private async Task<PermissionGroup?> GetDefaultGroupAsync()
        {
            if (!_defaultGroupLoaded)
            {
                _defaultGroup = await _permissionGroupRepo.GetByNameAsync(DefaultGroupName);
                _defaultGroupLoaded = true;
            }
            return _defaultGroup;
        }

        public async Task<IEnumerable<UserDto>> GetAllAsync()
        {
            var users = await _repo.GetAllAsync();
            var dtos = new List<UserDto>();
            foreach (var user in users)
            {
                dtos.Add(await MapToUserDtoAsync(user));
            }
            return dtos;
        }

        public async Task<UserDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var user = await _repo.GetByIdAsync(id);
            return user == null ? null : await MapToUserDtoAsync(user);
        }

        public async Task<UserDto?> GetByUuidAsync(string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return null;
            var user = await _repo.GetByUuidAsync(uuid);
            return user == null ? null : await MapToUserDtoAsync(user);
        }

        public async Task<UserDto?> GetByUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            var user = await _repo.GetByUsernameAsync(username);
            return user == null ? null : await MapToUserDtoAsync(user);
        }

        public async Task<UserDto> CreateAsync(UserCreateDto userDto)
        {
            if (userDto == null) throw new ArgumentNullException(nameof(userDto));
            if (string.IsNullOrWhiteSpace(userDto.Username)) throw new ArgumentException("Username is required.", nameof(userDto));
            if (!string.IsNullOrWhiteSpace(userDto.Email) && string.IsNullOrWhiteSpace(userDto.Password))
            {
                throw new ArgumentException("Password is required when email is provided.", nameof(userDto));
            }

            if (!string.IsNullOrWhiteSpace(userDto.Password) && string.IsNullOrWhiteSpace(userDto.Email))
            {
                throw new ArgumentException("Email is required when password is provided.", nameof(userDto));
            }

            var user = _mapper.Map<User>(userDto);
            user.CreatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(userDto.Password))
            {
                user.PasswordHash = await _passwordService.HashPasswordAsync(userDto.Password);
                user.LastPasswordChangeAt = DateTime.UtcNow;
            }

            user.AccountCreatedVia = string.IsNullOrWhiteSpace(userDto.Email)
                ? AccountCreationMethod.MinecraftServer
                : AccountCreationMethod.WebApp;

            // KNG-212: until Security:Registration:Mode is Invite, everyone who gets an account (the
            // plugin creates it on first join through the whitelist) is admitted, so switching the
            // plugin's admission gate on later locks nobody out. Under Invite an account needs an
            // invitation, a claim-code link or staff to be admitted (KNG-214).
            user.AdmittedAt = _registrationMode == RegistrationMode.Invite ? null : user.CreatedAt;

            // The row is inserted at balance 0 (EF never writes the balance columns); the starting
            // balance is a SIGNUP_GRANT ledger posting in the same transaction, so there is never
            // an account without its grant or a grant without its account (currency DESIGN.md
            // §3.10 step 2). No ids to lock yet: this is just the transaction.
            await _repo.RunWithUsersLockedAsync(Array.Empty<int>(), async () =>
            {
                await _repo.AddUserAsync(user);
                await GrantSignupBalanceAsync(user.Id);
            });
            await AssignDefaultGroupAsync(user.Id);
            return await MapToUserDtoAsync(user);
        }

        /// <summary>
        /// Posts the starting balance (CurrencyPolicy.SignupGrant: 250 coins / 50 gems unless
        /// changed) under the once-only key <c>signup:{userId}</c>.
        /// </summary>
        private async Task GrantSignupBalanceAsync(int userId)
        {
            var policies = await _currency.GetPoliciesAsync();
            var legs = new[] { Currency.Coins, Currency.Gems, Currency.Experience }
                .Select(c => new CurrencyLeg(userId, c,
                    policies != null && policies.TryGetValue(c, out var policy) ? policy.SignupGrant : CurrencyPolicy.DefaultSignupGrant(c)))
                .Where(l => l.Amount > 0)
                .ToList();
            if (legs.Count == 0)
            {
                return;
            }
            var ctx = CurrencyContext.ForSystem("UserService", CurrencyReasons.SignupGrant, $"signup:{userId}") with
            {
                SourceType = "User",
                SourceRef = userId.ToString()
            };
            await _currency.PostAsync(legs, ctx);
        }

        /// <summary>
        /// Assigns every newly created account to the standard "Default" group (developer
        /// request, 2026-09-25) — covers both the web-first flow here and the Minecraft-first
        /// flow, since knk-plugin's user creation also goes through this same CreateAsync via
        /// POST /api/Users. Best-effort: a missing "Default" seed (e.g. this migration hasn't
        /// run yet on an older database) must not block account creation, so this only logs a
        /// warning rather than throwing.
        /// </summary>
        private async Task AssignDefaultGroupAsync(int userId)
        {
            var defaultGroup = await _permissionGroupRepo.GetByNameAsync(DefaultGroupName);
            if (defaultGroup == null)
            {
                _logger.LogWarning("\"{DefaultGroupName}\" PermissionGroup not found — new user {UserId} was not assigned a default group. Run the SeedDefaultPermissionGroup migration.", DefaultGroupName, userId);
                return;
            }

            await _membershipService.UpsertAsync(new UpsertUserPermissionGroupDto
            {
                UserId = userId,
                PermissionGroupId = defaultGroup.Id,
                ExpiresAt = null
            });
        }

        public async Task UpdateAsync(int id, UserDto userDto, int? actorUserId = null)
        {
            if (userDto == null) throw new ArgumentNullException(nameof(userDto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(userDto.Username)) throw new ArgumentException("Username is required.", nameof(userDto));
            if (string.IsNullOrWhiteSpace(userDto.Email)) throw new ArgumentException("Email is required.", nameof(userDto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"User with id {id} not found.");

            var originalUuid = existing.Uuid;
            var originalCreatedAt = existing.CreatedAt;
            var originalEmail = existing.Email;
            var wasActive = existing.IsActive;

            // Apply all editable UserDto fields onto the tracked entity. The mapping profile
            // (UserMappingProfile: UserDto -> User) ignores fields that must never be set from
            // this endpoint: credentials and soft-delete fields, service-managed state
            // (ActiveMode, presence, LastSalaryPayoutAt) and, since KNG-22, Coins, Gems,
            // ExperiencePoints and the personal multipliers. Those change only through
            // PUT {id}/balances and PUT {id}/multipliers, which lock, validate and audit them
            // (docs/specs/currency-payments/DESIGN.md §1.4 A1).
            // Since KNG-195 it also leaves freeze state, EmailVerified and AccountCreatedVia alone,
            // and writes Gender, IsActive and GatePassThroughMethodDefault only when sent.
            _mapper.Map(userDto, existing);

            // CreatedAt is an immutable audit field: a generic edit form that doesn't include
            // it would otherwise submit UserDto's constructor default (DateTime.UtcNow at
            // serialization time) and silently overwrite the real creation date on every save.
            existing.CreatedAt = originalCreatedAt;

            // Preserve the existing UUID unless the caller explicitly supplied a new one
            // (used for web-app-first account linking).
            if (string.IsNullOrEmpty(userDto.Uuid))
            {
                existing.Uuid = originalUuid;
            }

            await _repo.UpdateUserAsync(existing);

            // Closed-alpha WP4: a staff email change or deactivation ends the player's sessions.
            if (!string.Equals(originalEmail, existing.Email, StringComparison.OrdinalIgnoreCase))
            {
                await RevokeSessionsAsync(id, "email changed by staff");
            }
            else if (wasActive && !existing.IsActive)
            {
                await RevokeSessionsAsync(id, "account deactivated");
            }
            else if (!wasActive && existing.IsActive)
            {
                _accessSync?.RequestSync();
            }
        }

        /// <summary>
        /// Ends the account's sessions. Every caller is an email change, a deactivation, a delete, a
        /// merge, a detached web login or a password change, so it also wakes the Cloudflare Access
        /// sync (KNG-216); a run that finds nothing to change costs one list read.
        /// </summary>
        private Task RevokeSessionsAsync(int userId, string reason)
        {
            _accessSync?.RequestSync();
            return _sessions?.RevokeAllSessionsAsync(userId, reason) ?? Task.CompletedTask;
        }

        /// <summary>Upper bound for a personal multiplier (KNG-22): keeps a typo or a hostile
        /// edit from turning one salary payout into billions of coins.</summary>
        public const decimal MaxPersonalMultiplier = 100m;

        public async Task SetPersonalMultipliersAsync(int id, UpdatePersonalMultipliersDto request, int? actorUserId = null)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateMultiplier(request.PersonalSalaryMultiplier, nameof(request.PersonalSalaryMultiplier));
            ValidateMultiplier(request.PersonalGemBonusMultiplier, nameof(request.PersonalGemBonusMultiplier));
            ValidateMultiplier(request.PersonalExpBonusMultiplier, nameof(request.PersonalExpBonusMultiplier));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"User with id {id} not found.");

            var previousMultiplier = existing.PersonalSalaryMultiplier;
            var previousGemBonusMultiplier = existing.PersonalGemBonusMultiplier;
            var previousExpBonusMultiplier = existing.PersonalExpBonusMultiplier;

            existing.PersonalSalaryMultiplier = request.PersonalSalaryMultiplier ?? previousMultiplier;
            existing.PersonalGemBonusMultiplier = request.PersonalGemBonusMultiplier ?? previousGemBonusMultiplier;
            existing.PersonalExpBonusMultiplier = request.PersonalExpBonusMultiplier ?? previousExpBonusMultiplier;

            if (existing.PersonalSalaryMultiplier == previousMultiplier
                && existing.PersonalGemBonusMultiplier == previousGemBonusMultiplier
                && existing.PersonalExpBonusMultiplier == previousExpBonusMultiplier)
            {
                return;
            }

            await _repo.UpdateUserAsync(existing);

            // Same entry shape the generic edit used to write, so the web activity feed
            // (knk-web-app utils/auditDetails.ts) keeps describing it.
            await _auditLogService.RecordAsync(actorUserId, id, AuditAction.BalanceAdjusted, JsonSerializer.Serialize(new
            {
                source = "PersonalMultipliers",
                coinsDelta = 0,
                gemsDelta = 0,
                experienceDelta = 0,
                previousPersonalSalaryMultiplier = previousMultiplier,
                newPersonalSalaryMultiplier = existing.PersonalSalaryMultiplier,
                previousPersonalGemBonusMultiplier = previousGemBonusMultiplier,
                newPersonalGemBonusMultiplier = existing.PersonalGemBonusMultiplier,
                previousPersonalExpBonusMultiplier = previousExpBonusMultiplier,
                newPersonalExpBonusMultiplier = existing.PersonalExpBonusMultiplier
            }));
            _realtime.UserChanged(existing, new[] { UserChangedFields.Multipliers }, actorUserId);
        }

        private static void ValidateMultiplier(decimal? value, string name)
        {
            if (value.HasValue && (value.Value < 0 || value.Value > MaxPersonalMultiplier))
            {
                throw new ArgumentException($"{name} must be between 0 and {MaxPersonalMultiplier}.", name);
            }
        }

        public async Task UpdateGatePassThroughMethodAsync(int id, GatePassThroughMethod method)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"User with id {id} not found.");

            var previous = existing.GatePassThroughMethodDefault;
            await _repo.UpdateGatePassThroughMethodAsync(id, method);
            if (previous != method)
            {
                _realtime.UserChanged(existing, new[] { UserChangedFields.GatePassThroughMethod });
            }
        }

        public async Task UpdateActiveModeAsync(int id, ActiveMode mode, int? actorUserId = null)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (!Enum.IsDefined(typeof(ActiveMode), mode)) throw new ArgumentException($"Unknown active mode '{mode}'.", nameof(mode));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"User with id {id} not found.");

            var previousMode = existing.ActiveMode;
            await _repo.UpdateActiveModeAsync(id, mode);

            if (previousMode != mode)
            {
                await _auditLogService.RecordAsync(actorUserId, id, AuditAction.VanishToggled,
                    $"{{\"from\":\"{previousMode}\",\"to\":\"{mode}\"}}");
                // Before #179 a mode set in the web app only applied on the player's next join.
                _realtime.ModeChanged(existing, mode, actorUserId);
            }
        }

        public async Task UpdatePresenceAsync(int id, bool isOnline)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"User with id {id} not found.");

            // Not audit-logged: this is a passive system signal from PlayerListener's
            // join/quit hooks, not an admin action (docs/specs/user-management/DESIGN.md §4
            // scopes the audit trail to admin/system *mutations affecting a player*, e.g. balance
            // or group changes — presence pings would just be noise at server-restart volume).
            await _repo.UpdatePresenceAsync(id, isOnline);
        }

        public async Task SetFrozenAsync(int userId, bool frozen, string? reason, int? actorUserId = null)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user ID.", nameof(userId));
            if (frozen && string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A reason is required to freeze a player.", nameof(reason));

            var user = await _repo.GetByIdAsync(userId);
            if (user == null) throw new KeyNotFoundException($"User with ID {userId} not found.");

            user.IsFrozen = frozen;
            user.FrozenReason = frozen ? reason : null;
            user.FrozenByUserId = frozen ? actorUserId : null;
            user.FrozenAt = frozen ? DateTime.UtcNow : null;
            await _repo.UpdateUserAsync(user);

            await _auditLogService.RecordAsync(actorUserId, userId, frozen ? AuditAction.PlayerFrozen : AuditAction.PlayerUnfrozen,
                JsonSerializer.Serialize(new { reason }));
            // Before #179 a freeze set in the web app only applied on the player's next join.
            _realtime.FrozenChanged(user, actorUserId);
        }

        /// <summary>TeleportKind names the plugin may audit (docs/specs/teleport/DESIGN.md §3.4).</summary>
        public static readonly IReadOnlyCollection<string> TeleportAuditKinds = new[] { "STAFF", "REQUEST", "SPAWN", "WARP", "BACK" };

        private const int TeleportAuditMaxWorldLength = 64;
        private const int TeleportAuditMaxReasonLength = 256;
        private const double TeleportAuditMaxCoordinate = 30_000_000;

        public async Task RecordTeleportAuditAsync(int targetUserId, TeleportAuditDto dto, int? actorUserId = null)
        {
            if (targetUserId <= 0) throw new ArgumentException("Invalid user ID.", nameof(targetUserId));
            if (dto == null) throw new ArgumentException("A teleport audit body is required.", nameof(dto));

            var target = await _repo.GetByIdAsync(targetUserId);
            if (target == null) throw new KeyNotFoundException($"User with ID {targetUserId} not found.");

            // Anonymous callers can reach this (plugin endpoint, KNG-22 adds the service key), so
            // nothing in the body is taken on trust: the entry must be about user {id}, every user
            // it names must exist, and the free-text parts are bounded.
            var kind = dto.Kind?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(kind) || !TeleportAuditKinds.Contains(kind))
                throw new ArgumentException($"Unknown teleport kind '{dto.Kind}'.", nameof(dto.Kind));
            if (dto.SubjectUserId <= 0) throw new ArgumentException("subjectUserId is required.", nameof(dto.SubjectUserId));
            if (dto.VisitedUserId is <= 0) throw new ArgumentException("Invalid visitedUserId.", nameof(dto.VisitedUserId));
            if (targetUserId != dto.SubjectUserId && targetUserId != dto.VisitedUserId)
                throw new ArgumentException("The audited user must be the moved or the visited player.", nameof(targetUserId));
            if (dto.DomainId is <= 0) throw new ArgumentException("Invalid domainId.", nameof(dto.DomainId));
            ValidateTeleportPoint(dto.From, "from");
            ValidateTeleportPoint(dto.To, "to");
            var reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            if (reason?.Length > TeleportAuditMaxReasonLength)
                throw new ArgumentException($"reason must be at most {TeleportAuditMaxReasonLength} characters.", nameof(dto.Reason));
            var via = string.IsNullOrWhiteSpace(dto.Via) ? "command" : dto.Via.Trim().ToLowerInvariant();
            if (via != "command" && via != "console") throw new ArgumentException($"Unknown via '{dto.Via}'.", nameof(dto.Via));

            var subject = dto.SubjectUserId == targetUserId ? target : await _repo.GetByIdAsync(dto.SubjectUserId);
            if (subject == null) throw new ArgumentException($"Subject user {dto.SubjectUserId} not found.", nameof(dto.SubjectUserId));
            User? visited = null;
            if (dto.VisitedUserId.HasValue)
            {
                visited = dto.VisitedUserId.Value == targetUserId ? target : await _repo.GetByIdAsync(dto.VisitedUserId.Value);
                if (visited == null) throw new ArgumentException($"Visited user {dto.VisitedUserId} not found.", nameof(dto.VisitedUserId));
            }

            // Usernames are resolved here rather than sent by the plugin, so the Recent Activity
            // line ("Alice → Bob") can't be spoofed and still reads right after a rename.
            await _auditLogService.RecordAsync(actorUserId, targetUserId, AuditAction.PlayerTeleported, JsonSerializer.Serialize(new
            {
                kind,
                subjectUserId = subject.Id,
                subjectUsername = subject.Username,
                visitedUserId = visited?.Id,
                visitedUsername = visited?.Username,
                from = new { world = dto.From.World.Trim(), x = dto.From.X, y = dto.From.Y, z = dto.From.Z },
                to = new { world = dto.To.World.Trim(), x = dto.To.X, y = dto.To.Y, z = dto.To.Z, domainId = dto.DomainId },
                silent = dto.Silent,
                reason,
                via
            }));
        }

        private static void ValidateTeleportPoint(TeleportAuditPointDto? point, string name)
        {
            if (point == null) throw new ArgumentException($"{name} is required.", name);
            if (string.IsNullOrWhiteSpace(point.World) || point.World.Trim().Length > TeleportAuditMaxWorldLength)
                throw new ArgumentException($"{name}.world is required (at most {TeleportAuditMaxWorldLength} characters).", name);
            foreach (var value in new[] { point.X, point.Y, point.Z })
            {
                if (!double.IsFinite(value) || Math.Abs(value) > TeleportAuditMaxCoordinate)
                    throw new ArgumentException($"{name} has an invalid coordinate.", name);
            }
        }

        public async Task<IEnumerable<UserListDto>> SearchByGroupAsync(int groupId, bool? onlineOnly = null)
        {
            if (groupId <= 0) throw new ArgumentException("Invalid group id.", nameof(groupId));
            var users = await _repo.SearchByGroupAsync(groupId, onlineOnly);
            return _mapper.Map<IEnumerable<UserListDto>>(users);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"User with id {id} not found.");

            // A hard delete takes the balance columns with the row, outside the ledger, and
            // leaves ledger rows for a user the reconciler can no longer find: a permanent R1
            // mismatch that switches player transfers off again every day (currency DESIGN.md
            // §3.1 invariants 1 and 6, §3.9). An account with money or money history stays;
            // merge it into another account instead (MERGE_FORFEIT/MERGE_CARRYOVER, soft delete).
            var history = await _currency.GetHistoryAsync(new LedgerQuery { UserId = id, Page = 1, PageSize = 1 });
            if (existing.Coins != 0 || existing.Gems != 0 || history.TotalCount > 0)
            {
                throw new InvalidOperationException(
                    $"User {id} has a currency balance or ledger history, so it can't be deleted; merge the account instead.");
            }

            await RevokeSessionsAsync(id, "account deleted");
            await _repo.DeleteUserAsync(id);
        }

        public async Task<PagedResultDto<UserListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            var resultDto = _mapper.Map<PagedResultDto<UserListDto>>(result);

            return resultDto;
        }

        // ===== NEW METHODS: VALIDATION =====

        /// <inheritdoc/>
        public async Task<(bool IsValid, string? ErrorMessage)> ValidateUserCreationAsync(UserCreateDto dto, int? minecraftOnlyAccountIdBeingLinked = null)
        {
            if (dto == null)
            {
                return (false, "User data is required.");
            }

            // Validate username
            if (string.IsNullOrWhiteSpace(dto.Username))
            {
                return (false, "Username is required.");
            }

            if (dto.Username.Length < 3 || dto.Username.Length > 50)
            {
                return (false, "Username must be between 3 and 50 characters.");
            }

            // Check username uniqueness (skip if linking to minecraft-only account)
            if (!minecraftOnlyAccountIdBeingLinked.HasValue)
            {
                var (usernameTaken, _) = await CheckUsernameTakenAsync(dto.Username);
                if (usernameTaken)
                {
                    return (false, "Username is already taken.");
                }
            }

            // Validate email (if provided)
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                if (!IsValidEmail(dto.Email))
                {
                    return (false, "Invalid email format.");
                }

                var (emailTaken, _) = await CheckEmailTakenAsync(dto.Email);
                if (emailTaken)
                {
                    return (false, "Email is already registered.");
                }

                if (string.IsNullOrWhiteSpace(dto.Password))
                {
                    return (false, "Password is required when email is provided.");
                }
            }

            // Validate UUID (if provided)
            if (!string.IsNullOrWhiteSpace(dto.Uuid))
            {
                var (uuidTaken, _) = await CheckUuidTakenAsync(dto.Uuid);
                if (uuidTaken)
                {
                    return (false, "UUID is already registered.");
                }
            }

            // Validate password (if provided)
            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                if (string.IsNullOrWhiteSpace(dto.Email))
                {
                    return (false, "Email is required when password is provided.");
                }

                var (isValidPassword, passwordError) = await ValidatePasswordAsync(dto.Password);
                if (!isValidPassword)
                {
                    return (false, passwordError);
                }

                // Check password confirmation
                if (dto.Password != dto.PasswordConfirmation)
                {
                    return (false, "Password and confirmation do not match.");
                }
            }

            return (true, null);
        }

        /// <inheritdoc/>
        public async Task<(bool IsValid, string? ErrorMessage)> ValidatePasswordAsync(string password)
        {
            return await _passwordService.ValidatePasswordAsync(password);
        }

        // ===== NEW METHODS: UNIQUE CONSTRAINT CHECKS =====

        /// <inheritdoc/>
        public async Task<(bool IsTaken, int? ConflictingUserId)> CheckUsernameTakenAsync(string username, int? excludeUserId = null)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return (false, null);
            }

            var isTaken = await _repo.IsUsernameTakenAsync(username, excludeUserId);
            
            if (isTaken)
            {
                var user = await _repo.GetByUsernameAsync(username);
                return (true, user?.Id);
            }

            return (false, null);
        }

        /// <inheritdoc/>
        public async Task<(bool IsTaken, int? ConflictingUserId)> CheckEmailTakenAsync(string email, int? excludeUserId = null)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return (false, null);
            }

            var isTaken = await _repo.IsEmailTakenAsync(email, excludeUserId);
            
            if (isTaken)
            {
                var user = await _repo.GetByEmailAsync(email);
                return (true, user?.Id);
            }

            return (false, null);
        }

        /// <inheritdoc/>
        public async Task<(bool IsTaken, int? ConflictingUserId)> CheckUuidTakenAsync(string uuid, int? excludeUserId = null)
        {
            if (string.IsNullOrWhiteSpace(uuid))
            {
                return (false, null);
            }

            var isTaken = await _repo.IsUuidTakenAsync(uuid, excludeUserId);
            
            if (isTaken)
            {
                var user = await _repo.GetByUuidAsync(uuid);
                return (true, user?.Id);
            }

            return (false, null);
        }

        // ===== NEW METHODS: CREDENTIALS MANAGEMENT =====

        /// <inheritdoc/>
        public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, string passwordConfirmation)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Invalid user ID.", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                throw new ArgumentException("Current password is required.", nameof(currentPassword));
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                throw new ArgumentException("New password is required.", nameof(newPassword));
            }

            if (newPassword != passwordConfirmation)
            {
                throw new ArgumentException("New password and confirmation do not match.");
            }

            var user = await _repo.GetByIdAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID {userId} not found.");
            }

            // Verify current password
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                var isValidPassword = await _passwordService.VerifyPasswordAsync(currentPassword, user.PasswordHash);
                if (!isValidPassword)
                {
                    throw new UnauthorizedAccessException("Current password is incorrect.");
                }
            }

            // Validate new password
            var (isValid, error) = await _passwordService.ValidatePasswordAsync(newPassword);
            if (!isValid)
            {
                throw new ArgumentException(error ?? "Invalid password.");
            }

            // Hash and update password
            var newHash = await _passwordService.HashPasswordAsync(newPassword);
            await _repo.UpdatePasswordHashAsync(userId, newHash);
            await RevokeSessionsAsync(userId, "password changed");
        }

        /// <inheritdoc/>
        public async Task<bool> VerifyPasswordAsync(string plainPassword, string? passwordHash)
        {
            if (string.IsNullOrEmpty(passwordHash))
            {
                return false;
            }

            return await _passwordService.VerifyPasswordAsync(plainPassword, passwordHash);
        }

        /// <inheritdoc/>
        public async Task UpdateEmailAsync(int userId, string newEmail, string? currentPassword = null)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Invalid user ID.", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(newEmail))
            {
                throw new ArgumentException("Email is required.", nameof(newEmail));
            }

            if (!IsValidEmail(newEmail))
            {
                throw new ArgumentException("Invalid email format.", nameof(newEmail));
            }

            var user = await _repo.GetByIdAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID {userId} not found.");
            }

            // Verify current password if provided and user has password set
            if (!string.IsNullOrEmpty(currentPassword) && !string.IsNullOrEmpty(user.PasswordHash))
            {
                var isValidPassword = await _passwordService.VerifyPasswordAsync(currentPassword, user.PasswordHash);
                if (!isValidPassword)
                {
                    throw new UnauthorizedAccessException("Current password is incorrect.");
                }
            }

            // Check email uniqueness
            var (emailTaken, _) = await CheckEmailTakenAsync(newEmail, userId);
            if (emailTaken)
            {
                throw new InvalidOperationException("Email is already registered to another account.");
            }

            await _repo.UpdateEmailAsync(userId, newEmail);
            await RevokeSessionsAsync(userId, "email changed");
        }

        // ===== NEW METHODS: BALANCES (COINS, GEMS, XP) =====

        /// <inheritdoc/>
        public async Task<BalanceAdjustmentResultDto> AdjustBalancesAsync(int userId, IReadOnlyList<BalanceChangeDto> changes, CurrencyContext ctx, string? metadata = null, bool notifyPlayer = true)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Invalid user ID.", nameof(userId));
            }
            if (ctx == null)
            {
                throw new ArgumentNullException(nameof(ctx));
            }
            if (string.IsNullOrWhiteSpace(ctx.Reason))
            {
                throw new ArgumentException("Reason is required for balance adjustments.", "reason");
            }
            if (string.IsNullOrEmpty(ctx.IdempotencyKey) || ctx.IdempotencyKey.Length > CurrencyClientKeys.MaxLength)
            {
                throw new ArgumentException($"An Idempotency-Key of 1–{CurrencyClientKeys.MaxLength} characters is required.", nameof(ctx));
            }
            if (changes == null || changes.Count == 0)
            {
                throw new ArgumentException("At least one balance change is required.", nameof(changes));
            }
            if (changes.Any(c => c == null || !Enum.IsDefined(c.Currency) || !Enum.IsDefined(c.Mode)))
            {
                throw new ArgumentException("Each change needs a currency (Coins, Gems, Experience) and a mode (Add, Remove, Set).", nameof(changes));
            }
            if (changes.GroupBy(c => c.Currency).Any(g => g.Count() > 1))
            {
                throw new ArgumentException("Each currency may appear only once.", nameof(changes));
            }

            User user = null!;
            var posted = new List<(BalanceChangeDto Change, PostingResult Posting)>();
            TitleChangeResultDto? titleChange = null;

            // One transaction with the user's row locked (DESIGN.md §1.4 A2): the postings, any
            // title bonuses and the audit entry commit together or not at all. XP goes last, so
            // title progression runs once on the final XP. The acting staff member's row is locked
            // too (ascending with the player's), as the ledger does for the per-staff grant cap.
            var lockIds = ctx.InitiatorUserId is > 0 ? new[] { userId, ctx.InitiatorUserId.Value } : new[] { userId };
            await _repo.RunWithUsersLockedAsync(lockIds, async () =>
            {
                user = await _repo.GetByIdAsync(userId)
                    ?? throw new KeyNotFoundException($"User with ID {userId} not found.");
                var coinsBefore = user.Coins;
                var gemsBefore = user.Gems;
                var experienceBefore = user.ExperiencePoints;

                foreach (var change in changes.OrderBy(c => c.Currency))
                {
                    var posting = await _currency.AdminAdjustAsync(
                        new AdminAdjustRequest(userId, change.Currency, change.Mode, change.Amount, change.ExpectedCurrent),
                        ctx with
                        {
                            ReasonCode = CurrencyReasons.ForAdminMode(change.Mode),
                            IdempotencyKey = $"{ctx.IdempotencyKey}:{KeySuffix(change.Currency)}",
                            SourceType = ctx.SourceType ?? "User",
                            SourceRef = ctx.SourceRef ?? userId.ToString()
                        });
                    posted.Add((change, posting));
                }

                var xpPosting = posted.FirstOrDefault(p => p.Change.Currency == Currency.Experience).Posting;
                if (xpPosting != null)
                {
                    var titleChanges = await _titleProgression.ApplyForPostingAsync(xpPosting, ctx.InitiatorUserId);
                    titleChange = titleChanges.GetValueOrDefault(userId);
                }

                // A full replay (same Idempotency-Key retried) posted nothing, and its audit entry
                // was written the first time.
                if (posted.All(p => p.Posting.Replayed))
                {
                    return;
                }

                // docs/specs/user-management/IMPLEMENTATION_PLAN.md §0: every balance mutation
                // writes an AuditLogEntry. The ledger is authoritative; this entry keeps the web
                // activity feed (knk-web-app utils/auditDetails.ts) and links to the ledger rows.
                long Delta(Currency c) => posted.Where(p => p.Change.Currency == c).SelectMany(p => p.Posting.Entries).Sum(e => e.Amount);
                await _auditLogService.RecordAsync(ctx.InitiatorUserId, userId, AuditAction.BalanceAdjusted, JsonSerializer.Serialize(new
                {
                    coinsDelta = Delta(Currency.Coins),
                    gemsDelta = Delta(Currency.Gems),
                    experienceDelta = Delta(Currency.Experience),
                    reason = ctx.Reason,
                    metadata,
                    changes = posted.Select(p => new { currency = p.Change.Currency.ToString(), mode = p.Change.Mode.ToString(), amount = p.Change.Amount }),
                    ledgerTransactionIds = posted.Select(p => p.Posting.PublicId),
                    titleBonusCoins = titleChange?.CoinBonusGranted ?? 0,
                    titleBonusGems = titleChange?.GemBonusGranted ?? 0,
                    titleBonusExp = titleChange?.ExpBonusGranted ?? 0,
                    // Before/after (bonuses included) for the moderation activity feed.
                    coinsBefore,
                    coinsAfter = user.Coins,
                    gemsBefore,
                    gemsAfter = user.Gems,
                    experienceBefore,
                    experienceAfter = user.ExperiencePoints
                }));
            });

            // Only after the commit: the plugin must not announce a promotion that rolled back.
            if (titleChange != null && notifyPlayer)
            {
                // The result below only reaches this request's caller. When that's the web app,
                // the plugin would otherwise never learn of the change, so the player never got
                // the in-game promotion moment - queue it for the plugin's poller to deliver.
                _notificationQueue?.Enqueue(userId, user.Uuid, user.Username, PlayerNotificationTypes.TitleChanged, titleChange);
            }

            var balances = await _currency.GetBalancesAsync(userId);
            return new BalanceAdjustmentResultDto
            {
                NewCoins = balances.Coins,
                NewGems = balances.Gems,
                NewExperiencePoints = balances.ExperiencePoints,
                TitleChange = titleChange,
                Replayed = posted.All(p => p.Posting.Replayed),
                Changes = posted.SelectMany(p => p.Posting.Entries.Select(e => new BalanceChangeResultDto
                {
                    Currency = e.Currency,
                    Mode = p.Change.Mode.ToString(),
                    Amount = e.Amount,
                    BalanceBefore = e.BalanceBefore,
                    BalanceAfter = e.BalanceAfter,
                    TransactionPublicId = p.Posting.PublicId,
                    Replayed = p.Posting.Replayed
                })).ToList()
            };
        }

        /// <inheritdoc/>
        public async Task<TitleChangeResultDto?> ApplyTitleProgressionAsync(int userId, int previousExperience, string reason, string? metadata = null, int? actorUserId = null, bool notifyPlayer = true)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Invalid user ID.", nameof(userId));
            }
            var change = await _titleProgression.ApplyAsync(userId, previousExperience, actorUserId);
            if (change != null && notifyPlayer)
            {
                var user = await _repo.GetByIdAsync(userId);
                if (user != null)
                {
                    _notificationQueue?.Enqueue(userId, user.Uuid, user.Username, PlayerNotificationTypes.TitleChanged, change);
                }
            }
            return change;
        }

        private static string KeySuffix(Currency currency) => currency switch
        {
            Currency.Coins => "coins",
            Currency.Gems => "gems",
            _ => "xp"
        };

        // ===== NEW METHODS: LINK CODES =====

        /// <inheritdoc/>
        public async Task<LinkCodeResponseDto> GenerateLinkCodeAsync(int? userId)
        {
            if (userId.HasValue)
            {
                var user = await _repo.GetByIdAsync(userId.Value);
                if (user == null)
                {
                    throw new KeyNotFoundException($"User with ID {userId} not found.");
                }
            }

            return await _linkCodeService.GenerateLinkCodeAsync(userId);
        }

        /// <inheritdoc/>
        public async Task<(bool IsValid, UserDto? User)> ValidateLinkCodeAsync(string code)
        {
            var (isValid, linkCode, _) = await _linkCodeService.ValidateLinkCodeAsync(code);

            if (!isValid || linkCode == null)
            {
                return (false, null);
            }

            if (linkCode.UserId.HasValue)
            {
                var user = await _repo.GetByIdAsync(linkCode.UserId.Value);
                if (user != null)
                {
                    return (true, await MapToUserDtoAsync(user));
                }
            }

            return (false, null);
        }

        /// <inheritdoc/>
        public async Task<(bool IsValid, UserDto? User)> ConsumeLinkCodeAsync(string code)
        {
            var (success, linkCode, error) = await _linkCodeService.ConsumeLinkCodeAsync(code);

            if (!success || linkCode == null)
            {
                return (false, null);
            }

            if (linkCode.UserId.HasValue)
            {
                var user = await _repo.GetByIdAsync(linkCode.UserId.Value);
                if (user != null)
                {
                    return (true, await MapToUserDtoAsync(user));
                }
            }

            return (false, null);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<LinkCode>> GetExpiredLinkCodesAsync()
        {
            return await _linkCodeService.GetExpiredCodesAsync();
        }

        /// <inheritdoc/>
        public async Task<int> CleanupExpiredLinksAsync()
        {
            return await _linkCodeService.CleanupExpiredCodesAsync();
        }

        // ===== NEW METHODS: MERGING & LINKING =====

        /// <inheritdoc/>
        public async Task<(bool HasConflict, int? SecondaryUserId)> CheckForDuplicateAsync(string uuid, string username)
        {
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(username))
            {
                return (false, null);
            }

            var duplicate = await _repo.FindDuplicateAsync(uuid, username);
            if (duplicate != null)
            {
                return (true, duplicate.Id);
            }

            return (false, null);
        }

        /// <inheritdoc/>
        public async Task<UserDto> MergeAccountsAsync(int primaryUserId, int secondaryUserId)
        {
            if (primaryUserId <= 0)
            {
                throw new ArgumentException("Invalid primary user ID.", nameof(primaryUserId));
            }

            if (secondaryUserId <= 0)
            {
                throw new ArgumentException("Invalid secondary user ID.", nameof(secondaryUserId));
            }

            if (primaryUserId == secondaryUserId)
            {
                throw new ArgumentException("Cannot merge a user with itself.");
            }

            var primaryUser = await _repo.GetByIdAsync(primaryUserId);
            if (primaryUser == null)
            {
                throw new KeyNotFoundException($"Primary user with ID {primaryUserId} not found.");
            }

            var secondaryUser = await _repo.GetByIdAsync(secondaryUserId);
            if (secondaryUser == null)
            {
                throw new KeyNotFoundException($"Secondary user with ID {secondaryUserId} not found.");
            }

            // Perform merge (repository handles soft delete and data preservation)
            await MergeWithForfeitAsync(primaryUserId, secondaryUserId);

            // Return updated primary user
            var mergedUser = await _repo.GetByIdAsync(primaryUserId);
            return await MapToUserDtoAsync(mergedUser!);
        }

        /// <summary>
        /// Soft-deletes the secondary account; the surviving (primary) account ends with the
        /// higher of the two balances of each currency - coins, gems and XP separately (developer
        /// decision, KNG-21 smoke test; supersedes the forfeit-only rule of DESIGN.md §5 Q6). In
        /// the ledger: one MERGE_FORFEIT posting zeroes the secondary's full balances (key
        /// <c>merge:{secondaryId}</c>), and one MERGE_CARRYOVER posting credits the survivor with
        /// secondary − primary wherever that is positive (key <c>merge-carry:{secondaryId}</c>).
        /// An XP carry-over runs title progression once; a bracket's bonus already paid to either
        /// account is not paid again (once per bracket, ever: the forfeit posted first puts the
        /// secondary among the survivor's merged accounts, whose bonuses count as paid). Both rows are locked; the
        /// postings, the bonuses and the soft delete commit together.
        /// </summary>
        private async Task MergeWithForfeitAsync(int primaryUserId, int secondaryUserId)
        {
            User primary = null!;
            TitleChangeResultDto? titleChange = null;
            await _repo.RunWithUsersLockedAsync(new[] { primaryUserId, secondaryUserId }, async () =>
            {
                primary = await _repo.GetByIdAsync(primaryUserId)
                    ?? throw new KeyNotFoundException($"Primary user with ID {primaryUserId} not found.");
                var secondary = await _repo.GetByIdAsync(secondaryUserId)
                    ?? throw new KeyNotFoundException($"Secondary user with ID {secondaryUserId} not found.");
                var balances = new[]
                {
                    (Currency: Currency.Coins, Primary: (long)primary.Coins, Secondary: (long)secondary.Coins),
                    (Currency: Currency.Gems, Primary: (long)primary.Gems, Secondary: (long)secondary.Gems),
                    (Currency: Currency.Experience, Primary: (long)primary.ExperiencePoints, Secondary: (long)secondary.ExperiencePoints)
                };

                var forfeit = balances.Where(b => b.Secondary > 0)
                    .Select(b => new CurrencyLeg(secondaryUserId, b.Currency, -b.Secondary)).ToList();
                if (forfeit.Count > 0)
                {
                    await _currency.PostAsync(forfeit, CurrencyContext.ForSystem("UserService", CurrencyReasons.MergeForfeit,
                        CurrencyReasons.MergeForfeitKey(secondaryUserId), $"Account merged into user {primaryUserId}") with
                    {
                        SourceType = "User",
                        SourceRef = primaryUserId.ToString()
                    });
                }

                var carry = balances.Where(b => b.Secondary > b.Primary)
                    .Select(b => new CurrencyLeg(primaryUserId, b.Currency, b.Secondary - b.Primary)).ToList();
                if (carry.Count > 0)
                {
                    var posting = await _currency.PostAsync(carry, CurrencyContext.ForSystem("UserService", CurrencyReasons.MergeCarryover,
                        CurrencyReasons.MergeCarryoverKey(secondaryUserId), $"Higher balance kept from merged user {secondaryUserId}") with
                    {
                        SourceType = "User",
                        SourceRef = secondaryUserId.ToString()
                    });
                    titleChange = (await _titleProgression.ApplyForPostingAsync(posting, null)).GetValueOrDefault(primaryUserId);
                }
                await _repo.MergeUsersAsync(primaryUserId, secondaryUserId);
            });
            // The merged-away account is soft-deleted: its sessions end.
            await RevokeSessionsAsync(secondaryUserId, "merged into user " + primaryUserId);

            // Only after the commit, as for a staff XP change.
            if (titleChange != null)
            {
                _notificationQueue?.Enqueue(primaryUserId, primary.Uuid, primary.Username, PlayerNotificationTypes.TitleChanged, titleChange);
            }
            _realtime.UserChanged(primary, new[] { UserChangedFields.Merge });
        }

        // ===== HELPER METHODS =====

        /// <inheritdoc/>
        public async Task<LinkMinecraftAccountResult> LinkMinecraftAccountAsync(int userId, string linkCode)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Invalid user ID.", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(linkCode))
            {
                throw new ArgumentException("Link code is required.", nameof(linkCode));
            }

            // KNG-208: after Security:LinkAttempts:MaxFailures wrong codes the account waits, and
            // the code isn't even looked up.
            if (_linkAttempts != null && _linkAttempts.IsLocked(userId, out var remaining))
            {
                throw new LinkAttemptsExceededException(remaining);
            }

            var webAppUser = await _repo.GetByIdAsync(userId);
            if (webAppUser == null)
            {
                throw new KeyNotFoundException($"Web app user with ID {userId} not found");
            }

            // Step 1: validate the code without consuming it, and find the Minecraft identity it stands for.
            var (isCodeValid, code, _) = await _linkCodeService.ValidateLinkCodeAsync(linkCode);
            var (uuid, verifiedName, minecraftUser) = isCodeValid && code != null
                ? await ResolveLinkedIdentityAsync(code)
                : (null, null, null);
            if (string.IsNullOrWhiteSpace(uuid))
            {
                _linkAttempts?.RecordFailure(userId);
                throw new InvalidOperationException("Invalid or expired link code");
            }

            // KNG-214: a claim code (the admission gate's) only links to an admitted web account,
            // invited or grandfathered, so it can't be used to get in without an invitation.
            var viaClaim = code!.Purpose == LinkCodePurpose.Claim;
            if (viaClaim && !webAppUser.AdmittedAt.HasValue)
            {
                throw new LinkForbiddenException(LinkForbiddenException.InviteRequired,
                    "You need an invitation before you can link a Minecraft account.");
            }

            // Step 2: the caller's own state (DESIGN § 5.4).
            if (!string.IsNullOrWhiteSpace(webAppUser.Uuid))
            {
                if (webAppUser.Uuid != uuid)
                {
                    throw new LinkConflictException(LinkConflictException.AlreadyLinked,
                        "Your web account is already linked to another Minecraft account. Changing it is a staff action.");
                }

                // Already linked to exactly this account: nothing to change, the code is spent.
                await ConsumeOrThrowAsync(linkCode);
                _linkAttempts?.Reset(userId);
                return new LinkMinecraftAccountResult(await MapToUserDtoAsync(webAppUser), Merged: false);
            }

            if (minecraftUser != null && minecraftUser.Id != webAppUser.Id)
            {
                if (!minecraftUser.IsActive || minecraftUser.DeletedAt.HasValue)
                {
                    _linkAttempts?.RecordFailure(userId);
                    throw new InvalidOperationException("Invalid or expired link code");
                }
                if (!string.IsNullOrEmpty(minecraftUser.PasswordHash))
                {
                    throw new LinkConflictException(LinkConflictException.AlreadyRegistered,
                        "This Minecraft account already has a web account. Log in with it, or use Forgot password.");
                }
            }

            // Step 3: the code is spent before anything changes, so two requests with the same code
            // can't both get here (TryMarkUsedAsync is a conditional write).
            await ConsumeOrThrowAsync(linkCode);
            _linkAttempts?.Reset(userId);

            if (minecraftUser == null)
            {
                // Case A: no account has this UUID (only claim codes get here, KNG-214). The web
                // account becomes the Minecraft account; its name is the Mojang-verified one. One
                // transaction: identity, admission, the starting balance it never got as a web
                // shell (SIGNUP_GRANT, once-only key signup:{id}), the audit entry and the mail.
                await _repo.RunWithUsersLockedAsync(new[] { webAppUser.Id }, async () =>
                {
                    if (!string.IsNullOrWhiteSpace(verifiedName) && !string.Equals(webAppUser.Username, verifiedName, StringComparison.OrdinalIgnoreCase))
                    {
                        await ReleaseUsernameFromWebOnlyAccountAsync(verifiedName, userId);
                        webAppUser.Username = verifiedName;
                    }
                    webAppUser.Uuid = uuid;
                    webAppUser.AdmittedAt ??= DateTime.UtcNow;
                    await _repo.UpdateUserAsync(webAppUser);
                    await GrantSignupBalanceAsync(webAppUser.Id);
                    await RecordLinkedAsync(webAppUser, webAppUser.Id, "A", viaClaim);
                });
                _logger.LogInformation("Web account {UserId} linked to Minecraft UUID {Uuid} (case A)", webAppUser.Id, uuid);
                var linked = await _repo.GetByIdAsync(webAppUser.Id);
                NotifyLinked(linked!);
                return new LinkMinecraftAccountResult(await MapToUserDtoAsync(linked!), Merged: false);
            }

            // Case B: fold the web account into the Minecraft account, which keeps its id, stats,
            // groups and history (decision I7, KNG-207).
            await FoldWebAccountIntoMinecraftAccountAsync(minecraftUser.Id, webAppUser.Id);
            var survivor = await _repo.GetByIdAsync(minecraftUser.Id);
            await RecordLinkedAsync(survivor!, webAppUser.Id, "B", viaClaim);
            NotifyLinked(survivor!);
            return new LinkMinecraftAccountResult(await MapToUserDtoAsync(survivor!), Merged: true);
        }

        /// <summary>
        /// Audit MinecraftLinked (target = the account that now holds both) and queue the
        /// MinecraftLinked mail to its address (KNG-214, DESIGN § 5.5: the owner hears of every link).
        /// </summary>
        private async Task RecordLinkedAsync(User account, int webUserId, string linkCase, bool viaClaim)
        {
            await _auditLogService.RecordAsync(webUserId, account.Id, AuditAction.MinecraftLinked, JsonSerializer.Serialize(new
            {
                uuid = account.Uuid,
                minecraftName = account.Username,
                webUserId,
                linkCase,
                via = viaClaim ? "claim" : "link"
            }));
            if (_email != null && !string.IsNullOrWhiteSpace(account.Email))
            {
                await _email.QueueAsync(EmailKind.MinecraftLinked, account.Email!,
                    new { username = account.Username, minecraftName = account.Username },
                    $"minecraft-linked:{account.Id}:{account.Uuid}", userId: account.Id);
            }
        }

        /// <summary>The in-game notice of a new link (AccountLinked), with the web login's address masked.
        /// Queued after the change is saved; the plugin shows it on the player's next join.</summary>
        private void NotifyLinked(User account)
        {
            _realtime.UserChanged(account, new[] { UserChangedFields.Link }, account.Id);
            _notificationQueue?.EnqueueAccountLinked(account.Id, account.Uuid, account.Username,
                new AccountLinkedNotificationDto { MaskedEmail = EmailMask.Mask(account.Email) });
        }

        /// <inheritdoc/>
        public async Task<bool> IsUuidAdmittedAsync(string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid))
            {
                return false;
            }
            var user = await _repo.GetByUuidAsync(uuid.Trim().ToLowerInvariant());
            return user != null && user.IsActive && !user.DeletedAt.HasValue && user.AdmittedAt.HasValue;
        }

        /// <inheritdoc/>
        public async Task<(UserDto User, bool Changed)> AdmitAsync(int userId, int? actorUserId)
        {
            var user = await _repo.GetByIdAsync(userId) ?? throw new KeyNotFoundException($"User with id {userId} not found.");
            if (user.DeletedAt.HasValue)
            {
                throw new KeyNotFoundException($"User with id {userId} not found.");
            }
            if (user.AdmittedAt.HasValue)
            {
                return (await MapToUserDtoAsync(user), false);
            }

            user.AdmittedAt = DateTime.UtcNow;
            await _repo.UpdateUserAsync(user);
            await _auditLogService.RecordAsync(actorUserId, user.Id, AuditAction.PlayerAdmitted, JsonSerializer.Serialize(new
            {
                uuid = user.Uuid,
                username = user.Username,
                active = user.IsActive
            }));
            _logger.LogInformation("User {UserId} admitted by staff {ActorId}", user.Id, actorUserId);
            _accessSync?.RequestSync();
            return (await MapToUserDtoAsync(user), true);
        }

        /// <inheritdoc/>
        public async Task<UserDto> DetachWebLoginAsync(int userId, int? actorUserId)
        {
            var user = await _repo.GetByIdAsync(userId) ?? throw new KeyNotFoundException($"User with id {userId} not found.");
            if (user.DeletedAt.HasValue)
            {
                throw new KeyNotFoundException($"User with id {userId} not found.");
            }
            if (string.IsNullOrWhiteSpace(user.Uuid))
            {
                // A web-only account has nothing but its login; detaching would leave an empty row.
                throw new LinkConflictException(LinkConflictException.NotLinked,
                    "This account has no Minecraft account; there is nothing to detach the web login from.");
            }
            if (string.IsNullOrEmpty(user.Email) && string.IsNullOrEmpty(user.PasswordHash))
            {
                throw new LinkConflictException(LinkConflictException.NoWebLogin, "This account has no web login.");
            }

            var maskedEmail = EmailMask.Mask(user.Email);
            user.Email = null;
            user.PasswordHash = null;
            user.EmailVerified = false;
            user.EmailVerifiedAt = null;
            user.LastPasswordChangeAt = null;
            await _repo.UpdateUserAsync(user);
            await RevokeSessionsAsync(user.Id, "web login detached by staff");
            await _auditLogService.RecordAsync(actorUserId, user.Id, AuditAction.MinecraftUnlinked, JsonSerializer.Serialize(new
            {
                uuid = user.Uuid,
                username = user.Username,
                email = maskedEmail
            }));
            _logger.LogWarning("Web login of user {UserId} detached by staff {ActorId}", user.Id, actorUserId);
            return await MapToUserDtoAsync(user);
        }

        /// <summary>
        /// The Minecraft identity a validated code stands for: a link code names the account that
        /// ran /account link, a claim code carries the UUID the admission gate saw (KNG-214).
        /// Reset-token hashes and codes of web-only accounts resolve to nothing.
        /// </summary>
        private async Task<(string? Uuid, string? Name, User? MinecraftUser)> ResolveLinkedIdentityAsync(LinkCode code)
        {
            switch (code.Purpose)
            {
                case LinkCodePurpose.Link:
                    if (!code.UserId.HasValue)
                    {
                        return (null, null, null);
                    }
                    var owner = await _repo.GetByIdAsync(code.UserId.Value);
                    return owner == null || string.IsNullOrWhiteSpace(owner.Uuid)
                        ? (null, null, null)
                        : (owner.Uuid, owner.Username, owner);
                case LinkCodePurpose.Claim:
                    if (string.IsNullOrWhiteSpace(code.MinecraftUuid))
                    {
                        return (null, null, null);
                    }
                    var holder = await _repo.GetByUuidAsync(code.MinecraftUuid);
                    return (code.MinecraftUuid, code.MinecraftName ?? holder?.Username, holder);
                default:
                    return (null, null, null);
            }
        }

        private async Task ConsumeOrThrowAsync(string linkCode)
        {
            var (consumed, _, _) = await _linkCodeService.ConsumeLinkCodeAsync(linkCode);
            if (!consumed)
            {
                throw new InvalidOperationException("Invalid or expired link code");
            }
        }

        /// <summary>
        /// Case B of DESIGN § 5.4, in one transaction with both rows locked: the balance merge
        /// (MergeWithForfeitAsync: forfeit + carry-over postings, soft delete of the shell), then
        /// the shell's email is cleared and saved first because users.Email is unique, then the web
        /// login (email, password hash, verification and change stamps) moves to the Minecraft
        /// account. The shell's sessions are revoked by the merge. Invite fields follow in KNG-212,
        /// when they exist.
        /// </summary>
        private async Task FoldWebAccountIntoMinecraftAccountAsync(int minecraftUserId, int webUserId)
        {
            await _repo.RunWithUsersLockedAsync(new[] { minecraftUserId, webUserId }, async () =>
            {
                await MergeWithForfeitAsync(minecraftUserId, webUserId);

                var shell = await _repo.GetByIdAsync(webUserId)
                    ?? throw new KeyNotFoundException($"Web app user with ID {webUserId} not found");
                var minecraft = await _repo.GetByIdAsync(minecraftUserId)
                    ?? throw new KeyNotFoundException($"Minecraft account with ID {minecraftUserId} not found");

                var email = shell.Email;
                var passwordHash = shell.PasswordHash;
                var lastPasswordChangeAt = shell.LastPasswordChangeAt;
                var lastEmailChangeAt = shell.LastEmailChangeAt;
                var emailVerified = shell.EmailVerified;
                var emailVerifiedAt = shell.EmailVerifiedAt;

                shell.Email = null;
                shell.PasswordHash = null;
                shell.EmailVerified = false;
                shell.EmailVerifiedAt = null;
                await _repo.UpdateUserAsync(shell);

                minecraft.Email = email;
                minecraft.PasswordHash = passwordHash;
                minecraft.LastPasswordChangeAt = lastPasswordChangeAt;
                minecraft.LastEmailChangeAt = lastEmailChangeAt;
                minecraft.EmailVerified = emailVerified;
                minecraft.EmailVerifiedAt = emailVerifiedAt;
                // KNG-212 (L1-2): the invite the shell was created with, and the admission it brought.
                minecraft.InvitedViaInvitationId ??= shell.InvitedViaInvitationId;
                minecraft.InvitedByUserId ??= shell.InvitedByUserId;
                minecraft.AdmittedAt ??= shell.AdmittedAt;
                await _repo.UpdateUserAsync(minecraft);

                // The invite's group follows the login; Default the Minecraft account has already.
                if (_invitations != null && shell.InvitedViaInvitationId.HasValue)
                {
                    var groupId = (await _invitations.GetByIdAsync(shell.InvitedViaInvitationId.Value))?.GrantGroupId;
                    if (groupId.HasValue)
                    {
                        await _membershipService.UpsertAsync(new UpsertUserPermissionGroupDto
                        {
                            UserId = minecraftUserId,
                            PermissionGroupId = groupId.Value,
                            ExpiresAt = null
                        });
                    }
                }
            });

            _logger.LogInformation("Web account {ShellId} folded into Minecraft account {UserId} (case B, KNG-207)", webUserId, minecraftUserId);
        }

        /// <inheritdoc/>
        public Task<T> RunInTransactionAsync<T>(Func<Task<T>> work, Func<T, bool> shouldCommit)
            => _repo.RunInTransactionAsync(work, shouldCommit);

        /// <inheritdoc/>
        public async Task<int?> ReleaseUsernameFromWebOnlyAccountAsync(string username, int? actorUserId = null)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var holder = await _repo.GetByUsernameAsync(username);
            if (holder == null || !string.IsNullOrEmpty(holder.Uuid))
            {
                return null;
            }

            var oldUsername = holder.Username;
            holder.Username = $"unclaimed-{holder.Id}";
            await _repo.UpdateUserAsync(holder);
            // The caller creates the Minecraft account next in the same request, and creating it
            // loads the Default PermissionGroup; a still-tracked User in the shared
            // permission_holders key space made that query throw InvalidCastException.
            _repo.Detach(holder);
            await _auditLogService.RecordAsync(actorUserId, holder.Id, AuditAction.UsernameReleased, JsonSerializer.Serialize(new
            {
                oldUsername,
                newUsername = holder.Username,
                reason = "The Minecraft player with this name joined; web-only accounts can't hold a Minecraft name."
            }));
            _logger.LogWarning("Released username {Username} from web-only account {UserId} (now {NewUsername})",
                oldUsername, holder.Id, holder.Username);
            return holder.Id;
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

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
        private readonly IPlayerNotificationQueue? _notificationQueue;

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
            IPlayerNotificationQueue? notificationQueue = null)
        {
            _notificationQueue = notificationQueue;
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
            }
            return dto;
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
            
            await _repo.AddUserAsync(user);
            await AssignDefaultGroupAsync(user.Id);
            return await MapToUserDtoAsync(user);
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

            // Apply all editable UserDto fields onto the tracked entity. The mapping profile
            // (UserMappingProfile: UserDto -> User) ignores fields that must never be set from
            // this endpoint: credentials and soft-delete fields, service-managed state
            // (ActiveMode, presence, LastSalaryPayoutAt) and, since KNG-22, Coins, Gems,
            // ExperiencePoints and the personal multipliers. Those change only through
            // PUT {id}/balances and PUT {id}/multipliers, which lock, validate and audit them
            // (docs/specs/currency-payments/DESIGN.md §1.4 A1).
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

            await _repo.UpdateGatePassThroughMethodAsync(id, method);
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
        }

        // ===== NEW METHODS: BALANCES (COINS, GEMS, XP) =====

        /// <inheritdoc/>
        public async Task<BalanceAdjustmentResultDto> AdjustBalancesAsync(int userId, int coinsDelta, int gemsDelta, int experienceDelta, string reason, string? metadata = null, int? actorUserId = null, bool notifyPlayer = true)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Invalid user ID.", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Reason is required for balance adjustments.", nameof(reason));
            }

            User user = null!;
            TitleChangeResultDto? titleChange = null;

            // One transaction with the user's row locked (DESIGN.md §1.4 A2): two adjustments, or
            // an adjustment racing a salary payout or kit purchase, run one after the other
            // instead of both starting from the same balance. The audit entry is written in the
            // same transaction, so there is never a balance change without its entry.
            await _repo.RunWithUsersLockedAsync(new[] { userId }, async () =>
            {
                user = await _repo.GetByIdAsync(userId)
                    ?? throw new KeyNotFoundException($"User with ID {userId} not found.");
                titleChange = await ApplyBalanceAdjustmentAsync(user, coinsDelta, gemsDelta, experienceDelta, reason, metadata, actorUserId);
            });

            // Only after the commit: the plugin must not announce a promotion that rolled back.
            if (titleChange != null && notifyPlayer)
            {
                // The result below only reaches this request's caller. When that's the web app,
                // the plugin would otherwise never learn of the change, so the player never got
                // the in-game promotion moment - queue it for the plugin's poller to deliver.
                _notificationQueue?.Enqueue(userId, user.Uuid, user.Username, PlayerNotificationTypes.TitleChanged, titleChange);
            }

            return new BalanceAdjustmentResultDto
            {
                NewCoins = user.Coins,
                NewGems = user.Gems,
                NewExperiencePoints = user.ExperiencePoints,
                TitleChange = titleChange
            };
        }

        /// <summary>
        /// The body of AdjustBalancesAsync, run under the user's row lock: validates and applies
        /// the deltas and any title-promotion bonuses, saves, and writes the audit entries.
        /// Every new value is computed and checked (BalanceLimits: no negatives, caps, checked
        /// arithmetic) before anything is assigned, so a rejected adjustment changes nothing.
        /// </summary>
        private async Task<TitleChangeResultDto?> ApplyBalanceAdjustmentAsync(User user, int coinsDelta, int gemsDelta, int experienceDelta, string reason, string? metadata, int? actorUserId)
        {
            var userId = user.Id;
            var originalExperience = user.ExperiencePoints;
            var originalCoins = user.Coins;
            var originalGems = user.Gems;

            // Rejects underflow ("Insufficient …") and anything above the caps.
            var newCoins = BalanceLimits.ApplyCoins(originalCoins, coinsDelta);
            var newGems = BalanceLimits.ApplyGems(originalGems, gemsDelta);
            var newExperience = BalanceLimits.ApplyExperience(originalExperience, experienceDelta);

            // Resolved before the mutation so a resulting title change can be detected, and so the
            // consolidation loop below has every bracket to walk between old and new XP.
            var brackets = experienceDelta != 0 ? await _titleService.GetAllOrderedAsync() : null;
            TitleBracket? previousBracket = brackets != null && brackets.Count > 0
                ? (brackets.LastOrDefault(b => b.MinExperience <= originalExperience) ?? brackets[0])
                : null;

            TitleChangeResultDto? titleChange = null;

            // Consolidate every bracket crossed by this single adjustment into one grant + one
            // reported change, instead of firing once per tier the way v1's TitleChangeEvents
            // loop did (setPromoteLoop/setDemoteLoop) — a developer-confirmed behavior NOT to
            // repeat. ExpBonus can itself push into a further bracket, so this loops until
            // resolution stabilizes, mirroring v1's cascading re-check but accumulating instead
            // of firing per-iteration effects.
            if (previousBracket != null && brackets != null)
            {
                var direction = newExperience > originalExperience ? "promotion" : "demotion";
                var currentBracket = brackets.LastOrDefault(b => b.MinExperience <= newExperience) ?? brackets[0];

                if (currentBracket.Id != previousBracket.Id)
                {
                    var crossed = new List<TitleBracket>();
                    long coinBonusTotal = 0, gemBonusTotal = 0, expBonusTotal = 0;
                    int coinBonusBase = 0, gemBonusBase = 0, expBonusBase = 0;
                    var coinMultipliers = new List<RewardMultiplierDto>();
                    var gemMultipliers = new List<RewardMultiplierDto>();
                    var expMultipliers = new List<RewardMultiplierDto>();

                    if (direction == "promotion")
                    {
                        // KNG-16: each bonus is scaled by the player's personal x rank multiplier
                        // for that currency - coins by the salary multipliers, gems and XP by their
                        // own GemBonus/ExpBonus multipliers. No global multiplier applies.
                        var ranks = await _membershipService.GetActiveRankMultipliersAsync(userId) ?? RankMultipliersDto.Neutral;
                        var coinMultiplier = user.PersonalSalaryMultiplier * ranks.Salary;
                        var gemMultiplier = user.PersonalGemBonusMultiplier * ranks.GemBonus;
                        var expMultiplier = user.PersonalExpBonusMultiplier * ranks.ExpBonus;
                        coinMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalSalaryMultiplier));
                        coinMultipliers.AddRange(ranks.SalaryBreakdown());
                        gemMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalGemBonusMultiplier));
                        gemMultipliers.AddRange(ranks.GemBonusBreakdown());
                        expMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalExpBonusMultiplier));
                        expMultipliers.AddRange(ranks.ExpBonusBreakdown());

                        // Walk every bracket strictly above previousBracket up to (and possibly
                        // past, if ExpBonus pushes further) currentBracket, accumulating rewards.
                        var idx = brackets.FindIndex(b => b.Id == previousBracket.Id) + 1;
                        while (idx < brackets.Count && brackets[idx].MinExperience <= newExperience)
                        {
                            var tier = brackets[idx];
                            crossed.Add(tier);
                            var expBonus = ScaleBonus(tier.ExpBonus, expMultiplier);
                            coinBonusTotal = checked(coinBonusTotal + ScaleBonus(tier.CoinBonus, coinMultiplier));
                            gemBonusTotal = checked(gemBonusTotal + ScaleBonus(tier.GemBonus, gemMultiplier));
                            expBonusTotal = checked(expBonusTotal + expBonus);
                            coinBonusBase += tier.CoinBonus;
                            gemBonusBase += tier.GemBonus;
                            expBonusBase += tier.ExpBonus;
                            newExperience = BalanceLimits.ApplyExperience(newExperience, expBonus); // may unlock further brackets
                            idx++;
                        }
                        newCoins = BalanceLimits.ApplyCoins(newCoins, coinBonusTotal);
                        newGems = BalanceLimits.ApplyGems(newGems, gemBonusTotal);
                        currentBracket = brackets.LastOrDefault(b => b.MinExperience <= newExperience) ?? brackets[0];
                    }
                    // Demotion never claws back currency (matches v1's userDemotion, which only
                    // ever removed structural slots/skills — neither exists in v3), so no bonus
                    // accumulation happens on the way down.

                    titleChange = new TitleChangeResultDto
                    {
                        Direction = direction,
                        FromTitleBracketId = previousBracket.Id,
                        FromTitleName = previousBracket.NameFor(user.Gender),
                        ToTitleBracketId = currentBracket.Id,
                        ToTitleName = currentBracket.NameFor(user.Gender),
                        CrossedTitles = crossed.Select(t => new TitleCrossingDto { TitleBracketId = t.Id, TitleName = t.NameFor(user.Gender) }).ToList(),
                        // Each total is at most its cap here (the Apply* calls above passed).
                        CoinBonusGranted = (int)coinBonusTotal,
                        GemBonusGranted = (int)gemBonusTotal,
                        ExpBonusGranted = (int)expBonusTotal,
                        CoinBonusBase = coinBonusBase,
                        GemBonusBase = gemBonusBase,
                        ExpBonusBase = expBonusBase,
                        CoinBonusMultipliers = coinMultipliers,
                        GemBonusMultipliers = gemMultipliers,
                        ExpBonusMultipliers = expMultipliers
                    };
                }
            }

            user.Coins = newCoins;
            user.Gems = newGems;
            user.ExperiencePoints = newExperience;
            await _repo.SaveBalancesAsync(user);

            // docs/specs/user-management/IMPLEMENTATION_PLAN.md §0: every user-features mutation
            // threads in an AuditLogEntry write as it's built, rather than user-management's
            // Phase 2 retrofitting it later. This closes user-features IMPLEMENTATION_PLAN.md §6
            // carried-forward item 4's "AdjustBalancesAsync still needs retrofitting".
            await _auditLogService.RecordAsync(actorUserId, userId, AuditAction.BalanceAdjusted, JsonSerializer.Serialize(new
            {
                coinsDelta,
                gemsDelta,
                experienceDelta,
                reason,
                metadata,
                titleBonusCoins = titleChange?.CoinBonusGranted ?? 0,
                titleBonusGems = titleChange?.GemBonusGranted ?? 0,
                titleBonusExp = titleChange?.ExpBonusGranted ?? 0,
                // Before/after (bonuses included) for the moderation activity feed.
                coinsBefore = originalCoins,
                coinsAfter = user.Coins,
                gemsBefore = originalGems,
                gemsAfter = user.Gems,
                experienceBefore = originalExperience,
                experienceAfter = user.ExperiencePoints
            }));

            if (titleChange != null)
            {
                // One consolidated audit entry for the whole crossing, not one per tier.
                await _auditLogService.RecordAsync(actorUserId, userId, AuditAction.TitleChanged, JsonSerializer.Serialize(new
                {
                    fromTitleBracketId = titleChange.FromTitleBracketId,
                    fromTitleName = titleChange.FromTitleName,
                    toTitleBracketId = titleChange.ToTitleBracketId,
                    toTitleName = titleChange.ToTitleName,
                    crossedTitles = titleChange.CrossedTitles.Select(t => t.TitleName),
                    direction = titleChange.Direction
                }));
            }

            return titleChange;
        }

        /// <summary>A title promotion bonus scaled by its multiplier, rounded to whole units and
        /// never negative (a multiplier set negative by a direct DB edit pays nothing).</summary>
        private static long ScaleBonus(int bonus, decimal multiplier) =>
            BalanceLimits.ToWholeAmount(() => bonus * multiplier, "title bonus");

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
            await _repo.MergeUsersAsync(primaryUserId, secondaryUserId);

            // Return updated primary user
            var mergedUser = await _repo.GetByIdAsync(primaryUserId);
            return await MapToUserDtoAsync(mergedUser!);
        }

        // ===== HELPER METHODS =====

        /// <summary>
        /// Link an authenticated web app user to a Minecraft account using a link code.
        /// This handles the web-app-first scenario where user already has email/password set.
        /// </summary>
        public async Task<UserDto> LinkMinecraftAccountAsync(int userId, string linkCode)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Invalid user ID.", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(linkCode))
            {
                throw new ArgumentException("Link code is required.", nameof(linkCode));
            }

            // Step 1: Validate link code WITHOUT consuming it
            var (isLinkCodeValid, minecraftUser) = await ValidateLinkCodeAsync(linkCode);
            
            if (!isLinkCodeValid || minecraftUser == null)
            {
                throw new InvalidOperationException("Invalid or expired link code");
            }

            // Step 2: Get the authenticated user
            var webAppUser = await _repo.GetByIdAsync(userId);
            if (webAppUser == null)
            {
                throw new KeyNotFoundException($"Web app user with ID {userId} not found");
            }

            // Step 3: Check if the Minecraft account already has a different UUID
            // (i.e., if the link code points to a different user)
            if (minecraftUser.Id != webAppUser.Id && minecraftUser.Uuid != null)
            {
                // Step 3a: We have a duplicate scenario
                // The Minecraft account (minecraftUser) already exists with a UUID
                // And our authenticated user (webAppUser) wants to link to it

                // Check if they have the same UUID already
                if (!string.IsNullOrWhiteSpace(webAppUser.Uuid) && webAppUser.Uuid == minecraftUser.Uuid)
                {
                    // Already linked, just consume the code
                    await _linkCodeService.ConsumeLinkCodeAsync(linkCode);
                    return await MapToUserDtoAsync(webAppUser);
                }

                // If different users, merge them (keep web app user as primary)
                await _repo.MergeUsersAsync(webAppUser.Id, minecraftUser.Id);
            }

            // Step 4: Consume the link code AFTER all validation
            var (isConsumed, consumedUser) = await ConsumeLinkCodeAsync(linkCode);
            if (!isConsumed || consumedUser == null)
            {
                throw new InvalidOperationException("Failed to consume link code");
            }

            // Step 5: Update the web app user with the Minecraft UUID from the consumed user
            minecraftUser = consumedUser; // Use the consumed user to get the UUID
            
            if (string.IsNullOrWhiteSpace(webAppUser.Uuid) && !string.IsNullOrWhiteSpace(minecraftUser.Uuid))
            {
                webAppUser.Uuid = minecraftUser.Uuid;
                await _repo.UpdateUserAsync(webAppUser);
            }

            // Step 6: Return updated user
            var updatedUser = await _repo.GetByIdAsync(webAppUser.Id);
            return await MapToUserDtoAsync(updatedUser!);
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

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

        public UserService(
            IUserRepository repo,
            IMapper mapper,
            IPasswordService passwordService,
            ILinkCodeService linkCodeService,
            ITitleService titleService,
            IUserPermissionGroupService membershipService,
            IAuditLogService auditLogService)
        {
            _repo = repo;
            _mapper = mapper;
            _passwordService = passwordService;
            _linkCodeService = linkCodeService;
            _titleService = titleService;
            _membershipService = membershipService;
            _auditLogService = auditLogService;
        }

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
            var title = await _titleService.ResolveAsync(user.ExperiencePoints);
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
            }
            return dto;
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
            return await MapToUserDtoAsync(user);
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

            // Real gap found while retrofitting audit hooks (docs/specs/user-management/
            // IMPLEMENTATION_PLAN.md §0): unlike ActiveMode/LastSalaryPayoutAt (already
            // opt.Ignore()'d in UserMappingProfile precisely so a generic edit can't silently
            // reset them), Coins/Gems/ExperiencePoints/PersonalSalaryMultiplier ARE mapped here,
            // so this generic FormWizard path is a second, previously-unaudited write route to
            // the exact fields AdjustBalancesAsync/PayOutAsync now audit. Captured before the
            // mapper overwrites them so the diff can be logged after saving.
            var previousCoins = existing.Coins;
            var previousGems = existing.Gems;
            var previousExperience = existing.ExperiencePoints;
            var previousMultiplier = existing.PersonalSalaryMultiplier;
            var previousTitle = await _titleService.ResolveAsync(previousExperience);

            // Apply all editable UserDto fields onto the tracked entity. The mapping profile
            // (UserMappingProfile: UserDto -> User) already ignores fields that must never be
            // set from this endpoint (PasswordHash, LastPasswordChangeAt, LastEmailChangeAt,
            // DeletedAt, DeletedReason, ArchiveUntil, LinkCodes).
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

            if (existing.Coins != previousCoins || existing.Gems != previousGems ||
                existing.ExperiencePoints != previousExperience || existing.PersonalSalaryMultiplier != previousMultiplier)
            {
                await _auditLogService.RecordAsync(actorUserId, id, AuditAction.BalanceAdjusted, JsonSerializer.Serialize(new
                {
                    source = "GenericProfileEdit",
                    coinsDelta = existing.Coins - previousCoins,
                    gemsDelta = existing.Gems - previousGems,
                    experienceDelta = existing.ExperiencePoints - previousExperience,
                    previousPersonalSalaryMultiplier = previousMultiplier,
                    newPersonalSalaryMultiplier = existing.PersonalSalaryMultiplier
                }));

                if (existing.ExperiencePoints != previousExperience)
                {
                    var newTitle = await _titleService.ResolveAsync(existing.ExperiencePoints);
                    if (newTitle.TitleBracketId != previousTitle.TitleBracketId)
                    {
                        await _auditLogService.RecordAsync(actorUserId, id, AuditAction.TitleChanged, JsonSerializer.Serialize(new
                        {
                            fromTitleBracketId = previousTitle.TitleBracketId,
                            fromTitleName = previousTitle.TitleName,
                            toTitleBracketId = newTitle.TitleBracketId,
                            toTitleName = newTitle.TitleName,
                            direction = existing.ExperiencePoints > previousExperience ? "promotion" : "demotion"
                        }));
                    }
                }
            }
        }

        public async Task UpdateCoinsAsync(int id, int coins)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"User with id {id} not found.");

            await _repo.UpdateUserCoinsAsync(id, coins);
        }

        public async Task UpdateCoinsByUuidAsync(string uuid, int coins)
        {
            if (string.IsNullOrWhiteSpace(uuid)) throw new ArgumentException("Invalid uuid.", nameof(uuid));
            var existing = await _repo.GetByUuidAsync(uuid);
            if (existing == null) throw new KeyNotFoundException($"User with uuid {uuid} not found.");

            await _repo.UpdateUserCoinsByUuidAsync(uuid, coins);
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
        public async Task AdjustBalancesAsync(int userId, int coinsDelta, int gemsDelta, int experienceDelta, string reason, string? metadata = null, int? actorUserId = null)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Invalid user ID.", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Reason is required for balance adjustments.", nameof(reason));
            }

            var user = await _repo.GetByIdAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID {userId} not found.");
            }

            // Calculate new balances
            int newCoins = user.Coins + coinsDelta;
            int newGems = user.Gems + gemsDelta;
            int newExperience = user.ExperiencePoints + experienceDelta;

            // Reject underflows (no negative balances)
            if (newCoins < 0)
            {
                throw new InvalidOperationException($"Insufficient coins. Current: {user.Coins}, Attempted change: {coinsDelta}");
            }

            if (newGems < 0)
            {
                throw new InvalidOperationException($"Insufficient gems. Current: {user.Gems}, Attempted change: {gemsDelta}");
            }

            if (newExperience < 0)
            {
                throw new InvalidOperationException($"Insufficient experience points. Current: {user.ExperiencePoints}, Attempted change: {experienceDelta}");
            }

            // Resolved before the mutation so a resulting title change (see below) can be
            // detected by comparing against the post-mutation resolution.
            var previousTitle = experienceDelta != 0 ? await _titleService.ResolveAsync(user.ExperiencePoints) : null;

            // Update balances
            user.Coins = newCoins;
            user.Gems = newGems;
            user.ExperiencePoints = newExperience;

            await _repo.UpdateUserAsync(user);

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
                metadata
            }));

            // TitleService has no separate mutating "set title" method (see its own doc comment —
            // title is purely derived from ExperiencePoints), so a title change is detected here by
            // comparing the resolution before/after an XP-moving balance adjustment, not by a
            // dedicated write path in TitleService itself.
            if (previousTitle != null)
            {
                var newTitle = await _titleService.ResolveAsync(user.ExperiencePoints);
                if (newTitle.TitleBracketId != previousTitle.TitleBracketId)
                {
                    await _auditLogService.RecordAsync(actorUserId, userId, AuditAction.TitleChanged, JsonSerializer.Serialize(new
                    {
                        fromTitleBracketId = previousTitle.TitleBracketId,
                        fromTitleName = previousTitle.TitleName,
                        toTitleBracketId = newTitle.TitleBracketId,
                        toTitleName = newTitle.TitleName,
                        // Title brackets are ordered by MinExperience, not by TitleBracketId (an
                        // auto-increment PK unrelated to rank order), so direction is derived from
                        // the XP delta's sign rather than comparing bracket ids directly —
                        // resolution is monotonic in XP per TitleService's own doc comment.
                        direction = experienceDelta > 0 ? "promotion" : "demotion"
                    }));
                }
            }
        }

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

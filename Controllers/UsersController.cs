using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using AutoMapper;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _service;
        private readonly IMapper _mapper;
        private readonly IPermissionResolutionService _permissionResolutionService;
        private readonly ISalaryService _salaryService;

        public UsersController(
            IUserService service,
            IMapper mapper,
            IPermissionResolutionService permissionResolutionService,
            ISalaryService salaryService)
        {
            _service = service;
            _mapper = mapper;
            _permissionResolutionService = permissionResolutionService;
            _salaryService = salaryService;
        }

        /// <summary>
        /// Cheap single-node permission check. This is what knk-plugin calls per permission
        /// check (see docs/specs/user-features/IMPLEMENTATION_PLAN.md §1) - keep it a single,
        /// direct lookup rather than anything that fans out further.
        /// </summary>
        /// <param name="id">User ID</param>
        /// <param name="node">The permission node to check, e.g. "knk.gate.open".</param>
        /// <response code="200">Returns the resolution result (granted/denied/undeclared)</response>
        /// <response code="400">Node query parameter missing</response>
        /// <response code="404">User not found</response>
        [HttpGet("{id:int}/permissions/check")]
        public async Task<IActionResult> CheckPermission(int id, [FromQuery] string? node)
        {
            if (string.IsNullOrWhiteSpace(node))
            {
                return BadRequest(new { error = "InvalidRequest", message = "node query parameter is required" });
            }

            var result = await _permissionResolutionService.CheckAsync(id, node);
            if (result == null)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }

            return Ok(result);
        }

        /// <summary>
        /// Full resolved permission set for a user - every declared node, its value, and which
        /// holder (the user directly, or a PermissionGroup) it came from. Backs the
        /// user-management admin module's composite player-profile view (see
        /// docs/specs/user-features/IMPLEMENTATION_PLAN.md §1 item 5).
        /// </summary>
        /// <param name="id">User ID</param>
        /// <response code="200">Returns the full resolved permission set</response>
        /// <response code="404">User not found</response>
        [HttpGet("{id:int}/permissions/effective")]
        public async Task<IActionResult> GetEffectivePermissions(int id)
        {
            var result = await _permissionResolutionService.GetEffectiveAsync(id);
            if (result == null)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }

            return Ok(result);
        }

        /// <summary>
        /// Get all users
        /// </summary>
        /// <returns>List of all users with full details</returns>
        /// <response code="200">Returns list of users</response>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        /// <summary>
        /// Get user by ID
        /// </summary>
        /// <param name="id">User ID</param>
        /// <returns>User with full details</returns>
        /// <response code="200">Returns the user</response>
        /// <response code="404">User not found</response>
        [HttpGet("{id:int}", Name = nameof(GetUserById))]
        public async Task<IActionResult> GetUserById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        /// <summary>
        /// Get user summary by UUID (Minecraft plugin uses this)
        /// </summary>
        /// <param name="uuid">Minecraft player UUID</param>
        /// <returns>User summary with coins, gems, and experience points</returns>
        /// <response code="200">Returns the user summary</response>
        /// <response code="404">User not found</response>
        [HttpGet("uuid/{uuid}", Name = nameof(GetUserSummaryByUuid))]
        public async Task<IActionResult> GetUserSummaryByUuid(string uuid)
        {
            var item = await _service.GetByUuidAsync(uuid);
            if (item == null) return NotFound();
            var dto = new UserSummaryDto
            {
                Id = item.Id,
                Username = item.Username,
                Coins = item.Coins,
                Gems = item.Gems,
                ExperiencePoints = item.ExperiencePoints,
                Uuid = item.Uuid,
                IsFullAccount = item.IsFullAccount,
                GatePassThroughMethodDefault = item.GatePassThroughMethodDefault,
                ActiveMode = item.ActiveMode,
                TitleBracketId = item.TitleBracketId,
                TitleName = item.TitleName,
                PrestigeExperience = item.PrestigeExperience,
                PremiumTierGroupId = item.PremiumTierGroupId,
                PremiumTierName = item.PremiumTierName,
                PremiumTierExpiresAt = item.PremiumTierExpiresAt
            };
            return Ok(dto);
        }

        /// <summary>
        /// Get user summary by username
        /// </summary>
        /// <param name="username">Username</param>
        /// <returns>User summary with coins, gems, and experience points</returns>
        /// <response code="200">Returns the user summary</response>
        /// <response code="404">User not found</response>
        [HttpGet("username/{username}", Name = nameof(GetUserSummaryByUsername))]
        public async Task<IActionResult> GetUserSummaryByUsername(string username)
        {
            var item = await _service.GetByUsernameAsync(username);
            if (item == null) return NotFound();
            var dto = new UserSummaryDto
            {
                Id = item.Id,
                Username = item.Username,
                Coins = item.Coins,
                Gems = item.Gems,
                ExperiencePoints = item.ExperiencePoints,
                Uuid = item.Uuid,
                IsFullAccount = item.IsFullAccount,
                GatePassThroughMethodDefault = item.GatePassThroughMethodDefault,
                ActiveMode = item.ActiveMode,
                TitleBracketId = item.TitleBracketId,
                TitleName = item.TitleName,
                PrestigeExperience = item.PrestigeExperience,
                PremiumTierGroupId = item.PremiumTierGroupId,
                PremiumTierName = item.PremiumTierName,
                PremiumTierExpiresAt = item.PremiumTierExpiresAt
            };
            return Ok(dto);
        }

        /// <summary>
        /// Create a new user account
        /// </summary>
        /// <remarks>
        /// Supports two flows:
        /// 1. Web app first: Provide email + password (optional UUID/username)
        /// 2. Minecraft first: Provide UUID + username only
        /// 
        /// Password policy: 8-128 characters, no forced complexity, weak password blacklist
        /// </remarks>
        /// <param name="user">User creation data</param>
        /// <returns>Created user with link code if applicable</returns>
        /// <response code="201">User created successfully</response>
        /// <response code="400">Validation failed</response>
        /// <response code="409">Duplicate username, email, or UUID</response>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserCreateDto user)
        {
            if (user == null) return BadRequest(new { error = "InvalidRequest", message = "User data is required" });
            
            try
            {
                // Web app first linking (BEFORE validation): If providing UUID + username, check for existing pre-registered account
                // A pre-registered account has the same username but uuid = null (awaiting Minecraft join)
                UserDto? created = null;
                if (!string.IsNullOrEmpty(user.Uuid) && !string.IsNullOrEmpty(user.Username))
                {
                    var existingByUsername = await _service.GetByUsernameAsync(user.Username);
                    if (existingByUsername != null && string.IsNullOrEmpty(existingByUsername.Uuid))
                    {
                        // Found pre-registered account: link by setting UUID
                        var updatedDto = new UserDto
                        {
                            Id = existingByUsername.Id,
                            Username = existingByUsername.Username,
                            Email = existingByUsername.Email,
                            Uuid = user.Uuid,  // Set UUID from Minecraft join
                            Coins = existingByUsername.Coins,
                            Gems = existingByUsername.Gems,
                            ExperiencePoints = existingByUsername.ExperiencePoints
                        };
                        await _service.UpdateAsync(existingByUsername.Id, updatedDto);
                        created = await _service.GetByIdAsync(existingByUsername.Id);
                    }
                }

                // If not linked to existing pre-registered account, proceed with normal validation and creation
                if (created == null)
                {
                    // Check if link code is provided (minecraft-only account linking)
                    int? minecraftOnlyAccountId = null;
                    if (!string.IsNullOrEmpty(user.LinkCode))
                    {
                        var (isLinkCodeValid, linkCodeUser) = await _service.ValidateLinkCodeAsync(user.LinkCode);
                        if (!isLinkCodeValid || linkCodeUser == null)
                        {
                            return BadRequest(new { error = "InvalidLinkCode", message = "Invalid or expired link code" });
                        }

                        // Verify the minecraft account matches the username provided (if username is provided)
                        if (!string.IsNullOrEmpty(user.Username) && !string.IsNullOrEmpty(linkCodeUser.Username))
                        {
                            if (!user.Username.Equals(linkCodeUser.Username, StringComparison.OrdinalIgnoreCase))
                            {
                                return BadRequest(new { error = "UsernameConflict", message = "Username does not match the minecraft account associated with this link code" });
                            }
                        }

                        minecraftOnlyAccountId = linkCodeUser.Id;
                    }

                    // Phase 4.2: Validate user creation (pass minecraftOnlyAccountId to skip username duplicate check)
                    var (isValid, errorMessage) = await _service.ValidateUserCreationAsync(user, minecraftOnlyAccountId);
                    if (!isValid)
                    {
                        return BadRequest(new { error = "ValidationFailed", message = errorMessage });
                    }

                    // Check for duplicates
                    if (!string.IsNullOrEmpty(user.Username))
                    {
                        var (usernameTaken, conflictingUserId) = await _service.CheckUsernameTakenAsync(user.Username);
                        if (usernameTaken)
                        {
                            // If link code is valid for this username, link the minecraft-only account now
                            if (minecraftOnlyAccountId.HasValue && conflictingUserId.HasValue && conflictingUserId.Value == minecraftOnlyAccountId.Value && !string.IsNullOrEmpty(user.LinkCode))
                            {
                                var (isConsumed, _) = await _service.ConsumeLinkCodeAsync(user.LinkCode);
                                if (!isConsumed)
                                {
                                    return BadRequest(new { error = "InvalidLinkCode", message = "Invalid or expired link code" });
                                }

                                if (!string.IsNullOrEmpty(user.Email))
                                {
                                    await _service.UpdateEmailAsync(minecraftOnlyAccountId.Value, user.Email, null);
                                }

                                if (!string.IsNullOrEmpty(user.Password))
                                {
                                    var confirmation = user.PasswordConfirmation ?? user.Password;
                                    await _service.ChangePasswordAsync(minecraftOnlyAccountId.Value, user.Password, user.Password, confirmation);
                                }

                                created = await _service.GetByIdAsync(minecraftOnlyAccountId.Value);
                            }
                            else
                            {
                                // Check if the conflicting account is minecraft-only (can be linked instead)
                                var conflictingUser = conflictingUserId.HasValue ? await _service.GetByIdAsync(conflictingUserId.Value) : null;
                                if (conflictingUser != null && conflictingUser.IsFullAccount == false && !string.IsNullOrEmpty(conflictingUser.Uuid))
                                {
                                    // Minecraft-only account found - offer linking instead of rejection
                                    return Conflict(new 
                                    { 
                                        error = "MinecraftOnlyAccountExists", 
                                        message = "Username matches your Minecraft account. Please use the link code from /account link in-game to complete registration.",
                                        conflictingUserId = conflictingUserId,
                                        isMinecraftOnly = true
                                    });
                                }

                                // Regular duplicate (full account) - reject
                                return Conflict(new { error = "DuplicateUsername", message = "Username is already taken" });
                            }
                        }
                    }

                    if (created == null)
                    {
                        if (!string.IsNullOrEmpty(user.Email))
                        {
                            var (emailTaken, _) = await _service.CheckEmailTakenAsync(user.Email);
                            if (emailTaken)
                            {
                                return Conflict(new { error = "DuplicateEmail", message = "Email is already in use" });
                            }
                        }

                        if (!string.IsNullOrEmpty(user.Uuid))
                        {
                            var (uuidTaken, _) = await _service.CheckUuidTakenAsync(user.Uuid);
                            if (uuidTaken)
                            {
                                return Conflict(new { error = "DuplicateUuid", message = "UUID is already registered" });
                            }
                        }

                        // Create the user (service handles password hashing, link code validation/consumption, etc.)
                        created = await _service.CreateAsync(user);
                    }
                }
                
                // Generate link code for response if web app first (has email but no uuid yet)
                LinkCodeResponseDto? linkCode = null;
                if (!string.IsNullOrEmpty(created.Email) && string.IsNullOrEmpty(created.Uuid))
                {
                    linkCode = await _service.GenerateLinkCodeAsync(created.Id);
                }

                var response = new 
                {
                    user = created,
                    linkCode = linkCode
                };

                return CreatedAtRoute(nameof(GetUserById), new { id = created.Id }, response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidArgument", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = "OperationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Update user details
        /// </summary>
        /// <param name="id">User ID</param>
        /// <param name="user">Updated user data</param>
        /// <returns>No content</returns>
        /// <response code="204">User updated successfully</response>
        /// <response code="400">Validation failed</response>
        /// <response code="404">User not found</response>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UserDto user)
        {
            if (user == null) return BadRequest(new { error = "InvalidRequest", message = "User data is required" });
            try
            {
                await _service.UpdateAsync(id, user);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Update user coins balance
        /// </summary>
        /// <param name="id">User ID</param>
        /// <param name="coins">New coins balance</param>
        /// <returns>No content</returns>
        /// <response code="204">Coins updated successfully</response>
        /// <response code="400">Validation failed</response>
        /// <response code="404">User not found</response>
        [HttpPut("{id:int}/coins")]
        public async Task<IActionResult> UpdateCoins(int id, [FromBody] int coins)
        {
            try
            {
                await _service.UpdateCoinsAsync(id, coins);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Update user coins balance by UUID (Minecraft plugin uses this)
        /// </summary>
        /// <param name="uuid">Minecraft player UUID</param>
        /// <param name="coins">New coins balance</param>
        /// <returns>No content</returns>
        /// <response code="204">Coins updated successfully</response>
        /// <response code="400">Validation failed</response>
        /// <response code="404">User not found</response>
        [HttpPut("{uuid}/coins")]
        public async Task<IActionResult> UpdateCoinsByUuid(string uuid, [FromBody] int coins)
        {
            try
            {
                await _service.UpdateCoinsByUuidAsync(uuid, coins);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with UUID {uuid} not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Update a user's preferred gate pass-through method
        /// </summary>
        /// <remarks>
        /// Used by the Minecraft plugin's /knk gate passthrough &lt;mode&gt; command so a player
        /// can choose how they pass through AllowPassThrough-enabled gates.
        /// </remarks>
        /// <param name="id">User ID</param>
        /// <param name="request">New default pass-through method</param>
        /// <returns>No content</returns>
        /// <response code="204">Updated successfully</response>
        /// <response code="404">User not found</response>
        [HttpPut("{id:int}/gate-passthrough-method")]
        public async Task<IActionResult> UpdateGatePassThroughMethod(int id, [FromBody] UpdateGatePassThroughMethodDto request)
        {
            try
            {
                await _service.UpdateGatePassThroughMethodAsync(id, request.GatePassThroughMethodDefault);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Update a user's owner/staff mode
        /// </summary>
        /// <remarks>
        /// Used by the Minecraft plugin's /ownermode and /staffmode commands so the player's mode
        /// (and the vanish it implies) survives a server restart - see
        /// docs/specs/user-features/DESIGN.md §6.1. Who may enter which mode is enforced in-game
        /// via the knk.mode.owner/knk.mode.staff permission nodes, not here.
        /// </remarks>
        /// <param name="id">User ID</param>
        /// <param name="request">New active mode (None, Staff or Owner)</param>
        /// <returns>No content</returns>
        /// <response code="204">Updated successfully</response>
        /// <response code="400">Unknown mode value</response>
        /// <response code="404">User not found</response>
        [HttpPut("{id:int}/active-mode")]
        public async Task<IActionResult> UpdateActiveMode(int id, [FromBody] UpdateActiveModeDto request)
        {
            try
            {
                await _service.UpdateActiveModeAsync(id, request.ActiveMode);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Adjust a user's coins/gems/experience by a signed delta, with an audit reason.
        /// </summary>
        /// <remarks>
        /// Wraps UserService.AdjustBalancesAsync (previously implemented but unreachable via the
        /// API — no controller route called it). This is the deduction/adjustment hook
        /// docs/specs/user-features/IMPLEMENTATION_PLAN.md §4 calls for: a negative
        /// experienceDelta here is how XP is deducted for misconduct, and the resolved title on
        /// the next GET automatically reflects the new bracket — TitleService has no separate
        /// "demotion" code path, since it always jumps straight to whatever bracket the current
        /// XP total resolves to.
        /// </remarks>
        /// <param name="id">User ID</param>
        /// <param name="request">Signed deltas and an audit reason</param>
        /// <returns>No content</returns>
        /// <response code="204">Adjusted successfully</response>
        /// <response code="400">Missing reason, or a delta would underflow a balance below zero</response>
        /// <response code="404">User not found</response>
        [HttpPut("{id:int}/balances")]
        public async Task<IActionResult> AdjustBalances(int id, [FromBody] AdjustBalancesDto request)
        {
            try
            {
                await _service.AdjustBalancesAsync(id, request.CoinsDelta, request.GemsDelta, request.ExperienceDelta, request.Reason, request.Metadata);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = "InsufficientBalance", message = ex.Message });
            }
        }

        /// <summary>
        /// Pays out the salary gap since this user's last payout, if at least an hour has passed.
        /// </summary>
        /// <remarks>
        /// docs/specs/user-features/IMPLEMENTATION_PLAN.md §6. Intended trigger: the plugin calls
        /// this on player join (not wired this phase — see IMPLEMENTATION_PLAN.md §6's
        /// knk-web-api-only scope). Always returns 200; check the response's "paid" field rather
        /// than the status code to tell a real payout from "not yet eligible".
        /// </remarks>
        /// <param name="id">User ID</param>
        /// <response code="200">Payout evaluated (see the "paid" field for the outcome)</response>
        /// <response code="404">User not found</response>
        [HttpPost("{id:int}/salary/payout")]
        [ProducesResponseType(typeof(SalaryPayoutResultDto), 200)]
        public async Task<ActionResult<SalaryPayoutResultDto>> PayOutSalary(int id)
        {
            try
            {
                var result = await _salaryService.PayOutAsync(id);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }
        }

        // ===== PHASE 4.3: AUTHENTICATION ENDPOINTS =====

        /// <summary>
        /// Generate a new link code for the authenticated user
        /// </summary>
        /// <remarks>
        /// Link codes are valid for 20 minutes and used to link Minecraft accounts with web accounts.
        /// Format: 8 alphanumeric characters (e.g., ABC12XYZ)
        /// 
        /// Two ways to use this endpoint:
        /// 1. WEB APP (Authenticated): POST with Authorization header, no body required
        /// 2. MINECRAFT PLUGIN (Server-side): POST with userId in body, no auth required
        /// </remarks>
        /// <param name="request">Optional request body with userId (for Minecraft plugin use)</param>
        /// <returns>Link code with expiration time</returns>
        /// <response code="200">Link code generated successfully</response>
        /// <response code="401">User not authenticated (web app) or missing userId (plugin)</response>
        /// <response code="404">User not found</response>
        [HttpPost("generate-link-code")]
        public async Task<IActionResult> GenerateLinkCode([FromBody] GenerateLinkCodeRequestDto? request = null)
        {
            try
            {
                int? userId = null;

                // Check if request has userId (Minecraft plugin use case)
                if (request?.UserId.HasValue == true)
                {
                    userId = request.UserId;
                }
                else
                {
                    // Extract from JWT claims (web app use case)
                    userId = GetUserIdFromClaims(User);
                    if (!userId.HasValue)
                    {
                        return Unauthorized(new { error = "InvalidToken", message = "User claim missing or not authenticated." });
                    }
                }

                var user = await _service.GetByIdAsync(userId.Value);
                if (user == null)
                {
                    return NotFound(new { error = "UserNotFound", message = $"User with ID {userId.Value} not found" });
                }

                var linkCode = await _service.GenerateLinkCodeAsync(userId.Value);
                return Ok(linkCode);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidArgument", message = ex.Message });
            }
        }

        /// <summary>
        /// Validate a link code and return associated user information
        /// </summary>
        /// <remarks>
        /// Consumes the link code (marks it as used) and returns user details if valid.
        /// Link codes expire after 20 minutes.
        /// </remarks>
        /// <param name="code">8-character link code</param>
        /// <returns>Validation result with user information if valid</returns>
        /// <response code="200">Validation result (check IsValid field)</response>
        /// <response code="400">Invalid request</response>
        [HttpPost("validate-link-code/{code}")]
        public async Task<IActionResult> ValidateLinkCode(string code)
        {
            try
            {
                var (isValid, user) = await _service.ValidateLinkCodeAsync(code);
                
                if (!isValid || user == null)
                {
                    return Ok(new ValidateLinkCodeResponseDto
                    {
                        IsValid = false,
                        Error = "Invalid or expired link code"
                    });
                }

                return Ok(new ValidateLinkCodeResponseDto
                {
                    IsValid = true,
                    UserId = user.Id,
                    Username = user.Username,
                    Email = user.Email
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidArgument", message = ex.Message });
            }
        }

        /// <summary>
        /// Change user password
        /// </summary>
        /// <remarks>
        /// Requires current password for verification.
        /// New password must meet policy: 8-128 characters, not in weak password blacklist.
        /// </remarks>
        /// <param name="id">User ID</param>
        /// <param name="request">Password change request</param>
        /// <returns>No content</returns>
        /// <response code="204">Password changed successfully</response>
        /// <response code="400">Validation failed (weak password, mismatch, etc.)</response>
        /// <response code="401">Current password incorrect</response>
        /// <response code="404">User not found</response>
        [HttpPut("{id:int}/change-password")]
        public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto request)
        {
            try
            {
                var user = await _service.GetByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
                }

                await _service.ChangePasswordAsync(id, request.CurrentPassword, request.NewPassword, request.PasswordConfirmation);
                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = "InvalidPassword", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Update user email address
        /// </summary>
        /// <remarks>
        /// Optionally requires current password for additional security.
        /// Email must be unique across all accounts.
        /// </remarks>
        /// <param name="id">User ID</param>
        /// <param name="request">Email update request</param>
        /// <returns>No content</returns>
        /// <response code="204">Email updated successfully</response>
        /// <response code="400">Validation failed (invalid email format)</response>
        /// <response code="401">Current password incorrect</response>
        /// <response code="404">User not found</response>
        /// <response code="409">Email already in use</response>
        [HttpPut("{id:int}/update-email")]
        public async Task<IActionResult> UpdateEmail(int id, [FromBody] UpdateEmailDto request)
        {
            try
            {
                var user = await _service.GetByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
                }

                // Check if email is already taken
                var (emailTaken, conflictingUserId) = await _service.CheckEmailTakenAsync(request.NewEmail, id);
                if (emailTaken)
                {
                    return Conflict(new { error = "DuplicateEmail", message = "Email is already in use by another account" });
                }

                await _service.UpdateEmailAsync(id, request.NewEmail, request.CurrentPassword);
                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = "InvalidPassword", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Check for duplicate accounts based on UUID and username
        /// </summary>
        /// <remarks>
        /// Used by Minecraft server to detect when a player has multiple accounts.
        /// Returns both the primary (UUID-based) and conflicting (username-based) accounts.
        /// </remarks>
        /// <param name="request">Duplicate check request with UUID and username</param>
        /// <returns>Duplicate check result</returns>
        /// <response code="200">Check completed (check HasDuplicate field)</response>
        /// <response code="400">Invalid request</response>
        [HttpGet("check-duplicate")]
        public async Task<IActionResult> CheckDuplicateAvailability([FromQuery] string? email, [FromQuery] string? username, [FromQuery] int? excludeUserId)
        {
            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(username))
            {
                return BadRequest(new { error = "InvalidRequest", message = "Email or username is required" });
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var (isTaken, conflictingUserId) = await _service.CheckEmailTakenAsync(email, excludeUserId);
                return Ok(new { available = !isTaken, conflictingUserId });
            }

            var (usernameTaken, usernameConflictId) = await _service.CheckUsernameTakenAsync(username!, excludeUserId);
            return Ok(new { available = !usernameTaken, conflictingUserId = usernameConflictId });
        }

        [HttpPost("check-duplicate")]
        public async Task<IActionResult> CheckDuplicate([FromBody] DuplicateCheckDto request)
        {
            try
            {
                var (hasDuplicate, secondaryUserId) = await _service.CheckForDuplicateAsync(request.Uuid, request.Username);

                if (!hasDuplicate)
                {
                    return Ok(new DuplicateCheckResponseDto
                    {
                        HasDuplicate = false,
                        Message = "No duplicate accounts found"
                    });
                }

                // Get both users
                var primaryUser = await _service.GetByUuidAsync(request.Uuid);
                var secondaryUser = secondaryUserId.HasValue ? await _service.GetByIdAsync(secondaryUserId.Value) : null;

                return Ok(new DuplicateCheckResponseDto
                {
                    HasDuplicate = true,
                    PrimaryUser = primaryUser != null ? new UserSummaryDto
                    {
                        Id = primaryUser.Id,
                        Username = primaryUser.Username,
                        Uuid = primaryUser.Uuid,
                        Coins = primaryUser.Coins,
                        Gems = primaryUser.Gems,
                        ExperiencePoints = primaryUser.ExperiencePoints
                    } : null,
                    ConflictingUser = secondaryUser != null ? new UserSummaryDto
                    {
                        Id = secondaryUser.Id,
                        Username = secondaryUser.Username,
                        Uuid = secondaryUser.Uuid,
                        Coins = secondaryUser.Coins,
                        Gems = secondaryUser.Gems,
                        ExperiencePoints = secondaryUser.ExperiencePoints
                    } : null,
                    Message = "Duplicate accounts detected. Please merge them."
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidArgument", message = ex.Message });
            }
        }

        // ===== PHASE 4.4: ACCOUNT MERGE ENDPOINTS =====

        /// <summary>
        /// Merge two user accounts into one
        /// </summary>
        /// <remarks>
        /// Keeps the primary account and soft-deletes the secondary account.
        /// Primary account retains all its data (winner takes all strategy).
        /// Foreign key relationships are updated to point to primary account.
        /// </remarks>
        /// <param name="request">Merge request with primary and secondary user IDs</param>
        /// <returns>Merged user account</returns>
        /// <response code="200">Accounts merged successfully</response>
        /// <response code="400">Invalid request or operation failed</response>
        /// <response code="404">One or both users not found</response>
        [HttpPost("merge")]
        public async Task<IActionResult> MergeAccounts([FromBody] AccountMergeDto request)
        {
            try
            {
                // Verify both users exist
                var primaryUser = await _service.GetByIdAsync(request.PrimaryUserId);
                if (primaryUser == null)
                {
                    return NotFound(new { error = "PrimaryUserNotFound", message = $"Primary user with ID {request.PrimaryUserId} not found" });
                }

                var secondaryUser = await _service.GetByIdAsync(request.SecondaryUserId);
                if (secondaryUser == null)
                {
                    return NotFound(new { error = "SecondaryUserNotFound", message = $"Secondary user with ID {request.SecondaryUserId} not found" });
                }

                // Perform merge
                var mergedUser = await _service.MergeAccountsAsync(request.PrimaryUserId, request.SecondaryUserId);

                return Ok(new AccountMergeResultDto
                {
                    User = mergedUser,
                    MergedFromUserId = request.SecondaryUserId,
                    Message = $"Successfully merged account {request.SecondaryUserId} into {request.PrimaryUserId}"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidArgument", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = "OperationFailed", message = ex.Message });
            }
        }

        /// <summary>
        /// Link an existing Minecraft account with email and password from web app
        /// </summary>
        /// <remarks>
        /// Used when a player creates account in Minecraft first, then wants to add web access.
        /// Requires a valid link code generated from Minecraft.
        /// Sets initial password (no current password needed for first-time setup).
        /// </remarks>
        /// <param name="request">Link account request with code, email, and password</param>
        /// <returns>Linked user account</returns>
        /// <response code="200">Account linked successfully</response>
        /// <response code="400">Invalid link code, weak password, or validation failed</response>
        /// <response code="409">Email already in use</response>
        [HttpPost("link-account")]
        public async Task<IActionResult> LinkAccount([FromBody] LinkAccountDto request)
        {
            try
            {
                // Step 1: Validate link code WITHOUT consuming it (validate first)
                var (isLinkCodeValid, linkCodeUser) = await _service.ValidateLinkCodeAsync(request.LinkCode);
                
                if (!isLinkCodeValid || linkCodeUser == null)
                {
                    return BadRequest(new { error = "InvalidLinkCode", message = "Invalid or expired link code" });
                }

                // Step 2: Validate password
                var (passwordValid, passwordError) = await _service.ValidatePasswordAsync(request.Password);
                if (!passwordValid)
                {
                    return BadRequest(new { error = "InvalidPassword", message = passwordError });
                }

                // Step 3: Check password confirmation
                if (request.Password != request.PasswordConfirmation)
                {
                    return BadRequest(new { error = "PasswordMismatch", message = "Password and confirmation do not match" });
                }

                // Step 4: Check if email is already taken
                var (emailTaken, conflictingUserId) = await _service.CheckEmailTakenAsync(request.Email, linkCodeUser.Id);
                if (emailTaken)
                {
                    return Conflict(new { error = "DuplicateEmail", message = "Email is already in use by another account" });
                }

                // Step 5: All validations passed - NOW consume the link code
                var (isConsumed, consumedUser) = await _service.ConsumeLinkCodeAsync(request.LinkCode);
                if (!isConsumed || consumedUser == null)
                {
                    return BadRequest(new { error = "InvalidLinkCode", message = "Link code could not be consumed" });
                }

                // Step 6: Update user with email
                await _service.UpdateEmailAsync(linkCodeUser.Id, request.Email, null);
                
                // Step 7: Set initial password (no current password needed since we're setting it for the first time)
                // Use empty string as currentPassword since ChangePasswordAsync allows null PasswordHash
                await _service.ChangePasswordAsync(linkCodeUser.Id, "", request.Password, request.PasswordConfirmation);
                
                // Step 8: Get updated user
                var updatedUser = await _service.GetByIdAsync(linkCodeUser.Id);
                
                return Ok(new 
                {
                    user = updatedUser,
                    message = "Account successfully linked with email and password"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidArgument", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = "OperationFailed", message = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _service.DeleteAsync(id);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {id} not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<UserListDto>>> SearchUsers([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        /// <summary>
        /// Link an authenticated web app account to a Minecraft account using a link code
        /// </summary>
        /// <remarks>
        /// Used in the web-app-first flow where user already has email/password set.
        /// Requires authentication (JWT token in Authorization header).
        /// Requires a valid link code generated from Minecraft (/account link command).
        /// 
        /// If the link code points to an existing Minecraft account (with UUID), 
        /// the system will automatically merge the accounts (keep web app account as primary).
        /// </remarks>
        /// <param name="request">Link request with link code only</param>
        /// <returns>Updated user account with linked Minecraft UUID</returns>
        /// <response code="200">Account linked successfully</response>
        /// <response code="400">Invalid link code, or validation failed</response>
        /// <response code="401">User not authenticated</response>
        /// <response code="404">User not found</response>
        [Authorize]
        [HttpPost("link-minecraft-account")]
        public async Task<IActionResult> LinkMinecraftAccount([FromBody] LinkMinecraftAccountDto request)
        {
            try
            {
                // Extract user ID from JWT claims
                var userId = GetUserIdFromClaims(User);
                if (!userId.HasValue)
                {
                    return Unauthorized(new { error = "InvalidToken", message = "User claim missing or not authenticated." });
                }

                // Verify user exists
                var user = await _service.GetByIdAsync(userId.Value);
                if (user == null)
                {
                    return NotFound(new { error = "UserNotFound", message = $"User with ID {userId.Value} not found" });
                }

                // Link the account
                var linkedUser = await _service.LinkMinecraftAccountAsync(userId.Value, request.LinkCode);

                return Ok(new
                {
                    user = linkedUser,
                    message = "Minecraft account successfully linked to your web app account"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidArgument", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = "OperationFailed", message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = "NotFound", message = ex.Message });
            }
        }

        private int? GetUserIdFromClaims(ClaimsPrincipal principal)
        {
            var userIdClaim = principal.FindFirst("uid")
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirst(ClaimTypes.NameIdentifier);

            if (userIdClaim == null)
            {
                return null;
            }

            return int.TryParse(userIdClaim.Value, out var userId) ? userId : (int?)null;
        }
    }
}

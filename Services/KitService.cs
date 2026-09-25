using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class KitService : IKitService
    {
        private readonly IKitRepository _kitRepo;
        private readonly IUserRepository _userRepo;
        private readonly IItemBlueprintRepository _itemBlueprintRepo;
        private readonly ITitleBracketRepository _titleBracketRepo;
        private readonly IPermissionGroupRepository _permissionGroupRepo;
        private readonly ITitleService _titleService;
        private readonly IUserPermissionGroupService _userPermissionGroupService;
        private readonly IPermissionResolutionService _permissionResolutionService;
        private readonly IMapper _mapper;

        public KitService(
            IKitRepository kitRepo,
            IUserRepository userRepo,
            IItemBlueprintRepository itemBlueprintRepo,
            ITitleBracketRepository titleBracketRepo,
            IPermissionGroupRepository permissionGroupRepo,
            ITitleService titleService,
            IUserPermissionGroupService userPermissionGroupService,
            IPermissionResolutionService permissionResolutionService,
            IMapper mapper)
        {
            _kitRepo = kitRepo;
            _userRepo = userRepo;
            _itemBlueprintRepo = itemBlueprintRepo;
            _titleBracketRepo = titleBracketRepo;
            _permissionGroupRepo = permissionGroupRepo;
            _titleService = titleService;
            _userPermissionGroupService = userPermissionGroupService;
            _permissionResolutionService = permissionResolutionService;
            _mapper = mapper;
        }

        // ===== CRUD (FormWizard-only, DESIGN.md §4.0) =====

        public async Task<IEnumerable<KitDto>> GetAllAsync()
        {
            var kits = await _kitRepo.GetAllAsync();
            return _mapper.Map<IEnumerable<KitDto>>(kits);
        }

        public async Task<KitDto?> GetByIdAsync(int id)
        {
            var kit = await _kitRepo.GetByIdAsync(id);
            return kit == null ? null : _mapper.Map<KitDto>(kit);
        }

        public async Task<KitDto> CreateAsync(KitDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));

            await ValidateReferencesAsync(dto);

            var entity = _mapper.Map<Kit>(dto);
            entity.Contents = await BuildContentsAsync(dto.Contents);

            await _kitRepo.AddAsync(entity);
            return _mapper.Map<KitDto>(entity);
        }

        public async Task UpdateAsync(int id, KitDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));

            var existing = await _kitRepo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Kit with id {id} not found.");

            await ValidateReferencesAsync(dto);

            existing.Name = dto.Name;
            existing.Description = dto.Description;
            existing.HelmetId = dto.HelmetId;
            existing.ChestplateId = dto.ChestplateId;
            existing.LeggingsId = dto.LeggingsId;
            existing.BootsId = dto.BootsId;
            existing.ShieldId = dto.ShieldId;
            existing.HandId = dto.HandId;
            existing.MinTitleBracketId = dto.MinTitleBracketId;
            existing.RequiredPermissionGroupId = dto.RequiredPermissionGroupId;
            existing.RequiredPermissionNode = dto.RequiredPermissionNode;
            existing.GrantOnFirstJoin = dto.GrantOnFirstJoin;
            existing.CooldownSeconds = dto.CooldownSeconds;
            existing.CostAmount = dto.CostAmount;
            existing.CostCurrency = string.IsNullOrEmpty(dto.CostCurrency) ? null : Enum.Parse<KitCostCurrency>(dto.CostCurrency, true);
            existing.IsSinglePurchasePremium = dto.IsSinglePurchasePremium;
            existing.PremiumPriceGems = dto.PremiumPriceGems;

            existing.Contents.Clear();
            foreach (var content in await BuildContentsAsync(dto.Contents))
            {
                existing.Contents.Add(content);
            }

            await _kitRepo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _kitRepo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Kit with id {id} not found.");

            await _kitRepo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<KitDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _kitRepo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<KitDto>>(result);
        }

        // ===== Availability, claim, purchase, give, first-join grant (DESIGN.md §4.1) =====

        public async Task<List<KitAvailabilityDto>> GetAvailableForUserAsync(int userId)
        {
            var user = await _userRepo.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException($"User with id {userId} not found.");
            var kits = await _kitRepo.GetAllAsync();

            var result = new List<KitAvailabilityDto>();
            foreach (var kit in kits)
            {
                result.Add(await BuildAvailabilityAsync(kit, user));
            }
            return result;
        }

        private async Task<KitAvailabilityDto> BuildAvailabilityAsync(Kit kit, User user)
        {
            var dto = new KitAvailabilityDto
            {
                KitId = kit.Id,
                Name = kit.Name,
                Description = kit.Description,
                CostAmount = kit.CostAmount,
                CostCurrency = kit.CostCurrency?.ToString(),
                IsSinglePurchasePremium = kit.IsSinglePurchasePremium,
                PremiumPriceGems = kit.PremiumPriceGems
            };

            var (gatingPassed, gatingReason) = await CheckGatingAsync(kit, user);
            if (!gatingPassed)
            {
                dto.CanClaim = false;
                dto.DenialReason = gatingReason;
                return dto;
            }

            if (kit.IsSinglePurchasePremium)
            {
                var purchase = await _kitRepo.GetPurchaseAsync(kit.Id, user.Id);
                dto.IsPurchased = purchase != null;
                dto.CanClaim = purchase != null;
                if (purchase == null)
                {
                    dto.DenialReason = "This kit must be purchased before it can be claimed.";
                }
                return dto;
            }

            var cooldownReadyAt = await GetCooldownReadyAtAsync(kit, user.Id);
            if (cooldownReadyAt.HasValue && cooldownReadyAt.Value > DateTime.UtcNow)
            {
                dto.CanClaim = false;
                dto.CooldownExpiresAt = cooldownReadyAt;
                dto.DenialReason = $"On cooldown until {cooldownReadyAt:O}.";
                return dto;
            }

            if (HasInsufficientBalance(kit, user, out var balanceReason))
            {
                dto.CanClaim = false;
                dto.DenialReason = balanceReason;
                return dto;
            }

            dto.CanClaim = true;
            return dto;
        }

        public async Task<KitClaimResultDto> ClaimKitAsync(int userId, int kitId)
        {
            var user = await _userRepo.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException($"User with id {userId} not found.");
            var kit = await _kitRepo.GetByIdAsync(kitId)
                ?? throw new KeyNotFoundException($"Kit with id {kitId} not found.");

            // Never trust a caller's own prior gating check (DESIGN.md §4.1) - re-validate
            // everything here even though GetAvailableForUserAsync may have just said "yes".
            var (gatingPassed, gatingReason) = await CheckGatingAsync(kit, user);
            if (!gatingPassed)
                throw new InvalidOperationException(gatingReason ?? "Gating check failed.");

            var isPurchased = kit.IsSinglePurchasePremium
                && await _kitRepo.GetPurchaseAsync(kitId, userId) != null;

            if (kit.IsSinglePurchasePremium && !isPurchased)
                throw new InvalidOperationException("This kit must be purchased before it can be claimed.");

            User? userToPersist = null;

            // Purchased single-purchase-premium kits skip cooldown and cost entirely (DESIGN.md §2.4).
            if (!isPurchased)
            {
                var cooldownReadyAt = await GetCooldownReadyAtAsync(kit, userId);
                if (cooldownReadyAt.HasValue && cooldownReadyAt.Value > DateTime.UtcNow)
                    throw new InvalidOperationException($"Kit is on cooldown until {cooldownReadyAt:O}.");

                if (HasInsufficientBalance(kit, user, out var balanceReason))
                    throw new InvalidOperationException(balanceReason);

                if (kit.CostAmount.HasValue && kit.CostAmount.Value > 0 && kit.CostCurrency.HasValue)
                {
                    DeductCost(user, kit.CostCurrency.Value, kit.CostAmount.Value);
                    userToPersist = user;
                }
            }

            // Deduct-then-record, one SaveChanges call (DESIGN.md §5.1) - a failed deduction
            // above throws before this line is ever reached, so no claim row is written for it.
            var claim = new KitClaim { KitId = kitId, UserId = userId, ClaimedAt = DateTime.UtcNow };
            await _kitRepo.AddClaimAsync(claim, userToPersist);

            return _mapper.Map<KitClaimResultDto>(kit);
        }

        public async Task<KitPurchaseResultDto> PurchaseKitAsync(int userId, int kitId)
        {
            var user = await _userRepo.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException($"User with id {userId} not found.");
            var kit = await _kitRepo.GetByIdAsync(kitId)
                ?? throw new KeyNotFoundException($"Kit with id {kitId} not found.");

            if (!kit.IsSinglePurchasePremium)
                throw new ArgumentException("Kit is not a single-purchase premium kit.", nameof(kitId));

            var existing = await _kitRepo.GetPurchaseAsync(kitId, userId);
            if (existing != null)
                throw new InvalidOperationException("This kit has already been purchased by this user.");

            var price = kit.PremiumPriceGems ?? 0;
            if (user.Gems < price)
                throw new InvalidOperationException("Insufficient Gems to purchase this kit.");

            user.Gems -= price;
            var purchase = new KitPurchase
            {
                KitId = kitId,
                UserId = userId,
                PurchasedAt = DateTime.UtcNow,
                GemsPaid = price
            };
            await _kitRepo.AddPurchaseAsync(purchase, user);

            return new KitPurchaseResultDto
            {
                KitId = kitId,
                UserId = userId,
                GemsPaid = price,
                PurchasedAt = purchase.PurchasedAt
            };
        }

        public async Task<KitClaimResultDto> GiveKitAsync(int actorUserId, int targetUserId, int kitId)
        {
            _ = await _userRepo.GetByIdAsync(actorUserId)
                ?? throw new KeyNotFoundException($"User with id {actorUserId} not found.");
            _ = await _userRepo.GetByIdAsync(targetUserId)
                ?? throw new KeyNotFoundException($"User with id {targetUserId} not found.");
            var kit = await _kitRepo.GetByIdAsync(kitId)
                ?? throw new KeyNotFoundException($"Kit with id {kitId} not found.");

            // DESIGN.md §4.1 (§0b): GiveKitAsync deliberately bypasses gating, cooldown, and cost
            // - a staff override hands a kit to a player regardless of eligibility/affordability.
            var claim = new KitClaim { KitId = kitId, UserId = targetUserId, ClaimedAt = DateTime.UtcNow };
            await _kitRepo.AddClaimAsync(claim);

            // TODO(kits-phase2): DESIGN.md §4.1 also calls for a call into AuditLogService.Record
            // (actorUserId, targetUserId, "KitGranted", ...) here. That service does not exist -
            // no AuditLogEntry/AuditLogService/IAuditLogService exists anywhere in this codebase
            // yet, and docs/specs/user-management/IMPLEMENTATION_PLAN.md's own Phase 2 (the audit
            // log) is still "Draft" (verified by direct code read, 2026-09-25 - see
            // docs/specs/kits/IMPLEMENTATION_PLAN.md's §2 status note for the full explanation).
            // Wire the real call in here once that phase ships; don't stub a fake
            // IAuditLogService in the meantime.
            return _mapper.Map<KitClaimResultDto>(kit);
        }

        public async Task<List<KitClaimResultDto>> GrantFirstJoinKitsAsync(int userId)
        {
            var user = await _userRepo.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException($"User with id {userId} not found.");
            var kits = (await _kitRepo.GetAllAsync()).Where(k => k.GrantOnFirstJoin).ToList();

            var results = new List<KitClaimResultDto>();
            foreach (var kit in kits)
            {
                // Gating still applies (a brand-new player is bracket-0/group-less by definition,
                // so a mistakenly-flagged premium/high-title kit still won't auto-grant) - but
                // cost and cooldown are unconditionally ignored (DESIGN.md §4.4).
                var (gatingPassed, _) = await CheckGatingAsync(kit, user);
                if (!gatingPassed) continue;

                var claim = new KitClaim { KitId = kit.Id, UserId = userId, ClaimedAt = DateTime.UtcNow };
                await _kitRepo.AddClaimAsync(claim);
                results.Add(_mapper.Map<KitClaimResultDto>(kit));
            }

            return results;
        }

        // ===== Gating (DESIGN.md §3) =====

        /// <summary>All three gating fields are optional and combine as AND, checked in the
        /// order DESIGN.md §3 specifies: title bracket, then permission group, then permission
        /// node. KitService is a caller of ITitleService/IUserPermissionGroupService/
        /// IPermissionResolutionService here, never a reimplementer of their resolution logic.</summary>
        private async Task<(bool Passed, string? Reason)> CheckGatingAsync(Kit kit, User user)
        {
            if (kit.MinTitleBracketId.HasValue)
            {
                var brackets = await _titleBracketRepo.GetAllOrderedByMinExperienceAsync();
                var required = brackets.FirstOrDefault(b => b.Id == kit.MinTitleBracketId.Value);
                if (required != null)
                {
                    var resolution = await _titleService.ResolveAsync(user.ExperiencePoints, user.Gender);
                    var current = resolution.TitleBracketId.HasValue
                        ? brackets.FirstOrDefault(b => b.Id == resolution.TitleBracketId.Value)
                        : null;

                    // "At or above" the required bracket - brackets are ordered by MinExperience,
                    // so comparing MinExperience directly is comparing rank.
                    if (current == null || current.MinExperience < required.MinExperience)
                    {
                        return (false, $"Requires the '{required.NameFor(user.Gender)}' title or higher.");
                    }
                }
            }

            if (kit.RequiredPermissionGroupId.HasValue)
            {
                var memberships = await _userPermissionGroupService.GetByUserAsync(user.Id);
                var hasActive = memberships.Any(m =>
                    m.PermissionGroupId == kit.RequiredPermissionGroupId.Value && m.IsActive);
                if (!hasActive)
                {
                    var groupName = kit.RequiredPermissionGroup?.Name ?? $"group {kit.RequiredPermissionGroupId}";
                    return (false, $"Requires membership in the '{groupName}' permission group.");
                }
            }

            if (!string.IsNullOrWhiteSpace(kit.RequiredPermissionNode))
            {
                var check = await _permissionResolutionService.CheckAsync(user.Id, kit.RequiredPermissionNode);
                if (check == null || check.Result != PermissionResolutionResult.Granted)
                {
                    return (false, $"Requires the '{kit.RequiredPermissionNode}' permission.");
                }
            }

            return (true, null);
        }

        // ===== Cooldown / cost helpers (DESIGN.md §2.3/§5.1) =====

        /// <summary>Null if the kit has never been claimed by this user, or has no cooldown.
        /// Otherwise the instant the cooldown lifts - the caller compares this against
        /// DateTime.UtcNow (readyAt > now => still blocked; readyAt == now => allowed, per
        /// DESIGN.md §2.3's "must be in the past").</summary>
        private async Task<DateTime?> GetCooldownReadyAtAsync(Kit kit, int userId)
        {
            if (kit.CooldownSeconds <= 0) return null;

            var lastClaim = await _kitRepo.GetLastClaimAsync(kit.Id, userId);
            return lastClaim?.ClaimedAt.AddSeconds(kit.CooldownSeconds);
        }

        private static bool HasInsufficientBalance(Kit kit, User user, out string? reason)
        {
            reason = null;
            if (!kit.CostAmount.HasValue || kit.CostAmount.Value <= 0 || !kit.CostCurrency.HasValue)
                return false;

            var balance = kit.CostCurrency.Value == KitCostCurrency.Coins ? user.Coins : user.Gems;
            if (balance >= kit.CostAmount.Value) return false;

            reason = $"Insufficient {kit.CostCurrency.Value} to claim this kit.";
            return true;
        }

        private static void DeductCost(User user, KitCostCurrency currency, int amount)
        {
            if (currency == KitCostCurrency.Coins) user.Coins -= amount;
            else user.Gems -= amount;
        }

        // ===== CRUD validation / M2M helpers =====

        private async Task ValidateReferencesAsync(KitDto dto)
        {
            foreach (var itemBlueprintId in new[] { dto.HelmetId, dto.ChestplateId, dto.LeggingsId, dto.BootsId, dto.ShieldId, dto.HandId })
            {
                if (!itemBlueprintId.HasValue) continue;
                if (await _itemBlueprintRepo.GetByIdAsync(itemBlueprintId.Value) == null)
                    throw new ArgumentException($"ItemBlueprint with id {itemBlueprintId} not found.");
            }

            if (dto.MinTitleBracketId.HasValue)
            {
                var brackets = await _titleBracketRepo.GetAllOrderedByMinExperienceAsync();
                if (!brackets.Any(b => b.Id == dto.MinTitleBracketId.Value))
                    throw new ArgumentException($"TitleBracket with id {dto.MinTitleBracketId} not found.");
            }

            if (dto.RequiredPermissionGroupId.HasValue)
            {
                if (await _permissionGroupRepo.GetByIdAsync(dto.RequiredPermissionGroupId.Value) == null)
                    throw new ArgumentException($"PermissionGroup with id {dto.RequiredPermissionGroupId} not found.");
            }
        }

        private async Task<List<KitContent>> BuildContentsAsync(List<KitContentDto>? contents)
        {
            var result = new List<KitContent>();
            if (contents == null || contents.Count == 0) return result;

            var seenSlots = new HashSet<int>();
            foreach (var content in contents)
            {
                if (!seenSlots.Add(content.SlotIndex))
                    throw new ArgumentException($"Duplicate SlotIndex {content.SlotIndex} in Contents - each slot may hold at most one item.");

                if (await _itemBlueprintRepo.GetByIdAsync(content.ItemBlueprintId) == null)
                    throw new ArgumentException($"ItemBlueprint with id {content.ItemBlueprintId} not found.");

                result.Add(new KitContent
                {
                    SlotIndex = content.SlotIndex,
                    ItemBlueprintId = content.ItemBlueprintId,
                    Quantity = content.Quantity
                });
            }
            return result;
        }
    }
}

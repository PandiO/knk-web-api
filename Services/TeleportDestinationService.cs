using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Domain warps (docs/specs/teleport/DESIGN.md §3.7) and teleport fees (§3.5), KNG-17 Phase 5.
    /// <para>
    /// Access rule, first failure wins: not a destination (disabled, no Location, AllowEntry off)
    /// → never listed; title (ExperiencePoints ≥ the bracket's MinExperience); premium tier (the
    /// player's highest active premium group's Weight ≥ the required group's); discovery (a
    /// UserDomainDiscovery row, only when TeleportRequiresDiscovery); price (Gems ≥
    /// TeleportPriceGems).
    /// </para>
    /// <para>
    /// Charges run under the player's row lock (IUserRepository.RunWithUsersLockedAsync, which the
    /// ledger posting joins), so the evaluation, the idempotency lookup, the void check and the
    /// posting see one consistent state. Charges are ledger postings in the plugin's idempotency
    /// scope with reason TELEPORT_FEE; refunds are ledger reversals.
    /// </para>
    /// </summary>
    public class TeleportDestinationService : ITeleportDestinationService
    {
        public const string Component = "TeleportDestinationService";
        public const string WarpSourceType = "Domain";
        public const string RequestSourceType = "TeleportRequest";
        public const string BackSourceType = "TeleportBack";

        /// <summary>The kinds of place a /back returns to (plugin BackKind config keys, KNG-42).</summary>
        public static readonly IReadOnlySet<string> BackKinds = new HashSet<string> { "death", "warps", "teleport", "spawn" };

        /// <summary>Highest coin fee a teleport request may carry (the coin balance cap).</summary>
        public const int MaxRequestFeeCoins = BalanceLimits.MaxCoins;

        private static readonly Regex KeyPattern = new("^[A-Za-z0-9:_.\\-]{1,100}$", RegexOptions.Compiled);

        private static readonly string[] TypeOrder = { "Town", "District", "Structure", "GateStructure" };

        private readonly ITeleportDestinationRepository _repo;
        private readonly IUserRepository _users;
        private readonly IUserPermissionGroupService _groups;
        private readonly IDiscoveryRepository _discoveries;
        private readonly ITitleBracketRepository _titleBrackets;
        private readonly IPermissionGroupRepository _permissionGroups;
        private readonly ICurrencyService _currency;
        private readonly ICurrencyRepository _ledger;
        private readonly ILogger<TeleportDestinationService> _logger;

        public TeleportDestinationService(
            ITeleportDestinationRepository repo,
            IUserRepository users,
            IUserPermissionGroupService groups,
            IDiscoveryRepository discoveries,
            ITitleBracketRepository titleBrackets,
            IPermissionGroupRepository permissionGroups,
            ICurrencyService currency,
            ICurrencyRepository ledger,
            ILogger<TeleportDestinationService> logger)
        {
            _repo = repo;
            _users = users;
            _groups = groups;
            _discoveries = discoveries;
            _titleBrackets = titleBrackets;
            _permissionGroups = permissionGroups;
            _currency = currency;
            _ledger = ledger;
            _logger = logger;
        }

        // ===== Reads =====

        public async Task<List<TeleportDestinationDto>> ListForUserAsync(int userId)
        {
            var user = await _users.GetByIdAsync(userId) ?? throw new KeyNotFoundException($"User {userId} not found.");
            var domains = (await _repo.GetEnabledAsync()).Where(IsDestination).ToList();
            var access = await LoadAccessAsync(user, domains);
            return domains
                .Select(d => Evaluate(d, user, access))
                .OrderBy(d => TypeRank(d.DomainType))
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.DomainId)
                .ToList();
        }

        // ===== Charges =====

        public async Task<TeleportChargeResultDto> ChargeAsync(int domainId, TeleportChargeRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequireKey(request.IdempotencyKey);
            if (request.UserId <= 0) throw new KeyNotFoundException($"User {request.UserId} not found.");

            TeleportChargeResultDto? result = null;
            await _users.RunWithUsersLockedAsync(new[] { request.UserId }, async () =>
            {
                await RequireNotVoidAsync(request.IdempotencyKey);
                var user = await _users.GetByIdAsync(request.UserId) ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
                var domain = await _repo.GetByIdAsync(domainId);
                if (domain == null || domain.Location == null)
                {
                    throw new TeleportDestinationException(TeleportDestinationException.NotAvailable,
                        domain == null ? "That place doesn't exist." : $"You can't teleport to {domain.Name}.");
                }

                // A retry of an attempt that was already charged: the same answer, even if the
                // player can no longer afford it (they already paid) or staff just closed it.
                var existing = await _ledger.FindByIdempotencyAsync(CurrencyIdempotencyScopes.Plugin, request.IdempotencyKey);
                if (existing != null)
                {
                    await RequireSameChargeAsync(existing, user.Id, WarpSourceType, domainId.ToString());
                    result = new TeleportChargeResultDto
                    {
                        Currency = Currency.Gems.ToString(),
                        Charged = -UserLeg(existing, user.Id)!.Amount,
                        NewBalance = user.Gems,
                        Replayed = true,
                        TransactionPublicId = existing.PublicId,
                        Destination = Unlocked(ToDestination(domain, user.Gender))
                    };
                    return;
                }

                if (!IsDestination(domain))
                {
                    throw new TeleportDestinationException(TeleportDestinationException.NotAvailable,
                        $"You can't teleport to {domain.Name}.");
                }

                var access = await LoadAccessAsync(user, new[] { domain });
                var evaluated = Evaluate(domain, user, access);
                if (!request.BypassRequirements && !evaluated.RequirementsMet)
                {
                    throw new TeleportDestinationException(evaluated.LockCode!, evaluated.LockReason!);
                }

                var price = request.BypassCost ? 0 : domain.TeleportPriceGems;
                if (price <= 0)
                {
                    result = new TeleportChargeResultDto
                    {
                        Currency = Currency.Gems.ToString(),
                        Charged = 0,
                        NewBalance = user.Gems,
                        Destination = Unlocked(evaluated)
                    };
                    return;
                }

                var posting = await SpendAsync(user.Id, Currency.Gems, price, request.IdempotencyKey,
                    WarpSourceType, domain.Id.ToString(), $"Teleport to {domain.Name}",
                    new { domainId = domain.Id, domainName = domain.Name, bypassRequirements = request.BypassRequirements });
                result = ToChargeResult(posting, user.Id, Currency.Gems, Unlocked(evaluated));
            });

            if (result!.Charged > 0 && !result.Replayed)
            {
                _logger.LogInformation("Warp charge: user {UserId} paid {Gems} gems for domain {DomainId} (key {Key})",
                    request.UserId, result.Charged, domainId, request.IdempotencyKey);
            }
            return result;
        }

        public Task<TeleportChargeResultDto> ChargeRequestFeeAsync(TeleportRequestFeeDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return ChargeFlatCoinsAsync(request.UserId, request.AmountCoins, request.IdempotencyKey,
                RequestSourceType, request.OtherUserId?.ToString(), "Teleport request",
                new { otherUserId = request.OtherUserId });
        }

        public Task<TeleportChargeResultDto> ChargeBackFeeAsync(TeleportBackFeeDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var kind = request.BackKind?.Trim().ToLowerInvariant();
            if (kind != null && !BackKinds.Contains(kind))
            {
                throw new ArgumentException($"backKind must be one of {string.Join(", ", BackKinds)}.");
            }
            return ChargeFlatCoinsAsync(request.UserId, request.AmountCoins, request.IdempotencyKey,
                BackSourceType, kind, "Teleport back (/back)", new { backKind = kind });
        }

        /// <summary>
        /// A flat coin fee (a /tpa's, a /back's) charged once per idempotency key under the player's
        /// row lock; a retry with the same key replays the first charge, a refunded or voided key is
        /// refused.
        /// </summary>
        private async Task<TeleportChargeResultDto> ChargeFlatCoinsAsync(int userId, int amountCoins, string key,
            string sourceType, string? sourceRef, string reason, object metadata)
        {
            RequireKey(key);
            if (amountCoins < 1 || amountCoins > MaxRequestFeeCoins)
            {
                throw new ArgumentException($"amountCoins must be between 1 and {MaxRequestFeeCoins}.");
            }
            if (userId <= 0) throw new KeyNotFoundException($"User {userId} not found.");

            TeleportChargeResultDto? result = null;
            await _users.RunWithUsersLockedAsync(new[] { userId }, async () =>
            {
                await RequireNotVoidAsync(key);
                var user = await _users.GetByIdAsync(userId) ?? throw new KeyNotFoundException($"User {userId} not found.");

                var existing = await _ledger.FindByIdempotencyAsync(CurrencyIdempotencyScopes.Plugin, key);
                if (existing != null)
                {
                    await RequireSameChargeAsync(existing, user.Id, sourceType, sourceRef);
                    var leg = UserLeg(existing, user.Id)!;
                    if (leg.Currency != Currency.Coins || -leg.Amount != amountCoins)
                    {
                        throw KeyReuse(key);
                    }
                    result = new TeleportChargeResultDto
                    {
                        Currency = Currency.Coins.ToString(),
                        Charged = -leg.Amount,
                        NewBalance = user.Coins,
                        Replayed = true,
                        TransactionPublicId = existing.PublicId
                    };
                    return;
                }

                var posting = await SpendAsync(user.Id, Currency.Coins, amountCoins, key, sourceType, sourceRef,
                    reason, metadata);
                result = ToChargeResult(posting, user.Id, Currency.Coins, null);
            });
            return result!;
        }

        public async Task<TeleportRefundResultDto> RefundAsync(TeleportRefundRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequireKey(request.IdempotencyKey);
            if (request.UserId <= 0) throw new KeyNotFoundException($"User {request.UserId} not found.");
            if (request.Reason?.Length > 200) throw new ArgumentException("reason may be at most 200 characters.");

            TeleportRefundResultDto? result = null;
            await _users.RunWithUsersLockedAsync(new[] { request.UserId }, async () =>
            {
                var user = await _users.GetByIdAsync(request.UserId) ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
                var charge = await _ledger.FindByIdempotencyAsync(CurrencyIdempotencyScopes.Plugin, request.IdempotencyKey);
                if (charge == null)
                {
                    // Nothing charged (yet): make sure nothing will be. A charge still on its way
                    // with this key finds it void under the same row lock; the marker is a row, so
                    // a late duplicate is refused after an API restart and on every API instance.
                    if (await _repo.VoidFeeKeyAsync(new TeleportFeeVoid
                        {
                            IdempotencyKey = request.IdempotencyKey,
                            UserId = user.Id,
                            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
                        }))
                    {
                        _logger.LogInformation("Teleport refund before any charge: key {Key} of user {UserId} voided", request.IdempotencyKey, user.Id);
                    }
                    result = new TeleportRefundResultDto { Refunded = false };
                    return;
                }

                var leg = UserLeg(charge, user.Id);
                if (charge.ReasonCode != CurrencyReasons.TeleportFee || leg == null)
                {
                    throw KeyReuse(request.IdempotencyKey);
                }

                var prior = await _ledger.FindReversalOfAsync(charge.Id);
                if (prior != null)
                {
                    result = Refunded(leg, user, replayed: true);
                    return;
                }

                try
                {
                    var ctx = CurrencyContext.ForSystem(Component, CurrencyReasons.Reversal, $"reverse:{charge.Id}",
                        string.IsNullOrWhiteSpace(request.Reason) ? "Teleport refund: the teleport didn't happen" : $"Teleport refund: {request.Reason.Trim()}");
                    var posting = await _currency.ReverseAsync(charge.Id, new ReversalOptions(), ctx);
                    result = Refunded(leg, user, replayed: posting.Replayed, posting.Balances.TryGetValue(user.Id, out var b) ? b : null);
                    _logger.LogInformation("Teleport refund: {Amount} {Currency} back to user {UserId} (key {Key}, charge {PublicId})",
                        -leg.Amount, leg.Currency, user.Id, request.IdempotencyKey, charge.PublicId);
                }
                catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.AlreadyReversed)
                {
                    result = Refunded(leg, user, replayed: true);
                }
                catch (CurrencyException ex)
                {
                    _logger.LogWarning("Teleport refund refused for user {UserId} (key {Key}): {Message}", user.Id, request.IdempotencyKey, ex.Message);
                    throw new TeleportDestinationException(TeleportDestinationException.LedgerRefused, ex.Message);
                }
            });
            return result!;
        }

        // ===== Authoring =====

        public async Task ValidateSettingsAsync(IDomainTeleportSettingsDto settings)
        {
            if (settings?.TeleportEnabled == null) return;

            var price = settings.TeleportPriceGems ?? 0;
            if (price < 0 || price > BalanceLimits.MaxGems)
            {
                throw new ArgumentException($"Teleport price must be between 0 and {BalanceLimits.MaxGems} gems.");
            }
            if (settings.TeleportMinTitleBracketId.HasValue)
            {
                var brackets = await _titleBrackets.GetAllOrderedByMinExperienceAsync();
                if (brackets.All(b => b.Id != settings.TeleportMinTitleBracketId.Value))
                {
                    throw new ArgumentException($"TitleBracket with id {settings.TeleportMinTitleBracketId} not found.");
                }
            }
            if (settings.TeleportMinPremiumGroupId.HasValue)
            {
                var group = await _permissionGroups.GetByIdAsync(settings.TeleportMinPremiumGroupId.Value)
                    ?? throw new ArgumentException($"PermissionGroup with id {settings.TeleportMinPremiumGroupId} not found.");
                if (!group.IsPremiumTier)
                {
                    throw new ArgumentException($"'{group.Name}' isn't a premium tier; pick a premium tier group for the teleport requirement.");
                }
            }
        }

        // ===== Evaluation =====

        private sealed record Access(int? PremiumWeight, HashSet<int> Discovered);

        public static bool IsDestination(Domain domain) =>
            domain.TeleportEnabled && domain.AllowEntry && domain.LocationId != null && domain.Location != null;

        private async Task<Access> LoadAccessAsync(User user, IReadOnlyCollection<Domain> domains)
        {
            int? premiumWeight = null;
            if (domains.Any(d => d.TeleportMinPremiumGroupId != null))
            {
                premiumWeight = (await _groups.GetActivePremiumTierAsync(user.Id))?.Weight;
            }
            var needDiscovery = domains.Where(d => d.TeleportRequiresDiscovery).Select(d => d.Id).ToList();
            var discovered = needDiscovery.Count == 0
                ? new HashSet<int>()
                : await _discoveries.GetDiscoveredDomainIdsAsync(user.Id, needDiscovery);
            return new Access(premiumWeight, discovered);
        }

        private static TeleportDestinationDto Evaluate(Domain domain, User user, Access access)
        {
            var dto = ToDestination(domain, user.Gender);
            string? code = null;
            string? reason = null;

            if (domain.TeleportMinTitleBracket != null && user.ExperiencePoints < domain.TeleportMinTitleBracket.MinExperience)
            {
                code = TeleportDestinationException.TitleTooLow;
                reason = $"Reach title {dto.MinTitleName} to unlock";
            }
            else if (domain.TeleportMinPremiumGroup != null
                && (access.PremiumWeight == null || access.PremiumWeight.Value < domain.TeleportMinPremiumGroup.Weight))
            {
                code = TeleportDestinationException.PremiumTooLow;
                reason = $"Premium tier {dto.MinPremiumTierName} or higher required";
            }
            else if (domain.TeleportRequiresDiscovery && !access.Discovered.Contains(domain.Id))
            {
                code = TeleportDestinationException.NotDiscovered;
                reason = $"Discover {domain.Name} first";
            }

            dto.RequirementsMet = code == null;
            dto.CanAfford = user.Gems >= domain.TeleportPriceGems;
            if (code == null && !dto.CanAfford)
            {
                code = TeleportDestinationException.InsufficientGems;
                reason = "You don't have enough gems to teleport to this location!";
            }
            dto.Available = code == null;
            dto.LockCode = code;
            dto.LockReason = reason;
            return dto;
        }

        private static TeleportDestinationDto ToDestination(Domain domain, Gender? gender) => new()
        {
            DomainId = domain.Id,
            Name = domain.Name,
            DomainType = DomainType(domain),
            Location = new TeleportLocationDto
            {
                World = domain.Location!.World,
                X = domain.Location.X,
                Y = domain.Location.Y,
                Z = domain.Location.Z,
                Yaw = domain.Location.Yaw,
                Pitch = domain.Location.Pitch
            },
            PriceGems = domain.TeleportPriceGems,
            MinTitleName = domain.TeleportMinTitleBracket?.NameFor(gender),
            MinPremiumTierName = domain.TeleportMinPremiumGroup?.Name,
            RequiresDiscovery = domain.TeleportRequiresDiscovery
        };

        /// <summary>The destination as returned by an accepted charge: nothing locks it any more.</summary>
        private static TeleportDestinationDto Unlocked(TeleportDestinationDto dto)
        {
            dto.Available = true;
            dto.RequirementsMet = true;
            dto.CanAfford = true;
            dto.LockCode = null;
            dto.LockReason = null;
            return dto;
        }

        public static string DomainType(Domain domain) => domain switch
        {
            GateStructure => "GateStructure",
            Structure => "Structure",
            District => "District",
            Town => "Town",
            _ => "Domain"
        };

        private static int TypeRank(string type)
        {
            var index = Array.IndexOf(TypeOrder, type);
            return index < 0 ? TypeOrder.Length : index;
        }

        // ===== Ledger helpers =====

        private async Task<PostingResult> SpendAsync(int userId, Currency currency, long amount, string key,
            string sourceType, string? sourceRef, string reason, object metadata)
        {
            var ctx = new CurrencyContext
            {
                IdempotencyKey = key,
                IdempotencyScope = CurrencyIdempotencyScopes.Plugin,
                ReasonCode = CurrencyReasons.TeleportFee,
                Reason = reason.Length > 500 ? reason[..500] : reason,
                // The player pays for their own teleport; the plugin relays it.
                Initiator = CurrencyInitiator.Player,
                InitiatorUserId = userId,
                InitiatorComponent = Component,
                SourceType = sourceType,
                SourceRef = sourceRef,
                MetadataJson = JsonSerializer.Serialize(metadata)
            };
            try
            {
                return await _currency.SpendAsync(userId, currency, amount, ctx);
            }
            catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.InsufficientFunds)
            {
                throw currency == Currency.Gems
                    ? new TeleportDestinationException(TeleportDestinationException.InsufficientGems, "You don't have enough gems to teleport to this location!")
                    : new TeleportDestinationException(TeleportDestinationException.InsufficientCoins, "You don't have enough coins to send this teleport request!");
            }
            catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.IdempotencyKeyReuse)
            {
                throw KeyReuse(key);
            }
            catch (CurrencyException ex)
            {
                throw new TeleportDestinationException(TeleportDestinationException.LedgerRefused, ex.Message);
            }
        }

        /// <summary>A replayed key must name the same kind of charge for the same player and
        /// target, and must not have been refunded.</summary>
        private async Task RequireSameChargeAsync(CurrencyTransaction existing, int userId, string sourceType, string? sourceRef)
        {
            if (existing.ReasonCode != CurrencyReasons.TeleportFee
                || UserLeg(existing, userId) == null
                || existing.SourceType != sourceType
                || !string.Equals(existing.SourceRef, sourceRef, StringComparison.Ordinal))
            {
                throw KeyReuse(existing.IdempotencyKey);
            }
            if (await _ledger.FindReversalOfAsync(existing.Id) != null)
            {
                throw new TeleportDestinationException(TeleportDestinationException.Refunded,
                    "This teleport was cancelled and refunded; try again.");
            }
        }

        private static CurrencyEntry? UserLeg(CurrencyTransaction tx, int userId) =>
            tx.Entries.FirstOrDefault(e => e.AccountKind == CurrencyAccountKind.User && e.UserId == userId);

        private static TeleportChargeResultDto ToChargeResult(PostingResult posting, int userId, Currency currency, TeleportDestinationDto? destination)
        {
            var entry = posting.Entries.First(e => e.UserId == userId);
            return new TeleportChargeResultDto
            {
                Currency = currency.ToString(),
                Charged = -entry.Amount,
                NewBalance = entry.BalanceAfter,
                Replayed = posting.Replayed,
                TransactionPublicId = posting.PublicId,
                Destination = destination
            };
        }

        private static TeleportRefundResultDto Refunded(CurrencyEntry leg, User user, bool replayed, BalancesDto? balances = null) => new()
        {
            Refunded = true,
            Currency = leg.Currency.ToString(),
            Amount = -leg.Amount,
            NewBalance = balances != null
                ? (leg.Currency == Currency.Gems ? balances.Gems : balances.Coins)
                : (leg.Currency == Currency.Gems ? user.Gems : user.Coins),
            Replayed = replayed
        };

        private async Task RequireNotVoidAsync(string key)
        {
            if (await _repo.IsFeeKeyVoidAsync(key))
            {
                throw new TeleportDestinationException(TeleportDestinationException.Refunded,
                    "This teleport was already cancelled; try again.");
            }
        }

        private static void RequireKey(string? key)
        {
            if (string.IsNullOrEmpty(key) || !KeyPattern.IsMatch(key))
            {
                throw new ArgumentException("idempotencyKey must be 1-100 characters of A-Z, a-z, 0-9, ':', '_', '.', '-'.");
            }
        }

        private static TeleportDestinationException KeyReuse(string key) =>
            new(TeleportDestinationException.IdempotencyKeyReuse, $"Idempotency key '{key}' was already used for a different charge.");
    }

    /// <summary>
    /// Applies the warp settings of a Town/District/Structure DTO to the entity (teleport DESIGN.md
    /// §3.7.1). A DTO without teleportEnabled (a form that doesn't show the teleport fields) leaves
    /// all five untouched; otherwise all five are set, null ids meaning "no requirement".
    /// </summary>
    public static class DomainTeleportSettings
    {
        public static void Apply(Domain target, IDomainTeleportSettingsDto? settings)
        {
            if (settings?.TeleportEnabled == null) return;
            target.TeleportEnabled = settings.TeleportEnabled.Value;
            target.TeleportPriceGems = settings.TeleportPriceGems ?? 0;
            target.TeleportMinTitleBracketId = settings.TeleportMinTitleBracketId;
            target.TeleportMinPremiumGroupId = settings.TeleportMinPremiumGroupId;
            target.TeleportRequiresDiscovery = settings.TeleportRequiresDiscovery ?? false;
        }
    }
}

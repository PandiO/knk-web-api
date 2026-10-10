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
    /// UserDomainDiscovery row, only when TeleportRequiresDiscovery); price (the player can pay it).
    /// </para>
    /// <para>
    /// Prices (Linear KNG-41): the player's permission groups can price /tpa, /warp and /spawn
    /// (TeleportGroupPolicy) - a multiple of the default price or a fixed price in coins, gems
    /// and/or XP. No group price = the default: the request's coin fee from the plugin's config,
    /// the domain's TeleportPriceGems, a free /spawn. A /back's flat fee (KNG-42) isn't group-priced.
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
        public const string SpawnSourceType = "TeleportSpawn";

        /// <summary>The kinds of place a /back returns to (plugin BackKind config keys, KNG-42).</summary>
        public static readonly IReadOnlySet<string> BackKinds = new HashSet<string> { "death", "warps", "teleport", "spawn" };

        /// <summary>Highest coin fee a teleport request may carry (the coin balance cap).</summary>
        public const int MaxRequestFeeCoins = BalanceLimits.MaxCoins;

        private static readonly Regex KeyPattern = new("^[A-Za-z0-9:_.\\-]{1,100}$", RegexOptions.Compiled);

        private static readonly string[] TypeOrder = { "Town", "District", "Structure", "GateStructure" };

        private const string WarpPurpose = "to teleport to this location";
        private const string RequestPurpose = "to send this teleport request";
        private const string BackPurpose = "to teleport back";
        private const string SpawnPurpose = "to teleport to spawn";

        private readonly ITeleportDestinationRepository _repo;
        private readonly IUserRepository _users;
        private readonly IUserPermissionGroupService _groups;
        private readonly IDiscoveryRepository _discoveries;
        private readonly ITitleBracketRepository _titleBrackets;
        private readonly IPermissionGroupRepository _permissionGroups;
        private readonly ICurrencyService _currency;
        private readonly ICurrencyRepository _ledger;
        private readonly ILogger<TeleportDestinationService> _logger;
        private readonly ITitleProgressionService? _titleProgression;
        private readonly IPlayerNotificationQueue? _notifications;

        /// <param name="titleProgression">Runs title progression when a group's XP price (or its
        /// refund) changes a player's XP (KNG-41); without it (some tests) titles aren't updated.</param>
        /// <param name="notifications">Tells the player in game about such a title change; optional.</param>
        public TeleportDestinationService(
            ITeleportDestinationRepository repo,
            IUserRepository users,
            IUserPermissionGroupService groups,
            IDiscoveryRepository discoveries,
            ITitleBracketRepository titleBrackets,
            IPermissionGroupRepository permissionGroups,
            ICurrencyService currency,
            ICurrencyRepository ledger,
            ILogger<TeleportDestinationService> logger,
            ITitleProgressionService? titleProgression = null,
            IPlayerNotificationQueue? notifications = null)
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
            _titleProgression = titleProgression;
            _notifications = notifications;
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

        public async Task<TeleportPolicyDto> GetPolicyAsync(int userId)
        {
            var user = await _users.GetByIdAsync(userId) ?? throw new KeyNotFoundException($"User {userId} not found.");
            var chain = await LoadGroupChainAsync(user.Id);
            return new TeleportPolicyDto
            {
                UserId = user.Id,
                Request = TeleportGroupPolicy.ToDto(TeleportGroupPolicy.Resolve(chain, TeleportFeeKind.Request)),
                Warp = TeleportGroupPolicy.ToDto(TeleportGroupPolicy.Resolve(chain, TeleportFeeKind.Warp)),
                Spawn = TeleportGroupPolicy.ToDto(TeleportGroupPolicy.Resolve(chain, TeleportFeeKind.Spawn))
            };
        }

        // ===== Charges =====

        public async Task<TeleportChargeResultDto> ChargeAsync(int domainId, TeleportChargeRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequireKey(request.IdempotencyKey);
            if (request.UserId <= 0) throw new KeyNotFoundException($"User {request.UserId} not found.");

            TeleportChargeResultDto? result = null;
            Dictionary<int, TitleChangeResultDto> titleChanges = new();
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
                    result = Replay(existing, user, Unlocked(ToDestination(domain, user.Gender)));
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

                var price = request.BypassCost ? Array.Empty<TeleportPriceLeg>() : WarpPrice(domain, access.Warp);
                if (price.Count == 0)
                {
                    result = Free(user, Currency.Gems, Unlocked(evaluated));
                    return;
                }

                var posting = await SpendAsync(user, price, request.IdempotencyKey, WarpSourceType, domain.Id.ToString(),
                    $"Teleport to {domain.Name}", WarpPurpose,
                    new
                    {
                        domainId = domain.Id,
                        domainName = domain.Name,
                        bypassRequirements = request.BypassRequirements,
                        priceGroup = access.Warp.PriceGroup?.Name,
                        priceMode = access.Warp.Price?.Mode.ToString()
                    }, titleChanges);
                result = ToChargeResult(posting, user.Id, Unlocked(evaluated));
            });

            await NotifyTitleChangesAsync(titleChanges);
            if (result!.Payments.Count > 0 && !result.Replayed)
            {
                _logger.LogInformation("Warp charge: user {UserId} paid {Price} for domain {DomainId} (key {Key})",
                    request.UserId, Describe(result.Payments), domainId, request.IdempotencyKey);
            }
            return result;
        }

        public Task<TeleportChargeResultDto> ChargeRequestFeeAsync(TeleportRequestFeeDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.AmountCoins < 0 || request.AmountCoins > MaxRequestFeeCoins)
            {
                throw new ArgumentException($"amountCoins must be between 0 and {MaxRequestFeeCoins}.");
            }
            return ChargeFeeAsync(request.UserId, request.IdempotencyKey, Currency.Coins, RequestSourceType,
                request.OtherUserId?.ToString(), "Teleport request", RequestPurpose,
                async user =>
                {
                    var policy = TeleportGroupPolicy.Resolve(await LoadGroupChainAsync(user.Id), TeleportFeeKind.Request);
                    var price = policy.PriceFor(new[] { new TeleportPriceLeg(Currency.Coins, request.AmountCoins) });
                    return (price, new
                    {
                        otherUserId = request.OtherUserId,
                        defaultCoins = request.AmountCoins,
                        priceGroup = policy.PriceGroup?.Name,
                        priceMode = policy.Price?.Mode.ToString()
                    });
                });
        }

        public Task<TeleportChargeResultDto> ChargeSpawnFeeAsync(TeleportSpawnFeeDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return ChargeFeeAsync(request.UserId, request.IdempotencyKey, Currency.Coins, SpawnSourceType, null,
                "Teleport to spawn", SpawnPurpose,
                async user =>
                {
                    var policy = TeleportGroupPolicy.Resolve(await LoadGroupChainAsync(user.Id), TeleportFeeKind.Spawn);
                    return (policy.PriceFor(Array.Empty<TeleportPriceLeg>()), new
                    {
                        priceGroup = policy.PriceGroup?.Name
                    });
                });
        }

        public Task<TeleportChargeResultDto> ChargeBackFeeAsync(TeleportBackFeeDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var kind = request.BackKind?.Trim().ToLowerInvariant();
            if (kind != null && !BackKinds.Contains(kind))
            {
                throw new ArgumentException($"backKind must be one of {string.Join(", ", BackKinds)}.");
            }
            if (request.AmountCoins < 1 || request.AmountCoins > MaxRequestFeeCoins)
            {
                throw new ArgumentException($"amountCoins must be between 1 and {MaxRequestFeeCoins}.");
            }
            IReadOnlyList<TeleportPriceLeg> price = new[] { new TeleportPriceLeg(Currency.Coins, request.AmountCoins) };
            return ChargeFeeAsync(request.UserId, request.IdempotencyKey, Currency.Coins, BackSourceType, kind,
                "Teleport back (/back)", BackPurpose,
                _ => Task.FromResult<(IReadOnlyList<TeleportPriceLeg>, object)>((price, new { backKind = kind })));
        }

        /// <summary>
        /// A teleport fee that isn't a warp (a /tpa's, a /spawn's, a /back's), charged once per
        /// idempotency key under the player's row lock: a retry with the same key replays the first
        /// charge (whatever the price is now), a refunded or voided key is refused. A free price
        /// posts nothing.
        /// </summary>
        private async Task<TeleportChargeResultDto> ChargeFeeAsync(int userId, string key, Currency freeCurrency,
            string sourceType, string? sourceRef, string reason, string purpose,
            Func<User, Task<(IReadOnlyList<TeleportPriceLeg> Price, object Metadata)>> priceOf)
        {
            RequireKey(key);
            if (userId <= 0) throw new KeyNotFoundException($"User {userId} not found.");

            TeleportChargeResultDto? result = null;
            Dictionary<int, TitleChangeResultDto> titleChanges = new();
            await _users.RunWithUsersLockedAsync(new[] { userId }, async () =>
            {
                await RequireNotVoidAsync(key);
                var user = await _users.GetByIdAsync(userId) ?? throw new KeyNotFoundException($"User {userId} not found.");

                var existing = await _ledger.FindByIdempotencyAsync(CurrencyIdempotencyScopes.Plugin, key);
                if (existing != null)
                {
                    await RequireSameChargeAsync(existing, user.Id, sourceType, sourceRef);
                    result = Replay(existing, user, null);
                    return;
                }

                var (price, metadata) = await priceOf(user);
                if (price.Count == 0)
                {
                    result = Free(user, freeCurrency, null);
                    return;
                }
                var posting = await SpendAsync(user, price, key, sourceType, sourceRef, reason, purpose, metadata, titleChanges);
                result = ToChargeResult(posting, user.Id, null);
            });
            await NotifyTitleChangesAsync(titleChanges);
            return result!;
        }

        public async Task<TeleportRefundResultDto> RefundAsync(TeleportRefundRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequireKey(request.IdempotencyKey);
            if (request.UserId <= 0) throw new KeyNotFoundException($"User {request.UserId} not found.");
            if (request.Reason?.Length > 200) throw new ArgumentException("reason may be at most 200 characters.");

            TeleportRefundResultDto? result = null;
            Dictionary<int, TitleChangeResultDto> titleChanges = new();
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

                var legs = UserLegs(charge, user.Id);
                if (charge.ReasonCode != CurrencyReasons.TeleportFee || legs.Count == 0)
                {
                    throw KeyReuse(request.IdempotencyKey);
                }

                var prior = await _ledger.FindReversalOfAsync(charge.Id);
                if (prior != null)
                {
                    result = Refunded(legs, user, replayed: true);
                    return;
                }

                try
                {
                    var ctx = CurrencyContext.ForSystem(Component, CurrencyReasons.Reversal, $"reverse:{charge.Id}",
                        string.IsNullOrWhiteSpace(request.Reason) ? "Teleport refund: the teleport didn't happen" : $"Teleport refund: {request.Reason.Trim()}");
                    var posting = await _currency.ReverseAsync(charge.Id, new ReversalOptions(), ctx);
                    if (!posting.Replayed)
                    {
                        // Giving XP back can promote the player again (bonuses are paid once per bracket, ever).
                        await ApplyTitleProgressionAsync(posting, titleChanges);
                    }
                    result = Refunded(legs, user, replayed: posting.Replayed, posting.Balances.TryGetValue(user.Id, out var b) ? b : null);
                    _logger.LogInformation("Teleport refund: {Price} back to user {UserId} (key {Key}, charge {PublicId})",
                        Describe(result.Payments), user.Id, request.IdempotencyKey, charge.PublicId);
                }
                catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.AlreadyReversed)
                {
                    result = Refunded(legs, user, replayed: true);
                }
                catch (CurrencyException ex)
                {
                    _logger.LogWarning("Teleport refund refused for user {UserId} (key {Key}): {Message}", user.Id, request.IdempotencyKey, ex.Message);
                    throw new TeleportDestinationException(TeleportDestinationException.LedgerRefused, ex.Message);
                }
            });
            await NotifyTitleChangesAsync(titleChanges);
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

        private sealed record Access(int? PremiumWeight, HashSet<int> Discovered, TeleportKindPolicy Warp);

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
            var warp = domains.Count == 0
                ? TeleportKindPolicy.None(TeleportFeeKind.Warp)
                : TeleportGroupPolicy.Resolve(await LoadGroupChainAsync(user.Id), TeleportFeeKind.Warp);
            return new Access(premiumWeight, discovered, warp);
        }

        /// <summary>The player's groups in the order their teleport settings are checked (KNG-41).</summary>
        private async Task<List<PermissionGroup>> LoadGroupChainAsync(int userId)
        {
            var groups = await _permissionGroups.GetActiveGroupsForUserAsync(userId, DateTime.UtcNow);
            return TeleportGroupPolicy.Chain(groups ?? new List<PermissionGroup>());
        }

        /// <summary>A warp's price for this player: the domain's gems as the player's warp policy prices them.</summary>
        private static IReadOnlyList<TeleportPriceLeg> WarpPrice(Domain domain, TeleportKindPolicy policy) =>
            policy.PriceFor(new[] { new TeleportPriceLeg(Currency.Gems, domain.TeleportPriceGems) });

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

            var price = WarpPrice(domain, access.Warp);
            dto.PriceGems = (int)AmountOf(price, Currency.Gems);
            dto.PriceCoins = (int)AmountOf(price, Currency.Coins);
            dto.PriceExperience = (int)Math.Min(int.MaxValue, AmountOf(price, Currency.Experience));
            var shortOf = ShortOf(user, price);
            dto.RequirementsMet = code == null;
            dto.CanAfford = shortOf == null;
            if (code == null && shortOf != null)
            {
                code = InsufficientCode(shortOf.Value);
                reason = InsufficientMessage(shortOf.Value, WarpPurpose);
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

        // ===== Prices =====

        private static long AmountOf(IReadOnlyList<TeleportPriceLeg> price, Currency currency) =>
            price.Where(l => l.Currency == currency).Sum(l => l.Amount);

        private static long BalanceOf(User user, Currency currency) => currency switch
        {
            Currency.Coins => user.Coins,
            Currency.Gems => user.Gems,
            _ => user.ExperiencePoints
        };

        /// <summary>The first currency of <paramref name="price"/> the player can't pay; null when they can pay it all.</summary>
        private static Currency? ShortOf(User user, IReadOnlyList<TeleportPriceLeg> price) =>
            price.Where(l => BalanceOf(user, l.Currency) < l.Amount).Select(l => (Currency?)l.Currency).FirstOrDefault();

        private static string InsufficientCode(Currency currency) => currency switch
        {
            Currency.Coins => TeleportDestinationException.InsufficientCoins,
            Currency.Gems => TeleportDestinationException.InsufficientGems,
            _ => TeleportDestinationException.InsufficientExperience
        };

        private static string InsufficientMessage(Currency currency, string purpose) =>
            $"You don't have enough {Word(currency)} {purpose}!";

        private static string Word(Currency currency) => currency switch
        {
            Currency.Coins => "coins",
            Currency.Gems => "gems",
            _ => "XP"
        };

        private static string Describe(IEnumerable<TeleportPaymentDto> payments) =>
            string.Join(" + ", payments.Select(p => $"{p.Amount} {p.Currency}"));

        // ===== Ledger helpers =====

        /// <summary>
        /// Takes <paramref name="price"/> from the player in one TELEPORT_FEE posting (all
        /// currencies or none), checked against their balances first so the refusal names the
        /// currency they're short of. An XP price runs title progression (it may demote, KNG-41);
        /// the changes are collected for <see cref="NotifyTitleChangesAsync"/> after the commit.
        /// </summary>
        private async Task<PostingResult> SpendAsync(User user, IReadOnlyList<TeleportPriceLeg> price, string key,
            string sourceType, string? sourceRef, string reason, string purpose, object metadata,
            Dictionary<int, TitleChangeResultDto> titleChanges)
        {
            if (ShortOf(user, price) is { } shortOf)
            {
                throw new TeleportDestinationException(InsufficientCode(shortOf), InsufficientMessage(shortOf, purpose));
            }
            var ctx = new CurrencyContext
            {
                IdempotencyKey = key,
                IdempotencyScope = CurrencyIdempotencyScopes.Plugin,
                ReasonCode = CurrencyReasons.TeleportFee,
                Reason = reason.Length > 500 ? reason[..500] : reason,
                // The player pays for their own teleport; the plugin relays it.
                Initiator = CurrencyInitiator.Player,
                InitiatorUserId = user.Id,
                InitiatorComponent = Component,
                SourceType = sourceType,
                SourceRef = sourceRef,
                MetadataJson = JsonSerializer.Serialize(metadata)
            };
            try
            {
                var posting = price.Count == 1
                    ? await _currency.SpendAsync(user.Id, price[0].Currency, price[0].Amount, ctx)
                    : await _currency.PostAsync(price.Select(l => new CurrencyLeg(user.Id, l.Currency, -l.Amount)).ToList(), ctx);
                if (!posting.Replayed)
                {
                    await ApplyTitleProgressionAsync(posting, titleChanges);
                }
                return posting;
            }
            catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.InsufficientFunds)
            {
                var currency = price.Count == 1 ? price[0].Currency : ShortOf(user, price) ?? price[0].Currency;
                throw new TeleportDestinationException(InsufficientCode(currency), InsufficientMessage(currency, purpose));
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

        /// <summary>Title progression for a posting that changed XP (same transaction, KNG-41).</summary>
        private async Task ApplyTitleProgressionAsync(PostingResult posting, Dictionary<int, TitleChangeResultDto> titleChanges)
        {
            if (_titleProgression == null || posting.Entries.All(e => e.Currency != nameof(Currency.Experience)))
            {
                return;
            }
            foreach (var (userId, change) in await _titleProgression.ApplyForPostingAsync(posting, null))
            {
                titleChanges[userId] = change;
            }
        }

        /// <summary>Queues the in-game title change messages, after the posting committed.</summary>
        private async Task NotifyTitleChangesAsync(Dictionary<int, TitleChangeResultDto> titleChanges)
        {
            if (_notifications == null || titleChanges.Count == 0) return;
            var identities = await _ledger.GetIdentitiesAsync(titleChanges.Keys);
            foreach (var (userId, change) in titleChanges)
            {
                var who = identities.GetValueOrDefault(userId);
                _notifications.Enqueue(userId, who.Uuid, who.Username ?? "", PlayerNotificationTypes.TitleChanged, change);
            }
        }

        /// <summary>A replayed key must name the same kind of charge for the same player and
        /// target, and must not have been refunded.</summary>
        private async Task RequireSameChargeAsync(CurrencyTransaction existing, int userId, string sourceType, string? sourceRef)
        {
            if (existing.ReasonCode != CurrencyReasons.TeleportFee
                || UserLegs(existing, userId).Count == 0
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

        /// <summary>The player's legs of a charge, in coins, gems, XP order.</summary>
        private static List<CurrencyEntry> UserLegs(CurrencyTransaction tx, int userId) =>
            tx.Entries.Where(e => e.AccountKind == CurrencyAccountKind.User && e.UserId == userId)
                .OrderBy(e => e.Currency).ToList();

        private static TeleportChargeResultDto Free(User user, Currency currency, TeleportDestinationDto? destination) => new()
        {
            Currency = currency.ToString(),
            Charged = 0,
            NewBalance = BalanceOf(user, currency),
            Destination = destination
        };

        private static TeleportChargeResultDto Replay(CurrencyTransaction existing, User user, TeleportDestinationDto? destination)
        {
            var payments = UserLegs(existing, user.Id).Select(e => new TeleportPaymentDto
            {
                Currency = e.Currency.ToString(),
                Amount = -e.Amount,
                NewBalance = BalanceOf(user, e.Currency)
            }).ToList();
            return WithPayments(new TeleportChargeResultDto
            {
                Replayed = true,
                TransactionPublicId = existing.PublicId,
                Destination = destination
            }, payments);
        }

        private static TeleportChargeResultDto ToChargeResult(PostingResult posting, int userId, TeleportDestinationDto? destination)
        {
            var payments = posting.Entries.Where(e => e.UserId == userId)
                .OrderBy(e => Enum.Parse<Currency>(e.Currency))
                .Select(e => new TeleportPaymentDto { Currency = e.Currency, Amount = -e.Amount, NewBalance = e.BalanceAfter })
                .ToList();
            return WithPayments(new TeleportChargeResultDto
            {
                Replayed = posting.Replayed,
                TransactionPublicId = posting.PublicId,
                Destination = destination
            }, payments);
        }

        /// <summary>Sets the payments and the single-currency fields (the first payment) older plugins read.</summary>
        private static TeleportChargeResultDto WithPayments(TeleportChargeResultDto result, List<TeleportPaymentDto> payments)
        {
            result.Payments = payments;
            result.Currency = payments[0].Currency;
            result.Charged = payments[0].Amount;
            result.NewBalance = payments[0].NewBalance;
            return result;
        }

        private static TeleportRefundResultDto Refunded(List<CurrencyEntry> legs, User user, bool replayed, BalancesDto? balances = null)
        {
            var payments = legs.Select(leg => new TeleportPaymentDto
            {
                Currency = leg.Currency.ToString(),
                Amount = -leg.Amount,
                NewBalance = balances != null
                    ? leg.Currency switch
                    {
                        Currency.Coins => balances.Coins,
                        Currency.Gems => balances.Gems,
                        _ => balances.ExperiencePoints
                    }
                    : BalanceOf(user, leg.Currency)
            }).ToList();
            return new TeleportRefundResultDto
            {
                Refunded = true,
                Currency = payments[0].Currency,
                Amount = payments[0].Amount,
                NewBalance = payments[0].NewBalance,
                Replayed = replayed,
                Payments = payments
            };
        }

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

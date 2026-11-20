using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    // Siege shop L4 (knk-workspace docs/specs/siege-shop/DESIGN.md § 4.2, § 5.6, § 6.2.3; P08, P09, P20; W32, W67;
    // PandiO/knk-workspace#869, #795): buying a siege weapon type's permanent unlock with personal coins. The kit
    // pattern (KitService.PurchaseKitAsync): under the player's row lock, check (unlocked already, title level, coins),
    // then the SIEGE_UNLOCK spend with the once-per-player-and-template key siege-unlock:{templateId}:{userId}, then the
    // UserSiegeWeaponUnlock row, in one transaction. "Not during a match" (P08, A28) is the game server's check: the
    // API has no reliable live-match state for a player, and an unlock bought mid-match is only read at the next start.
    public class SiegeWeaponUnlockPurchaseService : ISiegeWeaponUnlockPurchaseService
    {
        private readonly ISiegeWeaponRepository _weapons;
        private readonly IUserRepository _users;
        private readonly ITitleBracketRepository _titles;
        private readonly ICurrencyService _currency;

        public SiegeWeaponUnlockPurchaseService(ISiegeWeaponRepository weapons, IUserRepository users,
            ITitleBracketRepository titles, ICurrencyService currency)
        {
            _weapons = weapons;
            _users = users;
            _titles = titles;
            _currency = currency;
        }

        public static string IdempotencyKey(int templateId, int userId) => $"siege-unlock:{templateId}:{userId}";

        public async Task<SiegeWeaponUnlocksOverviewDto> GetOverviewAsync(int userId)
        {
            var user = await _users.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException($"User with id {userId} not found.");
            var brackets = await _titles.GetAllOrderedByMinExperienceAsync();
            var templates = await _weapons.GetTemplatesAsync();
            var unlocks = (await _weapons.GetUnlocksForUserAsync(userId))
                .GroupBy(u => u.SiegeWeaponTemplateId)
                .ToDictionary(g => g.Key, g => g.First());
            var current = CurrentBracket(brackets, user.ExperiencePoints);

            return new SiegeWeaponUnlocksOverviewDto
            {
                UserId = userId,
                Coins = user.Coins,
                TitleLevel = current?.Id,
                TitleName = current?.NameFor(user.Gender),
                Offers = templates.Select(t =>
                {
                    var required = RequiredBracket(brackets, t);
                    var meetsTitle = required == null || user.ExperiencePoints >= required.MinExperience;
                    var canAfford = user.Coins >= t.UnlockPrice;
                    unlocks.TryGetValue(t.Id, out var unlock);
                    return new SiegeWeaponUnlockOfferDto
                    {
                        TemplateId = t.Id,
                        Name = t.Name,
                        Description = t.Description,
                        Kind = t.Kind,
                        UnlockPrice = t.UnlockPrice,
                        MinTitleLevel = required?.Id,
                        MinTitleName = required?.NameFor(user.Gender),
                        Unlocked = unlock != null,
                        UnlockedAt = unlock?.UnlockedAt,
                        MeetsTitle = meetsTitle,
                        CanAfford = canAfford,
                        State = unlock != null ? SiegeWeaponUnlockStates.Unlocked
                            : !meetsTitle ? SiegeWeaponUnlockStates.NeedsTitle
                            : !canAfford ? SiegeWeaponUnlockStates.NeedsCoins
                            : SiegeWeaponUnlockStates.Unlockable,
                    };
                }).ToList(),
            };
        }

        public async Task<SiegeWeaponUnlockPurchaseResultDto> PurchaseAsync(int userId, int templateId, CurrencyContext context)
        {
            var template = await _weapons.GetTemplateByIdAsync(templateId)
                ?? throw new KeyNotFoundException($"Siege weapon template with id {templateId} not found.");
            var price = template.UnlockPrice;

            SiegeWeaponUnlockPurchaseResultDto result = null!;
            // Same row lock as the kit purchase: the checks and the coin posting see no other balance write for this
            // player in between, and a double click waits here and then finds the unlock.
            await _users.RunWithUsersLockedAsync(new[] { userId }, async () =>
            {
                var user = await _users.GetByIdAsync(userId)
                    ?? throw new KeyNotFoundException($"User with id {userId} not found.");

                var existing = await _weapons.GetUnlockAsync(userId, templateId);
                if (existing != null)
                {
                    result = Result(template, userId, 0, alreadyUnlocked: true, existing.UnlockedAt, user.Coins);
                    return;
                }

                var brackets = await _titles.GetAllOrderedByMinExperienceAsync();
                var required = RequiredBracket(brackets, template);
                if (required != null && user.ExperiencePoints < required.MinExperience)
                    throw new SiegeWeaponUnlockRefusedException("NeedsTitle",
                        $"You need the title {required.NameFor(user.Gender)} to unlock the {template.Name}.");

                if (user.Coins < price)
                    throw new SiegeWeaponUnlockRefusedException(nameof(CurrencyErrorCode.InsufficientFunds),
                        $"Not enough coins to unlock the {template.Name}: {user.Coins}/{price}.");

                var coins = user.Coins;
                if (price > 0)
                {
                    var ctx = context with
                    {
                        ReasonCode = CurrencyReasons.SiegeUnlock,
                        IdempotencyKey = IdempotencyKey(templateId, userId),
                        IdempotencyScope = CurrencyIdempotencyScopes.System,
                        SourceType = "SiegeWeaponTemplate",
                        SourceRef = templateId.ToString(),
                        Reason = $"Unlocked siege weapon {template.Name}"
                    };
                    var posting = await _currency.SpendAsync(userId, Currency.Coins, price, ctx);
                    coins = posting.Balances.TryGetValue(userId, out var balance) ? balance.Coins : coins - price;
                }

                var unlock = new UserSiegeWeaponUnlock
                {
                    UserId = userId,
                    SiegeWeaponTemplateId = templateId,
                    UnlockedAt = DateTime.UtcNow,
                };
                await _weapons.AddUnlockAsync(unlock);
                result = Result(template, userId, price, alreadyUnlocked: false, unlock.UnlockedAt, coins);
            });
            return result;
        }

        private static SiegeWeaponUnlockPurchaseResultDto Result(SiegeWeaponTemplate template, int userId, int paid,
            bool alreadyUnlocked, DateTime unlockedAt, int coins) => new()
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            UserId = userId,
            CoinsPaid = paid,
            AlreadyUnlocked = alreadyUnlocked,
            UnlockedAt = unlockedAt,
            Coins = coins,
        };

        // A minimum that names a bracket no longer seeded counts as none (the FK keeps that from happening).
        private static TitleBracket? RequiredBracket(List<TitleBracket> brackets, SiegeWeaponTemplate template) =>
            template.UnlockMinTitleLevel.HasValue
                ? brackets.FirstOrDefault(b => b.Id == template.UnlockMinTitleLevel.Value)
                : null;

        private static TitleBracket? CurrentBracket(List<TitleBracket> brackets, int experience) =>
            brackets.Count == 0 ? null : brackets.LastOrDefault(b => b.MinExperience <= experience) ?? brackets[0];
    }
}

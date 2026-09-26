using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Title progression on top of the currency ledger (currency-payments IMPLEMENTATION_PLAN.md
    /// Phase 2); see <see cref="ITitleProgressionService"/>. Before Phase 2 this loop lived inline
    /// in UserService.AdjustBalancesAsync and credited every crossed bracket's bonus again after a
    /// demotion (audit A5); now each bracket's bonus is a keyed ledger posting that can only ever
    /// be made once per user.
    /// </summary>
    public class TitleProgressionService : ITitleProgressionService
    {
        /// <summary>InitiatorComponent of the bonus postings.</summary>
        public const string Component = "TitleProgression";

        private readonly ICurrencyService _currency;
        private readonly IUserRepository _users;
        private readonly ITitleService _titleService;
        private readonly IUserPermissionGroupService _membershipService;
        private readonly IAuditLogService _auditLogService;

        public TitleProgressionService(
            ICurrencyService currency,
            IUserRepository users,
            ITitleService titleService,
            IUserPermissionGroupService membershipService,
            IAuditLogService auditLogService)
        {
            _currency = currency;
            _users = users;
            _titleService = titleService;
            _membershipService = membershipService;
            _auditLogService = auditLogService;
        }

        /// <summary>The once-ever key of a bracket's promotion bonus (CurrencyReasons.TitleBonus).</summary>
        public static string BonusKey(int userId, int bracketId) => $"title-bonus:{userId}:{bracketId}";

        public async Task<Dictionary<int, TitleChangeResultDto>> ApplyForPostingAsync(PostingResult posting, int? actorUserId, CancellationToken ct = default)
        {
            var changes = new Dictionary<int, TitleChangeResultDto>();
            if (posting == null || posting.Replayed)
            {
                return changes;
            }
            var xpLegs = posting.Entries
                .Where(e => e.Currency == nameof(Currency.Experience) && e.Amount != 0)
                .GroupBy(e => e.UserId);
            foreach (var legs in xpLegs)
            {
                var change = await ApplyAsync(legs.Key, legs.First().BalanceBefore, actorUserId, posting.PublicId, ct);
                if (change != null)
                {
                    changes[legs.Key] = change;
                }
            }
            return changes;
        }

        public async Task<TitleChangeResultDto?> ApplyAsync(int userId, long previousExperience, int? actorUserId,
            string? correlationId = null, CancellationToken ct = default)
        {
            var brackets = await _titleService.GetAllOrderedAsync();
            if (brackets == null || brackets.Count == 0)
            {
                return null;
            }

            TitleChangeResultDto? change = null;
            await _users.RunWithUsersLockedAsync(new[] { userId }, async () =>
            {
                var user = await _users.GetByIdAsync(userId)
                    ?? throw new KeyNotFoundException($"User with id {userId} not found.");
                change = await ApplyLockedAsync(user, previousExperience, brackets, correlationId, ct);
                if (change != null)
                {
                    // One consolidated audit entry for the whole crossing, not one per tier.
                    await _auditLogService.RecordAsync(actorUserId, userId, AuditAction.TitleChanged, JsonSerializer.Serialize(new
                    {
                        fromTitleBracketId = change.FromTitleBracketId,
                        fromTitleName = change.FromTitleName,
                        toTitleBracketId = change.ToTitleBracketId,
                        toTitleName = change.ToTitleName,
                        crossedTitles = change.CrossedTitles.Select(t => t.TitleName),
                        direction = change.Direction,
                        coinBonusGranted = change.CoinBonusGranted,
                        gemBonusGranted = change.GemBonusGranted,
                        expBonusGranted = change.ExpBonusGranted
                    }));
                }
            });
            return change;
        }

        private async Task<TitleChangeResultDto?> ApplyLockedAsync(User user, long previousExperience, List<TitleBracket> brackets,
            string? correlationId, CancellationToken ct)
        {
            var previousBracket = brackets.LastOrDefault(b => b.MinExperience <= previousExperience) ?? brackets[0];
            var currentBracket = brackets.LastOrDefault(b => b.MinExperience <= user.ExperiencePoints) ?? brackets[0];
            if (currentBracket.Id == previousBracket.Id)
            {
                return null;
            }

            // Consolidate every bracket crossed by this single change into one reported change,
            // instead of firing once per tier the way v1's TitleChangeEvents loop did
            // (setPromoteLoop/setDemoteLoop) — a developer-confirmed behavior NOT to repeat.
            var direction = user.ExperiencePoints > previousExperience ? "promotion" : "demotion";
            var crossed = new List<TitleBracket>();
            long coinBonusTotal = 0, gemBonusTotal = 0, expBonusTotal = 0;
            int coinBonusBase = 0, gemBonusBase = 0, expBonusBase = 0;
            var coinMultipliers = new List<RewardMultiplierDto>();
            var gemMultipliers = new List<RewardMultiplierDto>();
            var expMultipliers = new List<RewardMultiplierDto>();

            if (direction == "promotion")
            {
                // KNG-16: each bonus is scaled by the player's personal x rank multiplier for that
                // currency - coins by the salary multipliers, gems and XP by their own
                // GemBonus/ExpBonus multipliers. No global multiplier applies.
                var ranks = await _membershipService.GetActiveRankMultipliersAsync(user.Id) ?? RankMultipliersDto.Neutral;
                var coinMultiplier = user.PersonalSalaryMultiplier * ranks.Salary;
                var gemMultiplier = user.PersonalGemBonusMultiplier * ranks.GemBonus;
                var expMultiplier = user.PersonalExpBonusMultiplier * ranks.ExpBonus;
                coinMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalSalaryMultiplier));
                coinMultipliers.AddRange(ranks.SalaryBreakdown());
                gemMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalGemBonusMultiplier));
                gemMultipliers.AddRange(ranks.GemBonusBreakdown());
                expMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalExpBonusMultiplier));
                expMultipliers.AddRange(ranks.ExpBonusBreakdown());

                // Walk every bracket strictly above previousBracket up to the current XP. A paid XP
                // bonus raises the XP, so the loop re-checks and may cross further brackets.
                var idx = brackets.FindIndex(b => b.Id == previousBracket.Id) + 1;
                while (idx < brackets.Count && brackets[idx].MinExperience <= user.ExperiencePoints)
                {
                    var tier = brackets[idx++];
                    crossed.Add(tier);

                    var key = BonusKey(user.Id, tier.Id);
                    if (await _currency.FindAsync(CurrencyIdempotencyScopes.System, key, ct) != null)
                    {
                        continue; // reached before (e.g. demoted and promoted again): paid once, ever
                    }

                    var coins = ScaleBonus(tier.CoinBonus, coinMultiplier);
                    var gems = ScaleBonus(tier.GemBonus, gemMultiplier);
                    var exp = ScaleBonus(tier.ExpBonus, expMultiplier);
                    var legs = new List<CurrencyLeg>();
                    if (coins > 0) legs.Add(new CurrencyLeg(user.Id, Currency.Coins, coins));
                    if (gems > 0) legs.Add(new CurrencyLeg(user.Id, Currency.Gems, gems));
                    if (exp > 0) legs.Add(new CurrencyLeg(user.Id, Currency.Experience, exp));
                    if (legs.Count == 0)
                    {
                        continue;
                    }

                    var ctx = CurrencyContext.ForSystem(Component, CurrencyReasons.TitleBonus, key,
                        $"Title bonus for reaching {tier.NameFor(user.Gender)}") with
                    {
                        SourceType = "TitleBracket",
                        SourceRef = tier.Id.ToString(),
                        CorrelationId = correlationId,
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            coinBonusBase = tier.CoinBonus,
                            gemBonusBase = tier.GemBonus,
                            expBonusBase = tier.ExpBonus,
                            coinMultiplier,
                            gemMultiplier,
                            expMultiplier
                        })
                    };
                    // Updates the tracked user, so the loop condition sees the new XP.
                    var posting = await _currency.PostAsync(legs, ctx, ct);
                    coinBonusTotal += Posted(posting, Currency.Coins);
                    gemBonusTotal += Posted(posting, Currency.Gems);
                    expBonusTotal += Posted(posting, Currency.Experience);
                    coinBonusBase += tier.CoinBonus;
                    gemBonusBase += tier.GemBonus;
                    expBonusBase += tier.ExpBonus;
                }
                currentBracket = brackets.LastOrDefault(b => b.MinExperience <= user.ExperiencePoints) ?? brackets[0];
            }
            // Demotion never claws back currency (matches v1's userDemotion, which only ever
            // removed structural slots/skills — neither exists in v3).

            return new TitleChangeResultDto
            {
                Direction = direction,
                FromTitleBracketId = previousBracket.Id,
                FromTitleName = previousBracket.NameFor(user.Gender),
                ToTitleBracketId = currentBracket.Id,
                ToTitleName = currentBracket.NameFor(user.Gender),
                CrossedTitles = crossed.Select(t => new TitleCrossingDto { TitleBracketId = t.Id, TitleName = t.NameFor(user.Gender) }).ToList(),
                // What was actually posted: a bracket paid on an earlier promotion adds nothing.
                // Each total is within its balance cap (the ledger checked it), so it fits an int.
                CoinBonusGranted = (int)coinBonusTotal,
                GemBonusGranted = (int)gemBonusTotal,
                ExpBonusGranted = (int)Math.Min(expBonusTotal, int.MaxValue),
                CoinBonusBase = coinBonusBase,
                GemBonusBase = gemBonusBase,
                ExpBonusBase = expBonusBase,
                CoinBonusMultipliers = coinMultipliers,
                GemBonusMultipliers = gemMultipliers,
                ExpBonusMultipliers = expMultipliers
            };
        }

        private static long Posted(PostingResult posting, Currency currency) =>
            posting.Entries.Where(e => e.Currency == currency.ToString()).Sum(e => e.Amount);

        /// <summary>A title promotion bonus scaled by its multiplier, rounded to whole units and
        /// never negative (a multiplier set negative by a direct DB edit pays nothing).</summary>
        public static long ScaleBonus(int bonus, decimal multiplier) =>
            BalanceLimits.ToWholeAmount(() => bonus * multiplier, "title bonus");
    }
}

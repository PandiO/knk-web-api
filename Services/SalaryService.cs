using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Salary payout logic (docs/specs/user-features/DESIGN.md §5). The hourly base rate is the
    /// Salary of the user's current title bracket (resolved from ExperiencePoints, same as
    /// TitleService everywhere else), scaled by the global, personal and rank multipliers.
    /// Independent of the permission/rank system except for reading a user's currently-active
    /// PermissionGroup memberships to compute the rank-based multiplier — DESIGN.md §5's own note
    /// on this being "the one place Salary genuinely depends on the permission model".
    /// </summary>
    public class SalaryService : ISalaryService
    {
        /// <summary>A payout only triggers once this much time has passed since the last one —
        /// IMPLEMENTATION_PLAN.md §6's "if now - LastSalaryPayoutAt >= 1 hour". The elapsed gap is
        /// then paid with log decay (PaidHoursFor), per vision §5.4's offline-gap fix.</summary>
        private static readonly TimeSpan MinimumPayoutInterval = TimeSpan.FromHours(1);

        private readonly IUserRepository _userRepo;
        private readonly IUserPermissionGroupRepository _membershipRepo;
        private readonly ISalaryConfigurationService _configService;
        private readonly ITitleService _titleService;
        private readonly IAuditLogService _auditLogService;

        public SalaryService(
            IUserRepository userRepo,
            IUserPermissionGroupRepository membershipRepo,
            ISalaryConfigurationService configService,
            ITitleService titleService,
            IAuditLogService auditLogService)
        {
            _userRepo = userRepo;
            _membershipRepo = membershipRepo;
            _configService = configService;
            _titleService = titleService;
            _auditLogService = auditLogService;
        }

        public async Task<SalaryPayoutResultDto> PayOutAsync(int userId)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user id.", nameof(userId));

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) throw new KeyNotFoundException($"User with id {userId} not found.");

            var now = DateTime.UtcNow;
            // MySQL reads DateTime back as Unspecified — mark it UTC before arithmetic (same
            // convention UserPermissionGroupService.ToDto already established for ExpiresAt).
            var lastPayout = DateTime.SpecifyKind(user.LastSalaryPayoutAt, DateTimeKind.Utc);
            var elapsed = now - lastPayout;

            if (elapsed < MinimumPayoutInterval)
            {
                return new SalaryPayoutResultDto
                {
                    Paid = false,
                    LastSalaryPayoutAt = lastPayout,
                    NextEligibleAt = lastPayout + MinimumPayoutInterval
                };
            }

            // Sequential awaits: these share one scoped DbContext (see UserProfileSummaryService).
            var config = await _configService.GetAsync();
            var title = await _titleService.ResolveAsync(user.ExperiencePoints, user.Gender);
            var ranks = await GetActiveRanksAsync(userId, now);
            var rankMultiplier = ranks.Salary;
            var hoursCovered = (decimal)elapsed.TotalHours;
            var paidHours = PaidHoursFor(elapsed.TotalHours, config.OfflinePayoutMaxHours);

            // The title's Salary is the per-hour base; before this it was left out entirely and
            // GlobalMultiplier (default 1.0) stood in as the base rate, paying ~1 coin an hour.
            var rawPayout = title.Salary * config.GlobalMultiplier * user.PersonalSalaryMultiplier * rankMultiplier * paidHours;
            // Multipliers are validated non-negative at write time (SalaryConfigurationService,
            // PermissionGroupService) and PersonalSalaryMultiplier defaults to a non-negative 1.0,
            // but nothing currently stops a direct DB edit from making one negative — clamp
            // defensively rather than ever crediting negative coins through this path.
            var amountPaid = Math.Max(0, (int)Math.Round(rawPayout, MidpointRounding.AwayFromZero));

            var coinsBefore = user.Coins;
            user.Coins += amountPaid;
            user.LastSalaryPayoutAt = now;
            await _userRepo.UpdateUserAsync(user);

            // System-initiated (actorUserId null) — closes user-features IMPLEMENTATION_PLAN.md
            // §6 carried-forward item 4's "PayOutAsync's coin mutation has no audit write hook".
            await _auditLogService.RecordAsync(null, userId, Enums.AuditAction.SalaryPayout, System.Text.Json.JsonSerializer.Serialize(new
            {
                amountPaid,
                hoursCovered,
                paidHours,
                titleBracketId = title.TitleBracketId,
                titleSalary = title.Salary,
                globalMultiplier = config.GlobalMultiplier,
                personalMultiplier = user.PersonalSalaryMultiplier,
                rankMultiplier,
                coinsBefore,
                coinsAfter = user.Coins
            }));

            return new SalaryPayoutResultDto
            {
                Paid = true,
                AmountPaid = amountPaid,
                HoursCovered = hoursCovered,
                PaidHours = paidHours,
                TitleBracketId = title.TitleBracketId,
                TitleSalary = title.Salary,
                GlobalMultiplier = config.GlobalMultiplier,
                PersonalMultiplier = user.PersonalSalaryMultiplier,
                RankMultiplier = rankMultiplier,
                BaseAmount = title.Salary * paidHours,
                Multipliers = new List<RewardMultiplierDto>
                {
                    RewardMultiplierDto.Global(config.GlobalMultiplier),
                    RewardMultiplierDto.Personal(user.PersonalSalaryMultiplier)
                }.Concat(ranks.SalaryBreakdown()).ToList(),
                NewCoinsBalance = user.Coins,
                LastSalaryPayoutAt = now,
                NextEligibleAt = now + MinimumPayoutInterval
            };
        }

        /// <summary>
        /// Hours of salary a gap of <paramref name="elapsedHours"/> since the last payout is worth
        /// (developer-chosen log decay, docs/specs/user-features/DESIGN.md §5): hour N of the gap
        /// pays 1/N of an hour, so the first hour pays in full and the total grows like ln(hours)
        /// — 1.5 for 2h, ~2.7 for 8h, ~3.8 for a day, ~7.2 for 30 days. Hours past
        /// <paramref name="maxHours"/> (default 720 = 30 days) pay nothing. A partial hour pays
        /// its fraction of that hour's weight. While online the plugin pays every hour, so an
        /// online player's payouts are ~1 hour each and barely decay.
        /// </summary>
        public static decimal PaidHoursFor(double elapsedHours, int maxHours)
        {
            var capped = Math.Min(Math.Max(0, elapsedHours), Math.Max(1, maxHours));
            var wholeHours = (int)Math.Floor(capped);
            var paid = 0.0;
            for (var n = 1; n <= wholeHours; n++)
            {
                paid += 1.0 / n;
            }
            paid += (capped - wholeHours) / (wholeHours + 1);
            return (decimal)paid;
        }

        public async Task<decimal> GetCurrentRankMultiplierAsync(int userId)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user id.", nameof(userId));
            return (await GetActiveRanksAsync(userId, DateTime.UtcNow)).Salary;
        }

        /// <summary>
        /// Every currently-active (non-expired) PermissionGroup membership the user holds. The rank
        /// multiplier is the product of their SalaryMultiplier (developer-confirmed combination
        /// rule — deliberately not the highest-Weight-wins rule Phase 5 used for premium tier
        /// *display*, since stacking ranks multiplicatively is meant to reward holding more than
        /// one at once). A user with no active memberships yields 1.0 (neutral), not 0.
        /// </summary>
        private async Task<RankMultipliersDto> GetActiveRanksAsync(int userId, DateTime asOf) =>
            RankMultipliersDto.FromMemberships(await _membershipRepo.GetByUserAsync(userId), asOf);
    }
}

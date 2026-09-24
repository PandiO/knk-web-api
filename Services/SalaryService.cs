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
    /// Salary payout logic (docs/specs/user-features/DESIGN.md §5). Independent of the
    /// permission/rank system except for reading a user's currently-active PermissionGroup
    /// memberships to compute the rank-based multiplier — DESIGN.md §5's own note on this being
    /// "the one place Salary genuinely depends on the permission model".
    /// </summary>
    public class SalaryService : ISalaryService
    {
        /// <summary>A payout only triggers once this much time has passed since the last one —
        /// IMPLEMENTATION_PLAN.md §6's "if now - LastSalaryPayoutAt >= 1 hour". The full elapsed
        /// gap (not just whole hours) is then paid out, per vision §5.4's offline-gap fix.</summary>
        private static readonly TimeSpan MinimumPayoutInterval = TimeSpan.FromHours(1);

        private readonly IUserRepository _userRepo;
        private readonly IUserPermissionGroupRepository _membershipRepo;
        private readonly ISalaryConfigurationService _configService;

        public SalaryService(
            IUserRepository userRepo,
            IUserPermissionGroupRepository membershipRepo,
            ISalaryConfigurationService configService)
        {
            _userRepo = userRepo;
            _membershipRepo = membershipRepo;
            _configService = configService;
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

            var config = await _configService.GetAsync();
            var rankMultiplier = await ComputeRankMultiplierAsync(userId, now);
            var hoursCovered = (decimal)elapsed.TotalHours;

            var rawPayout = config.GlobalMultiplier * user.PersonalSalaryMultiplier * rankMultiplier * hoursCovered;
            // Multipliers are validated non-negative at write time (SalaryConfigurationService,
            // PermissionGroupService) and PersonalSalaryMultiplier defaults to a non-negative 1.0,
            // but nothing currently stops a direct DB edit from making one negative — clamp
            // defensively rather than ever crediting negative coins through this path.
            var amountPaid = Math.Max(0, (int)Math.Round(rawPayout, MidpointRounding.AwayFromZero));

            user.Coins += amountPaid;
            user.LastSalaryPayoutAt = now;
            await _userRepo.UpdateUserAsync(user);

            return new SalaryPayoutResultDto
            {
                Paid = true,
                AmountPaid = amountPaid,
                HoursCovered = hoursCovered,
                GlobalMultiplier = config.GlobalMultiplier,
                PersonalMultiplier = user.PersonalSalaryMultiplier,
                RankMultiplier = rankMultiplier,
                NewCoinsBalance = user.Coins,
                LastSalaryPayoutAt = now,
                NextEligibleAt = now + MinimumPayoutInterval
            };
        }

        public Task<decimal> GetCurrentRankMultiplierAsync(int userId)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user id.", nameof(userId));
            return ComputeRankMultiplierAsync(userId, DateTime.UtcNow);
        }

        /// <summary>
        /// Product of SalaryMultiplier across every currently-active (non-expired) PermissionGroup
        /// membership the user holds (developer-confirmed combination rule — deliberately not the
        /// highest-Weight-wins rule Phase 5 used for premium tier *display*, since stacking ranks
        /// multiplicatively is meant to reward holding more than one at once). A user with no
        /// active memberships yields 1.0 (neutral), not 0 — an empty product.
        /// </summary>
        private async Task<decimal> ComputeRankMultiplierAsync(int userId, DateTime asOf)
        {
            var memberships = await _membershipRepo.GetByUserAsync(userId);
            return memberships
                .Where(m => m.PermissionGroup != null && (m.ExpiresAt == null || m.ExpiresAt > asOf))
                .Aggregate(1.0m, (product, m) => product * m.PermissionGroup!.SalaryMultiplier);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The per-player coin multipliers shared by salary payouts and minigame coin rewards
    /// (docs/specs/user-features/DESIGN.md §5; siege rewards since the 2026-09-26 smoke test):
    /// the user's <see cref="User.PersonalSalaryMultiplier"/> times their rank multiplier, the
    /// product of <see cref="PermissionGroup.SalaryMultiplier"/> over every active membership
    /// (premium tiers included). Salary additionally applies its own global multiplier.
    /// </summary>
    public static class CoinRewardMultipliers
    {
        /// <summary>
        /// Product of SalaryMultiplier across the memberships active at <paramref name="asOf"/>;
        /// 1.0 (neutral) when there are none.
        /// </summary>
        public static decimal Rank(IEnumerable<UserPermissionGroup> memberships, DateTime asOf) =>
            memberships
                .Where(m => m.PermissionGroup != null && (m.ExpiresAt == null || m.ExpiresAt > asOf))
                .Aggregate(1.0m, (product, m) => product * m.PermissionGroup!.SalaryMultiplier);

        /// <summary>
        /// <paramref name="amount"/> times the multiplier, rounded half away from zero like salary
        /// payouts, never negative (multipliers are validated non-negative on write, but a direct DB
        /// edit could still make one negative).
        /// </summary>
        public static int Apply(decimal amount, decimal multiplier) =>
            Math.Max(0, (int)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero));
    }
}

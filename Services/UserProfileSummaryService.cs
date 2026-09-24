using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// docs/specs/user-management/IMPLEMENTATION_PLAN.md Phase 1 — see IUserProfileSummaryService.
    /// </summary>
    public class UserProfileSummaryService : IUserProfileSummaryService
    {
        private static readonly TimeSpan MinimumPayoutInterval = TimeSpan.FromHours(1);

        private readonly IUserService _userService;
        private readonly IPermissionResolutionService _permissionResolutionService;
        private readonly IUserPermissionGroupService _userPermissionGroupService;
        private readonly ITitleService _titleService;
        private readonly ISalaryService _salaryService;
        private readonly ISalaryConfigurationService _salaryConfigurationService;

        public UserProfileSummaryService(
            IUserService userService,
            IPermissionResolutionService permissionResolutionService,
            IUserPermissionGroupService userPermissionGroupService,
            ITitleService titleService,
            ISalaryService salaryService,
            ISalaryConfigurationService salaryConfigurationService)
        {
            _userService = userService;
            _permissionResolutionService = permissionResolutionService;
            _userPermissionGroupService = userPermissionGroupService;
            _titleService = titleService;
            _salaryService = salaryService;
            _salaryConfigurationService = salaryConfigurationService;
        }

        public async Task<UserProfileSummaryDto?> GetAsync(int userId)
        {
            var account = await _userService.GetByIdAsync(userId);
            if (account == null)
            {
                return null;
            }

            // These all go through the same scoped DbContext (via their respective repositories),
            // which EF Core does not support concurrent operations against - awaiting them via
            // Task.WhenAll throws "A second operation was started on this context instance before
            // a previous operation completed" the moment more than one of these actually hits the
            // database. Sequential awaits are required here, not just a style preference.
            var permissions = await _permissionResolutionService.GetEffectiveAsync(userId);
            var groups = await _userPermissionGroupService.GetByUserAsync(userId);
            var title = await _titleService.ResolveAsync(account.ExperiencePoints);
            var rankMultiplier = await _salaryService.GetCurrentRankMultiplierAsync(userId);
            var salaryConfig = await _salaryConfigurationService.GetAsync();

            var globalMultiplier = salaryConfig.GlobalMultiplier;

            var salary = new SalaryStateDto
            {
                GlobalMultiplier = globalMultiplier,
                PersonalMultiplier = account.PersonalSalaryMultiplier,
                RankMultiplier = rankMultiplier,
                EffectiveHourlyRate = globalMultiplier * account.PersonalSalaryMultiplier * rankMultiplier,
                LastSalaryPayoutAt = account.LastSalaryPayoutAt,
                NextEligibleAt = account.LastSalaryPayoutAt + MinimumPayoutInterval
            };

            return new UserProfileSummaryDto
            {
                Account = account,
                // GetEffectiveAsync/GetByIdAsync just succeeded for this same id, so a null here
                // would mean the user was deleted between those two calls - treat that the same
                // as "not found" rather than surfacing a confusing partial response.
                Permissions = permissions ?? new PermissionEffectiveResponseDto { UserId = userId },
                Groups = groups,
                Title = title,
                Salary = salary
            };
        }
    }
}

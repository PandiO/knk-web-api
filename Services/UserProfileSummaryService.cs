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

            // Everything below is read-only against a user we've just confirmed exists, so these
            // can run concurrently rather than round-tripping one at a time.
            var permissionsTask = _permissionResolutionService.GetEffectiveAsync(userId);
            var groupsTask = _userPermissionGroupService.GetByUserAsync(userId);
            var titleTask = _titleService.ResolveAsync(account.ExperiencePoints);
            var rankMultiplierTask = _salaryService.GetCurrentRankMultiplierAsync(userId);
            var salaryConfigTask = _salaryConfigurationService.GetAsync();

            await Task.WhenAll(permissionsTask, groupsTask, titleTask, rankMultiplierTask, salaryConfigTask);

            var globalMultiplier = salaryConfigTask.Result.GlobalMultiplier;
            var rankMultiplier = rankMultiplierTask.Result;

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
                Permissions = permissionsTask.Result ?? new PermissionEffectiveResponseDto { UserId = userId },
                Groups = groupsTask.Result,
                Title = titleTask.Result,
                Salary = salary
            };
        }
    }
}

using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    public class UserIgnoreService : IUserIgnoreService
    {
        /// <summary>Holders can't be ignored (DESIGN.md §3.5) - grant it to staff.</summary>
        public const string UnignorableNode = "knk.msg.unignorable";

        public const int MaxIgnoresPerUser = 100;

        private readonly IUserIgnoreRepository _repo;
        private readonly IUserRepository _userRepo;
        private readonly IPermissionResolutionService _permissions;

        public UserIgnoreService(IUserIgnoreRepository repo, IUserRepository userRepo, IPermissionResolutionService permissions)
        {
            _repo = repo;
            _userRepo = userRepo;
            _permissions = permissions;
        }

        public async Task<List<UserIgnoreDto>?> GetAsync(int userId)
        {
            if (await _userRepo.GetByIdAsync(userId) == null)
                return null;

            var ignores = await _repo.GetByUserAsync(userId);
            return ignores.Select(ToDto).ToList();
        }

        public async Task<UserIgnoreAddResult> AddAsync(int userId, int ignoredUserId)
        {
            if (await _userRepo.GetByIdAsync(userId) == null || await _userRepo.GetByIdAsync(ignoredUserId) == null)
                return UserIgnoreAddResult.UserNotFound;

            if (userId == ignoredUserId)
                return UserIgnoreAddResult.SelfIgnore;

            if (await _repo.GetAsync(userId, ignoredUserId) != null)
                return UserIgnoreAddResult.Ignored;

            var unignorable = await _permissions.CheckAsync(ignoredUserId, UnignorableNode);
            if (unignorable?.Allowed == true)
                return UserIgnoreAddResult.CannotIgnoreStaff;

            if (await _repo.CountByUserAsync(userId) >= MaxIgnoresPerUser)
                return UserIgnoreAddResult.IgnoreLimitReached;

            try
            {
                await _repo.AddAsync(new UserIgnore
                {
                    UserId = userId,
                    IgnoredUserId = ignoredUserId,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (DbUpdateException)
            {
                // A concurrent request added the same pair first (unique index) - same end state.
                if (await _repo.GetAsync(userId, ignoredUserId) == null)
                    throw;
            }
            return UserIgnoreAddResult.Ignored;
        }

        public async Task RemoveAsync(int userId, int ignoredUserId)
        {
            var existing = await _repo.GetAsync(userId, ignoredUserId);
            if (existing != null)
                await _repo.DeleteAsync(existing);
        }

        private static UserIgnoreDto ToDto(UserIgnore ignore) => new()
        {
            IgnoredUserId = ignore.IgnoredUserId,
            IgnoredUsername = ignore.IgnoredUser?.Username ?? string.Empty,
            IgnoredUuid = ignore.IgnoredUser?.Uuid,
            CreatedAt = DateTime.SpecifyKind(ignore.CreatedAt, DateTimeKind.Utc)
        };
    }
}

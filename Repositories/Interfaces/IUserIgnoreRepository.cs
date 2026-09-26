using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IUserIgnoreRepository
    {
        /// <summary>Everyone <paramref name="userId"/> ignores, oldest first, with IgnoredUser loaded.</summary>
        Task<List<UserIgnore>> GetByUserAsync(int userId);

        Task<UserIgnore?> GetAsync(int userId, int ignoredUserId);

        Task<int> CountByUserAsync(int userId);

        Task AddAsync(UserIgnore ignore);
        Task DeleteAsync(UserIgnore ignore);
    }
}

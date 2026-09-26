using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface ISiegeMatchRepository
    {
        /// <summary>
        /// The match with its lobby, scenario, participants and objective results; tracked.
        /// <paramref name="includeUsers"/> also loads each participant's User - leave it off where
        /// balances are about to change, so the users are first loaded after their row lock.
        /// </summary>
        Task<SiegeMatch?> GetByIdAsync(int id, bool includeUsers = true);

        /// <summary>The scenario with its teams and objectives (what rewards and validation need); tracked.</summary>
        Task<SiegeScenario?> GetScenarioAsync(int siegeScenarioId);

        Task<bool> LobbyExistsAsync(int siegeLobbyId);

        /// <summary>The users among <paramref name="userIds"/> that exist; tracked (load them after <see cref="LockUsersAsync"/>).</summary>
        Task<List<User>> GetUsersAsync(IEnumerable<int> userIds);

        /// <summary>The ids among <paramref name="userIds"/> that exist; untracked.</summary>
        Task<HashSet<int>> GetExistingUserIdsAsync(IEnumerable<int> userIds);

        /// <summary>Newest first; each filter is optional. Includes lobby, scenario and participants.</summary>
        Task<List<SiegeMatch>> QueryAsync(int? userId, int? siegeLobbyId, SiegeMatchStatus? status, int limit);

        /// <summary>Ids of matches still Created or InProgress.</summary>
        Task<List<int>> GetUnfinishedIdsAsync();

        Task AddAsync(SiegeMatch match);

        Task SaveChangesAsync();

        /// <summary>
        /// Runs <paramref name="work"/> in one database transaction that first takes a row lock on the
        /// match (SELECT ... FOR UPDATE), so concurrent complete/abort calls for the same match (plugin
        /// retries, spooled replays) serialize and the second one sees the first one's status
        /// (READ COMMITTED, so reads after a lock see the latest committed rows). On a
        /// non-relational provider (InMemory tests) the work simply runs.
        /// </summary>
        Task<T> RunLockedAsync<T>(int matchId, Func<Task<T>> work);

        /// <summary>Inside <see cref="RunLockedAsync{T}"/>: row-locks these users before their balances change.</summary>
        Task LockUsersAsync(IEnumerable<int> userIds);
    }
}

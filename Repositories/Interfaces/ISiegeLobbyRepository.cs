using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface ISiegeLobbyRepository
    {
        Task<IEnumerable<SiegeLobby>> GetAllAsync();
        Task<List<SiegeLobby>> GetEnabledAsync();
        Task<SiegeLobby?> GetByIdAsync(int id);
        Task<SiegeLobby?> GetByKeyAsync(string key);
        Task AddAsync(SiegeLobby entity);
        Task UpdateAsync(SiegeLobby entity);
        Task DeleteAsync(SiegeLobby entity);
        Task<PagedResult<SiegeLobby>> SearchAsync(PagedQuery query);
        Task<bool> HasMatchHistoryAsync(int siegeLobbyId);
        Task<List<int>> GetExistingScenarioIdsAsync(IEnumerable<int> siegeScenarioIds);
    }
}

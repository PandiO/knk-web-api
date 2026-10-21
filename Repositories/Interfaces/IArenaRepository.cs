using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    /// <summary>Arena duels (KNG-210): arenas, their battlegrounds, spectator areas and eligible kits.</summary>
    public interface IArenaRepository
    {
        Task<List<Arena>> GetAllAsync();
        Task<Arena?> GetByIdAsync(int id);
        Task<Arena?> GetByKeyAsync(string key);
        Task<bool> KeyTakenAsync(string key, int? excludeId);
        Task<PagedResult<Arena>> SearchAsync(PagedQuery query);

        Task<List<ArenaBattleground>> GetBattlegroundsAsync(int? arenaId);
        Task<ArenaBattleground?> GetBattlegroundAsync(int id);
        Task<bool> BattlegroundKeyTakenAsync(int arenaId, string key, int? excludeId);
        Task<PagedResult<ArenaBattleground>> SearchBattlegroundsAsync(PagedQuery query);

        Task<List<ArenaSpectatorArea>> GetSpectatorAreasAsync(int? arenaId);
        Task<ArenaSpectatorArea?> GetSpectatorAreaAsync(int id);
        Task<PagedResult<ArenaSpectatorArea>> SearchSpectatorAreasAsync(PagedQuery query);

        Task<List<int>> ExistingKitIdsAsync(IEnumerable<int> kitIds);
        Task<Location?> GetLocationAsync(int id);
        Task<bool> TitleBracketExistsAsync(int id);
        Task<bool> StreetExistsAsync(int id);
        Task<bool> DistrictExistsAsync(int id);

        Task AddAsync(object entity);
        Task SaveChangesAsync();
        /// <summary>Deletes the arena with its battlegrounds, spectator areas and kit rows, all through EF, so the
        /// children's TPT base rows in <c>domains</c> go too (a database cascade would only clear the subtype tables).</summary>
        Task DeleteArenaAsync(Arena arena);
        Task DeleteDomainAsync(Domain domain);
    }
}

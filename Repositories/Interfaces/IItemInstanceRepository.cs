using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IItemInstanceRepository
    {
        /// <summary>One instance with its blueprint, grade, owner and enchantments (definitions included), or null.</summary>
        Task<ItemInstance?> GetByIdAsync(long id);

        /// <summary>The subset of <paramref name="definitionIds"/> that exist as EnchantmentDefinition rows.</summary>
        Task<HashSet<int>> GetExistingEnchantmentDefinitionIdsAsync(IEnumerable<int> definitionIds);
    }
}

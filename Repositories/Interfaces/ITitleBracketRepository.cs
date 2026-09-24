using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface ITitleBracketRepository
    {
        /// <summary>All title brackets ordered ascending by MinExperience — the order
        /// TitleService needs to resolve a user's current bracket from their XP total.</summary>
        Task<List<TitleBracket>> GetAllOrderedByMinExperienceAsync();
    }
}

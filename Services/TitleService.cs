using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class TitleService : ITitleService
    {
        private readonly ITitleBracketRepository _repo;

        public TitleService(ITitleBracketRepository repo)
        {
            _repo = repo;
        }

        public async Task<List<TitleBracket>> GetAllOrderedAsync() => await _repo.GetAllOrderedByMinExperienceAsync();

        public async Task<TitleResolutionDto> ResolveAsync(int experiencePoints, Gender? gender = null)
        {
            var brackets = await _repo.GetAllOrderedByMinExperienceAsync();
            if (brackets.Count == 0)
            {
                return new TitleResolutionDto();
            }

            // Highest bracket whose MinExperience is <= the user's XP; falls back to the lowest
            // bracket if XP is somehow below every threshold (e.g. no bracket seeded at 0).
            var current = brackets.LastOrDefault(b => b.MinExperience <= experiencePoints)
                ?? brackets[0];

            var topBracket = brackets[^1];
            var prestige = current.Id == topBracket.Id
                ? Math.Max(0, experiencePoints - topBracket.MinExperience)
                : 0;

            // brackets is ordered ascending by MinExperience, so the next one (if any) is
            // whichever comes right after `current` in that same list.
            var currentIndex = brackets.FindIndex(b => b.Id == current.Id);
            var next = currentIndex >= 0 && currentIndex + 1 < brackets.Count
                ? brackets[currentIndex + 1]
                : null;

            return new TitleResolutionDto
            {
                TitleBracketId = current.Id,
                TitleName = current.NameFor(gender),
                Salary = current.Salary,
                PrestigeExperience = prestige,
                NextTitleBracketId = next?.Id,
                NextTitleName = next?.NameFor(gender),
                NextTitleMinExperience = next?.MinExperience
            };
        }
    }
}

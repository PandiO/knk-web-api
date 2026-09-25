using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Read-only list of title brackets (docs/specs/inventory-menu/CONTENT_PORT_PLAN.md CP3): the
    /// in-game Profile menu's title list and the Player manager's title picker. Brackets are
    /// seeded data (migration AddUserFeaturesPhase6RealTitleDataAndFreeze), edited nowhere else,
    /// so there is no write endpoint.
    /// </summary>
    [ApiController]
    [Route("api/title-brackets")]
    public class TitleBracketsController : ControllerBase
    {
        private readonly ITitleService _titleService;

        public TitleBracketsController(ITitleService titleService)
        {
            _titleService = titleService;
        }

        /// <summary>Every bracket, ordered by MinExperience (lowest first).</summary>
        [HttpGet]
        public async Task<ActionResult<List<TitleBracketDto>>> GetAll()
        {
            var brackets = await _titleService.GetAllOrderedAsync();
            return Ok(brackets.Select(b => new TitleBracketDto
            {
                Id = b.Id,
                MaleName = b.MaleName,
                FemaleName = b.FemaleName,
                MinExperience = b.MinExperience,
                Salary = b.Salary,
                CoinBonus = b.CoinBonus,
                GemBonus = b.GemBonus,
                ExpBonus = b.ExpBonus,
            }).ToList());
        }
    }
}

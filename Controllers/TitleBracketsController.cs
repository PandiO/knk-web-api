using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Read-only title-bracket lookup. Brackets are seeded reference data (user-features Phase 6),
    // so there is no create/update/delete here. Two routes, one controller: siege Phase 3's
    // api/TitleBrackets (web-app form pickers, siege plugin) and the InventoryMenu content port's
    // api/title-brackets (in-game Profile menu and Player manager title picker). The list is a
    // handful of rows, so search filters and pages in memory, ordered by MinExperience (lowest
    // title first).
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/title-brackets")]
    public class TitleBracketsController : ControllerBase
    {
        private readonly ITitleService _titleService;

        public TitleBracketsController(ITitleService titleService)
        {
            _titleService = titleService;
        }

        [HttpGet]
        public async Task<ActionResult<List<TitleBracketDto>>> GetAll()
        {
            var brackets = await _titleService.GetAllOrderedAsync();
            return Ok(brackets.Select(ToDto).ToList());
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var bracket = (await _titleService.GetAllOrderedAsync()).FirstOrDefault(b => b.Id == id);
            return bracket == null ? NotFound() : Ok(ToDto(bracket));
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] PagedQueryDto? query)
        {
            query ??= new PagedQueryDto();
            var pageNumber = Math.Max(1, query.PageNumber);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            IEnumerable<TitleBracket> matches = await _titleService.GetAllOrderedAsync();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.Trim();
                matches = matches.Where(b =>
                    b.MaleName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    b.FemaleName.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            var list = matches.ToList();
            return Ok(new PagedResultDto<TitleBracketDto>
            {
                Items = list.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(ToDto).ToList(),
                TotalCount = list.Count,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        private static TitleBracketDto ToDto(TitleBracket bracket) => new()
        {
            Id = bracket.Id,
            Name = bracket.MaleName,
            MaleName = bracket.MaleName,
            FemaleName = bracket.FemaleName,
            MinExperience = bracket.MinExperience,
            Salary = bracket.Salary,
            CoinBonus = bracket.CoinBonus,
            GemBonus = bracket.GemBonus,
            ExpBonus = bracket.ExpBonus
        };
    }
}

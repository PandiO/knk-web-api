using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Read-only title-bracket lookup for form pickers (siege Phase 3: SiegeScenario.MinTitleBracketId).
    // Brackets are seeded reference data (user-features Phase 6), so there is no create/update/delete
    // here. The list is a handful of rows, so search filters and pages in memory, ordered by
    // MinExperience (lowest title first).
    [ApiController]
    [Route("api/[controller]")]
    public class TitleBracketsController : ControllerBase
    {
        private readonly ITitleService _titleService;

        public TitleBracketsController(ITitleService titleService)
        {
            _titleService = titleService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var brackets = await _titleService.GetAllOrderedAsync();
            return Ok(brackets.Select(ToDto));
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
            MinExperience = bracket.MinExperience
        };
    }
}

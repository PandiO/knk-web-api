using Microsoft.AspNetCore.Mvc;
using Moq;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// GET api/TitleBrackets/{id} and POST api/TitleBrackets/search: the web-app's TitleBracket object
/// picker (a Domain's minimum title for warps, teleport Phase 5; the same routes siege Phase 3 uses).
/// </summary>
[Trait("Category", "API")]
public class TitleBracketPickerTests
{
    private static TitleBracketsController Controller()
    {
        var service = new Mock<ITitleService>();
        service.Setup(s => s.GetAllOrderedAsync()).ReturnsAsync(new List<TitleBracket>
        {
            new() { Id = 1, MaleName = "Peasant", FemaleName = "Peasant", MinExperience = 0 },
            new() { Id = 2, MaleName = "Knight", FemaleName = "Dame", MinExperience = 100 },
            new() { Id = 3, MaleName = "Lord", FemaleName = "Lady", MinExperience = 500 },
        });
        return new TitleBracketsController(service.Object);
    }

    [Fact]
    public async Task GetById_ReturnsTheBracketWithItsMaleNameAsName_Or404()
    {
        var found = Assert.IsType<TitleBracketDto>(Assert.IsType<OkObjectResult>(await Controller().GetById(2)).Value);

        Assert.Equal("Knight", found.Name);
        Assert.Equal("Dame", found.FemaleName);
        Assert.IsType<NotFoundResult>(await Controller().GetById(99));
    }

    [Fact]
    public async Task Search_MatchesEitherName_AndPages()
    {
        var byFemale = Assert.IsType<PagedResultDto<TitleBracketDto>>(Assert.IsType<OkObjectResult>(
            await Controller().Search(new PagedQueryDto { SearchTerm = "dame" })).Value);
        var page2 = Assert.IsType<PagedResultDto<TitleBracketDto>>(Assert.IsType<OkObjectResult>(
            await Controller().Search(new PagedQueryDto { PageNumber = 2, PageSize = 2 })).Value);

        Assert.Equal(2, Assert.Single(byFemale.Items).Id);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(3, Assert.Single(page2.Items).Id);
    }
}

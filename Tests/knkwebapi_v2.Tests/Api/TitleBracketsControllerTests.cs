using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// InventoryMenu content port CP3 (docs/specs/inventory-menu/CONTENT_PORT_PLAN.md §5): the
/// title-bracket list endpoint and the viewer's gender on the plugin-facing user summary.
/// </summary>
[Trait("Category", "API")]
public class TitleBracketsControllerTests
{
    [Fact]
    public async Task GetAll_ReturnsEveryBracketInServiceOrderWithAllFields()
    {
        var service = new Mock<ITitleService>();
        service.Setup(s => s.GetAllOrderedAsync()).ReturnsAsync(new List<TitleBracket>
        {
            new() { Id = 1, MaleName = "Peasant", FemaleName = "Peasant", MinExperience = 0, Salary = 10 },
            new() { Id = 2, MaleName = "Squire", FemaleName = "Maid", MinExperience = 100, Salary = 20,
                CoinBonus = 5, GemBonus = 1, ExpBonus = 3 },
        });

        var result = await new TitleBracketsController(service.Object).GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var brackets = Assert.IsType<List<TitleBracketDto>>(ok.Value);
        Assert.Equal(new[] { 1, 2 }, brackets.Select(b => b.Id));
        var squire = brackets[1];
        Assert.Equal(("Squire", "Maid", 100, 20, 5, 1, 3),
            (squire.MaleName, squire.FemaleName, squire.MinExperience, squire.Salary, squire.CoinBonus, squire.GemBonus, squire.ExpBonus));
    }

    [Fact]
    public async Task GetAll_WithNoBracketsSeeded_ReturnsAnEmptyList()
    {
        var service = new Mock<ITitleService>();
        service.Setup(s => s.GetAllOrderedAsync()).ReturnsAsync(new List<TitleBracket>());

        var result = await new TitleBracketsController(service.Object).GetAll();

        Assert.Empty(Assert.IsType<List<TitleBracketDto>>(Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    [Fact]
    public void UserSummaryMapping_CarriesGender()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<UserMappingProfile>()).CreateMapper();

        var female = mapper.Map<UserSummaryDto>(new User { Id = 1, Username = "a", Gender = Gender.Female });
        var unset = mapper.Map<UserSummaryDto>(new User { Id = 2, Username = "b", Gender = null });

        Assert.Equal(Gender.Female, female.Gender);
        Assert.Null(unset.Gender);
    }

    [Fact]
    public void UserSummaryJson_WritesGenderAsItsName()
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        var json = System.Text.Json.JsonSerializer.Serialize(new UserSummaryDto { Gender = Gender.Female }, options);

        Assert.Contains("\"gender\":\"Female\"", json);
    }
}

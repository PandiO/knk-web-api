using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#280 AM2 (DESIGN.md § 4.6.3): <c>api/Lookup/{type}</c> answers id, label and one detail line
/// through the registry's delegates, never an email, 404 for unknown types and 400 for types without a lookup.
/// </summary>
public class LookupControllerTests
{
    private readonly Mock<IItemBlueprintService> _items = new();
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<ILocationService> _locations = new();
    private readonly Mock<IDomainService> _domains = new();

    private LookupController Controller()
    {
        var services = new ServiceCollection()
            .AddSingleton(_items.Object).AddSingleton(_users.Object).AddSingleton(_locations.Object).AddSingleton(_domains.Object)
            .BuildServiceProvider();
        return new LookupController(new LookupService(services));
    }

    private static string Json(IActionResult result) => JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task Search_LabelsItems_WithCategoryAndGrade_AndCapsThePage()
    {
        PagedQueryDto? sent = null;
        _items.Setup(s => s.SearchAsync(It.IsAny<PagedQueryDto>()))
            .Callback<PagedQueryDto>(q => sent = q)
            .ReturnsAsync(new PagedResultDto<ItemBlueprintListDto>
            {
                Items = new() { new ItemBlueprintListDto { Id = 4, Name = "Iron Sword", CategoryName = "Weapons", GradeName = "Rare", Description = "secret lore" } },
                TotalCount = 31
            });

        var json = Json(await Controller().Search("ItemBlueprint", new LookupSearchRequestDto { SearchTerm = " iron ", Page = 2, PageSize = 500 }));

        Assert.Equal("{\"items\":[{\"id\":4,\"label\":\"Iron Sword\",\"detail\":\"Weapons \\u00B7 Rare\"}],\"total\":31}", json);
        Assert.Equal("iron", sent!.SearchTerm);
        Assert.Equal(2, sent.PageNumber);
        Assert.Equal(LookupService.MaxPageSize, sent.PageSize);
        Assert.Null(sent.Filters);
    }

    [Fact]
    public async Task Users_AreFoundByUsernameOnly_AndNeverShowAnEmail()
    {
        PagedQueryDto? sent = null;
        _users.Setup(s => s.SearchAsync(It.IsAny<PagedQueryDto>()))
            .Callback<PagedQueryDto>(q => sent = q)
            .ReturnsAsync(new PagedResultDto<UserListDto>
            {
                Items = new() { new UserListDto { id = 9, username = "Steve", email = "steve@example.com", uuid = "abc-uuid" } },
                TotalCount = 1
            });
        _users.Setup(s => s.GetByIdAsync(9)).ReturnsAsync(new UserDto { Id = 9, Username = "Steve", Email = "steve@example.com", Uuid = "abc-uuid" });

        var search = Json(await Controller().Search("User", new LookupSearchRequestDto { SearchTerm = "example.com" }));
        var get = Json(await Controller().Get("user", 9));

        Assert.Null(sent!.SearchTerm);
        Assert.Equal("example.com", sent.Filters!["username"]);
        Assert.Equal("{\"items\":[{\"id\":9,\"label\":\"Steve\"}],\"total\":1}", search);
        Assert.Equal("{\"id\":9,\"label\":\"Steve\"}", get);
        Assert.DoesNotContain("@", search + get);
        Assert.DoesNotContain("uuid", search + get);
    }

    [Fact]
    public async Task Get_LabelsALocation_WithWorldAndCoordinates()
    {
        _locations.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new LocationDto { Id = 3, Name = null, World = "world", X = 10.4, Y = 64, Z = -20.6 });

        Assert.Equal("{\"id\":3,\"label\":\"world \\u00B7 10, 64, -21\",\"detail\":\"world \\u00B7 10, 64, -21\"}",
            Json(await Controller().Get("Location", 3)));
    }

    [Fact]
    public async Task Get_ReadsTheEntityTheDomainServiceReturns()
    {
        _domains.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(new Town { Id = 5, Name = "Avalon", WorldName = "world" });
        Assert.Equal("{\"id\":5,\"label\":\"Avalon\",\"detail\":\"world\"}", Json(await Controller().Get("Domain", 5)));
    }

    [Fact]
    public async Task Get_Missing_Is404()
    {
        _items.Setup(s => s.GetByIdAsync(77)).ReturnsAsync((ItemBlueprintReadDto?)null);
        Assert.IsType<NotFoundObjectResult>(await Controller().Get("ItemBlueprint", 77));
    }

    [Fact]
    public async Task UnknownType_Is404_AndATypeWithoutLookup_Is400()
    {
        var unknown = Assert.IsType<NotFoundObjectResult>(await Controller().Search("Nope", null));
        Assert.Contains("UnknownEntityType", JsonSerializer.Serialize(unknown.Value));
        Assert.IsType<NotFoundObjectResult>(await Controller().Get("Nope", 1));

        var noLookup = Assert.IsType<BadRequestObjectResult>(await Controller().Search("CategoryTag", null));
        Assert.Contains("LookupNotAvailable", JsonSerializer.Serialize(noLookup.Value));
        // A type with a get but no search (banner layers): labels yes, picker search no.
        Assert.IsType<BadRequestObjectResult>(await Controller().Search("BannerLayer", null));
    }

    [Fact]
    public void Lookups_CoverEveryDeletableType_AndNoViewOnlyLinkRow()
    {
        foreach (var entry in DashboardAccess.All)
        {
            if (entry.Deletable) Assert.True(entry.LookupGettable, entry.TypeName);
            if (entry.Search != null) Assert.True(entry.LookupSearchable, entry.TypeName);
        }
        Assert.Null(DashboardAccess.Find("CategoryTag")!.Lookup);
        Assert.Null(DashboardAccess.Find("PermissionGrant")!.Lookup);
    }
}

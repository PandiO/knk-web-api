using System.Reflection;
using FluentAssertions;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Lootboxes Phase 1: every web-admin lootbox endpoint requires <see cref="StaffPermissions.ManageLootboxes"/>; only the
/// read-only odds preview is left open because knk-plugin's <c>/lootbox odds</c> calls it anonymously (until KNG-22 adds
/// service auth).
/// </summary>
[Trait("Category", "API")]
public class LootboxControllersAuthTests
{
    public static IEnumerable<object[]> Controllers() => new[]
    {
        new object[] { typeof(LootboxTypesController) },
        new object[] { typeof(LootboxSpecialEntriesController) },
        new object[] { typeof(LootboxSpawnAreasController) },
        new object[] { typeof(LootboxConfigurationController) },
        new object[] { typeof(ItemInstancesController) },
    };

    private static bool RequiresManageLootboxes(MemberInfo member) =>
        member.GetCustomAttributes<RequirePermissionAttribute>()
            .Any(a => (string)a.Arguments![0] == StaffPermissions.ManageLootboxes);

    [Theory]
    [MemberData(nameof(Controllers))]
    public void EveryActionButOdds_RequiresTheLootboxAdminNode(Type controller)
    {
        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        actions.Should().NotBeEmpty();

        foreach (var action in actions)
        {
            var guarded = RequiresManageLootboxes(action) || RequiresManageLootboxes(controller);
            guarded.Should().Be(action.Name != nameof(LootboxTypesController.GetOdds), $"{controller.Name}.{action.Name}");
        }
    }

    [Fact]
    public void ManageLootboxes_IsAnAdminNode()
    {
        StaffPermissions.ManageLootboxes.Should().Be("knk.admin.lootbox.manage");
    }

    [Fact]
    public async Task Odds_MapsNotFoundAndBadStars()
    {
        var service = new Mock<ILootboxTypeService>();
        service.Setup(s => s.GetOddsAsync(1, 5)).ReturnsAsync(new LootboxOddsDto { LootboxTypeId = 1, BoxStars = 5 });
        service.Setup(s => s.GetOddsAsync(1, 9)).ThrowsAsync(new ArgumentException("boxStars must be 1-5."));
        var controller = new LootboxTypesController(service.Object);

        (await controller.GetOdds(1, 5)).Should().BeOfType<OkObjectResult>();
        (await controller.GetOdds(2, 5)).Should().BeOfType<NotFoundResult>();
        (await controller.GetOdds(1, 9)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_ConflictCarriesItsCode()
    {
        var service = new Mock<ILootboxTypeService>();
        service.Setup(s => s.CreateAsync(It.IsAny<LootboxTypeDto>()))
            .ThrowsAsync(new knkwebapi_v2.Services.Lootbox.LootboxConflictException("CategoryTaken", "taken"));

        var result = await new LootboxTypesController(service.Object).Create(new LootboxTypeDto());

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value!.GetType().GetProperty("code")!.GetValue(conflict.Value).Should().Be("CategoryTaken");
    }
}

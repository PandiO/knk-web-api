using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Tests.Services.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// LeaderboardsController (IMPLEMENTATION_PLAN.md §3.2; link 5 acceptance criteria 1, 4): board
/// list, parameter validation, anonymous viewers limited to always-public boards (L1-3), the
/// viewer's own row, and owner-only exclusions (exact-grant attribute, tested in
/// RequireOwnerPermissionAttributeTests).
/// </summary>
public class LeaderboardsControllerTests : IDisposable
{
    private readonly StatisticsTestDb _db = new();

    public LeaderboardsControllerTests()
    {
        _db.Context.PlayerStatTotals.AddRange(
            new PlayerStatTotal { UserId = 1, MetricKey = "active_playtime", ContextKey = "", Value = 100, ReachedAt = StatisticsTestDb.Now },
            new PlayerStatTotal { UserId = 2, MetricKey = "active_playtime", ContextKey = "", Value = 200, ReachedAt = StatisticsTestDb.Now },
            new PlayerStatTotal { UserId = 2, MetricKey = "gate_damage", ContextKey = "siege", Value = 12.5m, ReachedAt = StatisticsTestDb.Now });
        _db.Context.PlayerStatVisibilities.Add(new PlayerStatVisibility
        {
            UserId = 2, SettingKey = "gate_damage", ContextKey = "", Visibility = knkwebapi_v2.Enums.StatisticVisibility.Everyone
        });
        _db.Context.SaveChanges();
        _db.LeaderboardBuilder().BuildAllAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private LeaderboardsController Controller(HttpContext http) =>
        new(_db.LeaderboardQuery()) { ControllerContext = new ControllerContext { HttpContext = http } };

    private static int? Status<T>(ActionResult<T> result) => result.Result switch
    {
        null => 200,
        OkObjectResult => 200,
        ObjectResult o => o.StatusCode,
        StatusCodeResult s => s.StatusCode,
        _ => -1
    };

    private static T Body<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

    [Fact]
    public void GetBoards_ListsEveryBoardWithPeriods()
    {
        var boards = Body(Controller(ServiceAuthTestHelper.Anonymous()).GetBoards());

        Assert.Equal(18, boards.Count);
        var playtime = boards.Single(b => b.BoardKey == "active_playtime");
        Assert.True(playtime.AlwaysPublic);
        Assert.Equal(new[] { "weekly", "monthly", "lifetime" }, playtime.Periods);
        var siegeKills = boards.Single(b => b.BoardKey == "pvp_kills@siege");
        Assert.Equal(("pvp_kills", "siege", false), (siegeKills.Metric, siegeKills.Context, siegeKills.AlwaysPublic));
    }

    [Theory]
    [InlineData("nope", null, 10, 404)]
    [InlineData("active_playtime", "daily", 10, 400)]
    [InlineData("active_playtime", "weekly", 0, 400)]
    [InlineData("active_playtime", "weekly", 51, 400)]
    [InlineData("active_playtime", "WEEKLY", 50, 200)]
    public async Task GetBoard_ValidatesItsParameters(string board, string? period, int top, int status)
    {
        Assert.Equal(status, Status(await Controller(ServiceAuthTestHelper.Anonymous()).GetBoard(board, period, top)));
    }

    [Fact]
    public async Task Anonymous_ReadsAlwaysPublicBoards_ButMustSignInForOthers()
    {
        var anonymous = Controller(ServiceAuthTestHelper.Anonymous());

        var playtime = await anonymous.GetBoard("active_playtime", null);
        Assert.Equal(200, Status(playtime));
        Assert.Equal(new[] { "bob", "alice" }, Body(playtime).Entries.Select(e => e.Username));
        Assert.Null(Body(playtime).Viewer);

        Assert.Equal(401, Status(await anonymous.GetBoard("gate_damage", null)));
    }

    [Fact]
    public async Task SignedInViewers_GetTheirOwnRow_FromTheWebOrThePlugin()
    {
        var web = await Controller(ServiceAuthTestHelper.WebUser(1)).GetBoard("active_playtime", "lifetime");
        Assert.Equal((2, 100m), (Body(web).Viewer!.Rank, Body(web).Viewer!.Value));

        var plugin = await Controller(ServiceAuthTestHelper.Plugin(actingUserId: 2)).GetBoard("gate_damage", null);
        Assert.Equal(200, Status(plugin));
        var entry = Assert.Single(Body(plugin).Entries);
        Assert.Equal((13m, 12.5m), (entry.Value, entry.RawValue)); // points display half away from zero
        Assert.Equal(1, Body(plugin).Viewer!.Rank);

        // A plugin call without an acting player is anonymous.
        Assert.Equal(401, Status(await Controller(ServiceAuthTestHelper.Plugin()).GetBoard("gate_damage", null)));
    }

    [Theory]
    [InlineData(nameof(LeaderboardsController.GetExclusions))]
    [InlineData(nameof(LeaderboardsController.PutExclusion))]
    [InlineData(nameof(LeaderboardsController.DeleteExclusion))]
    public void Exclusions_AreOwnerOnly(string action)
    {
        var method = typeof(LeaderboardsController).GetMethod(action)!;
        var gate = method.GetCustomAttribute<RequireOwnerPermissionAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(OwnerPermissions.LeaderboardManage, gate!.Node);
    }

    [Fact]
    public void ReadRoutes_CarryNoOwnerGate()
    {
        Assert.Null(typeof(LeaderboardsController).GetMethod(nameof(LeaderboardsController.GetBoard))!.GetCustomAttribute<RequireOwnerPermissionAttribute>());
        Assert.Null(typeof(LeaderboardsController).GetMethod(nameof(LeaderboardsController.GetBoards))!.GetCustomAttribute<RequireOwnerPermissionAttribute>());
    }

    [Fact]
    public async Task PutAndDeleteExclusion_RecordTheOwner_And404ForUnknownUsers()
    {
        var owner = Controller(ServiceAuthTestHelper.WebUser(1));

        Assert.IsType<NoContentResult>(await owner.PutExclusion(2, new LeaderboardExclusionRequestDto { Reason = "boosting" }, default));
        Assert.IsType<NotFoundObjectResult>(await owner.PutExclusion(999, null, default));
        var exclusion = Assert.Single(Body(await owner.GetExclusions(default)));
        Assert.Equal((2, "boosting", (int?)1), (exclusion.UserId, exclusion.Reason, exclusion.ExcludedByUserId));

        Assert.IsType<NoContentResult>(await owner.DeleteExclusion(2, default));
        Assert.IsType<NotFoundObjectResult>(await owner.DeleteExclusion(999, default));
        Assert.Empty(Body(await owner.GetExclusions(default)));
    }
}

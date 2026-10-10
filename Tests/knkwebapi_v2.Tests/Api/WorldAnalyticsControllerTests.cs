using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Tests.Services.WorldAnalytics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// WorldAnalyticsController (IMPLEMENTATION_PLAN.md §3.4; link 7 acceptance criterion 2): the batch
/// route is plugin-only, every read needs knk.owner.analytics.view (wildcards count, D24), the
/// kill switch answers 503 and parameters are validated.
/// </summary>
public class WorldAnalyticsControllerTests : IDisposable
{
    private readonly WorldAnalyticsTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private WorldAnalyticsController Controller() =>
        new(_db.Ingestion(), _db.Query(), Options.Create(_db.Options))
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(9) }
        };

    private static int? Status<T>(ActionResult<T> result) => result.Result switch
    {
        null => 200,
        OkObjectResult => 200,
        ObjectResult o => o.StatusCode,
        StatusCodeResult s => s.StatusCode,
        _ => -1
    };

    [Theory]
    [InlineData("GetHeatmapWorlds")]
    [InlineData("GetHeatmap")]
    [InlineData("GetMenuFunnels")]
    [InlineData("GetDomains")]
    public void Reads_AreOwnerOnly(string action)
    {
        var method = typeof(WorldAnalyticsController).GetMethod(action)!;

        Assert.Equal(new[] { OwnerPermissions.AnalyticsView },
            method.GetCustomAttributes<RequireOwnerPermissionAttribute>().Select(a => a.Node));
        Assert.Empty(method.GetCustomAttributes<RequirePluginServiceAttribute>());
    }

    [Fact]
    public void Batches_ArePluginOnly()
    {
        var method = typeof(WorldAnalyticsController).GetMethod("PostBatch")!;

        Assert.NotNull(method.GetCustomAttribute<RequirePluginServiceAttribute>());
        Assert.Empty(method.GetCustomAttributes<RequireOwnerPermissionAttribute>());
        Assert.Equal("api/world-analytics", typeof(WorldAnalyticsController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public async Task PostBatch_Disabled_Answers503_AndStoresNothing()
    {
        _db.Options.Enabled = false;
        var batch = WorldAnalyticsTestDb.Batch();
        batch.MovementCells!.Add(WorldAnalyticsTestDb.Cell(0, 0, 1));

        var result = await Controller().PostBatch(batch, default);

        Assert.Equal(503, Status(result));
        Assert.Empty(_db.NewContext().WorldAnalyticsBatches);
    }

    [Fact]
    public async Task PostBatch_StructuralError_Answers400_ValidBatch200()
    {
        Assert.Equal(400, Status(await Controller().PostBatch(new WorldAnalyticsBatchDto(), default)));
        Assert.Equal(200, Status(await Controller().PostBatch(WorldAnalyticsTestDb.Batch(), default)));
    }

    [Fact]
    public async Task Reads_ValidateParameters()
    {
        var today = WorldAnalyticsTestDb.Today;
        var controller = Controller();

        Assert.Equal(400, Status(await controller.GetHeatmap(null, null, null, null, default)));
        Assert.Equal(400, Status(await controller.GetHeatmap("world", null, null, 0, default)));
        Assert.Equal(400, Status(await controller.GetHeatmap("world", today, today.AddDays(-1), null, default)));
        Assert.Equal(400, Status(await controller.GetMenuFunnels(null, today.AddDays(-200), today, default)));
        Assert.Equal(400, Status(await controller.GetDomains(null, null, "teleport", default)));
        Assert.Equal(400, Status(await controller.GetHeatmapWorlds(today, today.AddDays(-3), default)));

        Assert.Equal(200, Status(await controller.GetHeatmap("world", null, null, 32, default)));
        Assert.Equal(200, Status(await controller.GetMenuFunnels("profile.main", null, null, default)));
        Assert.Equal(200, Status(await controller.GetDomains(null, null, "enter", default)));
        Assert.Equal(200, Status(await controller.GetHeatmapWorlds(null, null, default)));
    }
}

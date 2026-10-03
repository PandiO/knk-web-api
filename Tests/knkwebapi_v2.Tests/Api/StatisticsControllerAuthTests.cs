using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;
using knkwebapi_v2.Tests.Services.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// StatisticsController gates (IMPLEMENTATION_PLAN.md §3.1; link 2 acceptance criteria 2, 7, 9):
/// ingestion is plugin-only and answers 503 when Statistics:Enabled is false; the visibility
/// settings are the player's own (staff read-only); reads resolve the viewer from the caller.
/// </summary>
public class StatisticsControllerAuthTests : IDisposable
{
    private readonly StatisticsTestDb _db = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    public StatisticsControllerAuthTests()
    {
        // User 2 is staff (knk.admin.statistics.view); everyone else isn't.
        _permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), StaffPermissions.ViewStatistics))
            .ReturnsAsync((int id, string node) => new PermissionCheckResponseDto
            {
                UserId = id, Node = node,
                Result = id == 2 ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });
    }

    public void Dispose() => _db.Dispose();

    private StatisticsController Controller(HttpContext http, bool enabled = true)
    {
        var options = new StatisticsOptions { Enabled = enabled };
        var controller = new StatisticsController(
            new StatisticsIngestionService(_db.Repository(), Options.Create(options), null, null, _db.Time),
            _db.Query(), _db.Visibility(), new StatisticsViewerResolver(_permissions.Object), Options.Create(options));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static int? Status<T>(ActionResult<T> result) => result.Result switch
    {
        null => 200,
        OkObjectResult => 200,
        ObjectResult o => o.StatusCode,
        StatusCodeResult s => s.StatusCode,
        _ => -1
    };

    [Fact]
    public async Task PostBatch_IsPluginOnly()
    {
        Assert.Equal(401, await ServiceAuthTestHelper.RunGates(typeof(StatisticsController), nameof(StatisticsController.PostBatch), ServiceAuthTestHelper.Anonymous()));
        Assert.Equal(403, await ServiceAuthTestHelper.RunGates(typeof(StatisticsController), nameof(StatisticsController.PostBatch), ServiceAuthTestHelper.WebUser(1)));
        Assert.Null(await ServiceAuthTestHelper.RunGates(typeof(StatisticsController), nameof(StatisticsController.PostBatch), ServiceAuthTestHelper.Plugin()));
    }

    [Fact]
    public async Task PostBatch_Answers503_WhenStatisticsAreDisabled_AndAppliesNothing()
    {
        var batch = new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Counters = new() { new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 1, OccurredAt = StatisticsTestDb.Now } }
        };

        var disabled = await Controller(ServiceAuthTestHelper.Plugin(), enabled: false).PostBatch(batch, default);
        Assert.Equal(503, Status(disabled));
        Assert.Empty(_db.Context.PlayerStatBatches);

        var enabled = await Controller(ServiceAuthTestHelper.Plugin()).PostBatch(batch, default);
        Assert.Equal(200, Status(enabled));
        Assert.Equal(1m, _db.Total(1, "pve_kills", "open_world"));
    }

    [Fact]
    public async Task PostBatch_StructuralErrorsAre400()
    {
        var result = await Controller(ServiceAuthTestHelper.Plugin()).PostBatch(new StatisticsBatchDto(), default);

        Assert.Equal(400, Status(result));
    }

    [Fact]
    public async Task GetVisibility_SelfAndStaff_Only()
    {
        Assert.Equal(401, Status(await Controller(ServiceAuthTestHelper.Anonymous()).GetVisibility(1, default)));
        Assert.Equal(401, Status(await Controller(ServiceAuthTestHelper.Plugin()).GetVisibility(1, default))); // no acting user
        Assert.Equal(403, Status(await Controller(ServiceAuthTestHelper.WebUser(3)).GetVisibility(1, default)));
        Assert.Equal(200, Status(await Controller(ServiceAuthTestHelper.WebUser(1)).GetVisibility(1, default)));
        Assert.Equal(200, Status(await Controller(ServiceAuthTestHelper.Plugin(actingUserId: 1)).GetVisibility(1, default)));
        Assert.Equal(200, Status(await Controller(ServiceAuthTestHelper.WebUser(2)).GetVisibility(1, default))); // staff
        Assert.Equal(404, Status(await Controller(ServiceAuthTestHelper.WebUser(99)).GetVisibility(99, default)));
    }

    [Fact]
    public async Task PutVisibility_SelfOnly_StaffCantChangeIt()
    {
        var update = new StatisticsVisibilityUpdateDto
        {
            Changes = new() { new() { SettingKey = "logins", Context = "", Expected = StatisticVisibility.Nobody, Visibility = StatisticVisibility.Everyone } }
        };

        Assert.Equal(401, Status(await Controller(ServiceAuthTestHelper.Anonymous()).PutVisibility(1, update, default)));
        Assert.Equal(403, Status(await Controller(ServiceAuthTestHelper.WebUser(2)).PutVisibility(1, update, default)));
        Assert.Equal(403, Status(await Controller(ServiceAuthTestHelper.Plugin(actingUserId: 3)).PutVisibility(1, update, default)));
        Assert.Empty(_db.Context.PlayerStatVisibilities);

        Assert.Equal(200, Status(await Controller(ServiceAuthTestHelper.Plugin(actingUserId: 1)).PutVisibility(1, update, default)));
        // The same change again: the expected value is stale now → 409 with the current settings.
        var conflict = await Controller(ServiceAuthTestHelper.WebUser(1)).PutVisibility(1, update, default);
        Assert.Equal(409, Status(conflict));
    }

    [Fact]
    public async Task PutVisibility_ValidationErrorsAre400()
    {
        var update = new StatisticsVisibilityUpdateDto
        {
            Changes = new() { new() { SettingKey = "deaths", Context = "siege", Expected = StatisticVisibility.Nobody, Visibility = StatisticVisibility.Everyone } }
        };

        Assert.Equal(400, Status(await Controller(ServiceAuthTestHelper.WebUser(1)).PutVisibility(1, update, default)));
    }

    [Fact]
    public async Task Reads_ResolveTheViewer()
    {
        var anonymous = await Controller(ServiceAuthTestHelper.Anonymous()).GetUser(1, null, null, default);
        Assert.Equal("anonymous", ((PlayerStatisticsDto)((OkObjectResult)anonymous.Result!).Value!).Viewer);

        var self = await Controller(ServiceAuthTestHelper.Plugin(actingUserId: 1)).GetUser(1, null, null, default);
        Assert.Equal("self", ((PlayerStatisticsDto)((OkObjectResult)self.Result!).Value!).Viewer);

        var staff = await Controller(ServiceAuthTestHelper.WebUser(2)).GetUser(1, null, null, default);
        Assert.Equal("staff", ((PlayerStatisticsDto)((OkObjectResult)staff.Result!).Value!).Viewer);

        var other = await Controller(ServiceAuthTestHelper.WebUser(3)).GetUser(1, null, null, default);
        Assert.Equal("signedIn", ((PlayerStatisticsDto)((OkObjectResult)other.Result!).Value!).Viewer);

        Assert.Equal(404, Status(await Controller(ServiceAuthTestHelper.Anonymous()).GetUser(99, null, null, default)));
        Assert.Equal(400, Status(await Controller(ServiceAuthTestHelper.Anonymous()).GetUser(1, "decade", null, default)));
        Assert.Equal(403, Status(await Controller(ServiceAuthTestHelper.WebUser(3)).GetTitleHistory(1, 1, 20, default)));
        Assert.Equal(200, Status(await Controller(ServiceAuthTestHelper.WebUser(1)).GetTitleHistory(1, 1, 20, default)));
        Assert.Equal(403, Status(await Controller(ServiceAuthTestHelper.Anonymous()).GetSeries(1, "logins", null, "day", null, null, default)));
        Assert.Equal(200, Status(await Controller(ServiceAuthTestHelper.Anonymous()).GetSeries(1, "active_playtime", null, "day", null, null, default)));
    }

    [Fact]
    public void Catalog_IsPublic()
    {
        var result = Controller(ServiceAuthTestHelper.Anonymous()).GetCatalog();

        Assert.IsType<OkObjectResult>(result.Result);
    }
}

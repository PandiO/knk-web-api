using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;
using knkwebapi_v2.Tests.Services.Statistics;
using knkwebapi_v2.Tests.Services.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// TelemetryController, PrivacyController and POST api/statistics/rebuild (IMPLEMENTATION_PLAN.md
/// §3.3; link 6 acceptance criteria 1, 3, 4): every route carries the right gate (plugin key or an
/// exact owner grant — wildcards refused), the kill switch answers 503, and parameters are validated.
/// </summary>
public class TelemetryPrivacyControllerTests : IDisposable
{
    private readonly TelemetryTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static int? Status<T>(ActionResult<T> result) => result.Result switch
    {
        null => 200,
        OkObjectResult => 200,
        ObjectResult o => o.StatusCode,
        StatusCodeResult s => s.StatusCode,
        _ => -1
    };

    private TelemetryController Telemetry(HttpContext? http = null) =>
        new(_db.Ingestion(_db.Queue()), _db.Query(), Options.Create(_db.TelemetryOptions))
        {
            ControllerContext = new ControllerContext { HttpContext = http ?? ServiceAuthTestHelper.WebUser(9) }
        };

    private PrivacyController Privacy() =>
        new(_db.Privacy()) { ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(9) } };

    public static IEnumerable<object[]> OwnerRoutes() => new[]
    {
        new object[] { typeof(TelemetryController), "Search", OwnerPermissions.TelemetryView },
        new object[] { typeof(TelemetryController), "GetEvent", OwnerPermissions.TelemetryView },
        new object[] { typeof(TelemetryController), "GetTimeline", OwnerPermissions.TelemetryView },
        new object[] { typeof(TelemetryController), "GetHealth", OwnerPermissions.TelemetryView },
        new object[] { typeof(TelemetryController), "GetTestRuns", OwnerPermissions.TelemetryManage },
        new object[] { typeof(TelemetryController), "StartTestRun", OwnerPermissions.TelemetryManage },
        new object[] { typeof(TelemetryController), "EndTestRun", OwnerPermissions.TelemetryManage },
        new object[] { typeof(TelemetryController), "GetEnhancedTargets", OwnerPermissions.TelemetryManage },
        new object[] { typeof(TelemetryController), "AddEnhancedTarget", OwnerPermissions.TelemetryManage },
        new object[] { typeof(TelemetryController), "RemoveEnhancedTarget", OwnerPermissions.TelemetryManage },
        new object[] { typeof(StatisticsController), "Rebuild", OwnerPermissions.TelemetryManage }
    };

    [Theory]
    [MemberData(nameof(OwnerRoutes))]
    public void OwnerRoutes_CarryTheOwnerAttribute(Type controller, string action, string node)
    {
        var method = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m => m.Name == action);

        Assert.Equal(new[] { node }, method.GetCustomAttributes<RequireOwnerPermissionAttribute>().Select(a => a.Node));
        Assert.Empty(method.GetCustomAttributes<RequirePluginServiceAttribute>());
    }

    [Fact]
    public void PrivacyController_IsOwnerOnlyAsAWhole()
    {
        Assert.Equal(new[] { OwnerPermissions.PrivacyManage },
            typeof(PrivacyController).GetCustomAttributes<RequireOwnerPermissionAttribute>().Select(a => a.Node));
    }

    [Theory]
    [InlineData("PostBatch")]
    [InlineData("GetConfig")]
    public async Task PluginRoutes_NeedTheKey(string action)
    {
        Assert.Equal(StatusCodes.Status401Unauthorized, await ServiceAuthTestHelper.RunGates(typeof(TelemetryController), action, ServiceAuthTestHelper.Anonymous()));
        Assert.Equal(StatusCodes.Status403Forbidden, await ServiceAuthTestHelper.RunGates(typeof(TelemetryController), action, ServiceAuthTestHelper.WebUser(1)));
        Assert.Null(await ServiceAuthTestHelper.RunGates(typeof(TelemetryController), action, ServiceAuthTestHelper.Plugin()));
    }

    [Theory]
    [InlineData(OwnerPermissions.TelemetryView, null)]
    [InlineData("*", 403)]
    [InlineData("knk.*", 403)]
    [InlineData("knk.owner.*", 403)]
    [InlineData("knk.owner.telemetry.*", 403)]
    public async Task OwnerNodes_NeedAnExactGrant(string granted, int? expected)
    {
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new User { Id = id, Username = "u" + id });
        var grants = new Mock<IPermissionGrantRepository>();
        grants.Setup(r => r.GetActiveGrantsForHolderAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync((int holder, DateTime _) => holder == 9
                ? new List<PermissionGrant> { new() { HolderId = 9, Node = granted, Value = true } }
                : new List<PermissionGrant>());
        var groups = new Mock<IPermissionGroupRepository>();
        groups.Setup(r => r.GetActiveGroupsForUserAsync(It.IsAny<int>(), It.IsAny<DateTime>())).ReturnsAsync(new List<PermissionGroup>());
        var resolver = new PermissionResolutionService(users.Object, grants.Object, groups.Object);

        var context = new AuthorizationFilterContext(new ActionContext(ServiceAuthTestHelper.WebUser(9), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
        await new RequireOwnerPermissionFilter(OwnerPermissions.TelemetryView, resolver).OnAuthorizationAsync(context);

        Assert.Equal(expected, context.Result is ObjectResult o ? o.StatusCode : null);
    }

    [Fact]
    public async Task PostBatch_ValidatesSize_AndAnswers503WhenDisabled()
    {
        var events = Enumerable.Range(0, 501).Select(_ => TelemetryTestDb.Event()).ToList();

        Assert.Equal(400, Status(await Telemetry(ServiceAuthTestHelper.Plugin()).PostBatch(events, default)));
        Assert.Equal(400, Status(await Telemetry(ServiceAuthTestHelper.Plugin()).PostBatch(null, default)));
        var ok = await Telemetry(ServiceAuthTestHelper.Plugin()).PostBatch(events.Take(3).ToList(), default);
        Assert.Equal(3, ((TelemetryBatchResultDto)((OkObjectResult)ok.Result!).Value!).Accepted);

        _db.TelemetryOptions.Enabled = false;
        Assert.Equal(503, Status(await Telemetry(ServiceAuthTestHelper.Plugin()).PostBatch(events.Take(1).ToList(), default)));
    }

    [Fact]
    public async Task Search_AndTimeline_ValidateTheirFilters()
    {
        var c = Telemetry();
        Assert.Equal(400, Status(await c.Search(null, null, null, null, null, null, null, null, "sometimes")));
        Assert.Equal(400, Status(await c.Search(null, null, null, null, null, null, null, null, "1")));
        Assert.Equal(400, Status(await c.Search(null, null, null, null, null, null, null, null, null, limit: 0)));
        Assert.Equal(400, Status(await c.Search(null, null, null, null, null, null, null, null, null, before: "garbage")));
        Assert.Equal(200, Status(await c.Search(1, null, null, null, null, null, null, "session.join", "Succeeded")));

        var now = DateTime.UtcNow;
        Assert.Equal(400, Status(await c.GetTimeline(1, now.AddDays(-40), now)));
        Assert.Equal(400, Status(await c.GetTimeline(1, now, now.AddHours(-1))));
        Assert.Equal(404, Status(await c.GetTimeline(99, null, null)));
        Assert.Equal(200, Status(await c.GetTimeline(1, null, null)));
        Assert.Equal(404, Status(await c.GetEvent(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task TestRunsAndTargets_ValidateTheirInput()
    {
        var c = Telemetry();
        Assert.Equal(400, Status(await c.StartTestRun(new TelemetryTestRunCreateDto { Name = " " }, default)));
        var created = await c.StartTestRun(new TelemetryTestRunCreateDto { Name = "Siege alpha" }, default);
        Assert.Equal(201, Status(created));
        var run = (TelemetryTestRunDto)((ObjectResult)created.Result!).Value!;

        Assert.Equal(400, Status(await c.AddEnhancedTarget(new EnhancedTargetCreateDto { UserId = 1, TestRunId = run.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) }, default)));
        Assert.Equal(400, Status(await c.AddEnhancedTarget(new EnhancedTargetCreateDto { ExpiresAt = DateTime.UtcNow.AddHours(1) }, default)));
        Assert.Equal(400, Status(await c.AddEnhancedTarget(new EnhancedTargetCreateDto { UserId = 1, ExpiresAt = DateTime.UtcNow.AddHours(-1) }, default)));
        Assert.Equal(400, Status(await c.AddEnhancedTarget(new EnhancedTargetCreateDto { UserId = 1, ExpiresAt = DateTime.UtcNow.AddDays(30) }, default)));
        Assert.Equal(404, Status(await c.AddEnhancedTarget(new EnhancedTargetCreateDto { UserId = 99, ExpiresAt = DateTime.UtcNow.AddHours(1) }, default)));
        Assert.Equal(201, Status(await c.AddEnhancedTarget(new EnhancedTargetCreateDto { TestRunId = run.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) }, default)));
        Assert.Equal(200, Status(await c.EndTestRun(run.Id, default)));
        Assert.Equal(404, Status(await c.EndTestRun(999, default)));
        Assert.IsType<NotFoundObjectResult>(await c.RemoveEnhancedTarget(999, default));
    }

    [Fact]
    public async Task Privacy_RequestConflictPreviewExecuteCancel()
    {
        var c = Privacy();
        Assert.Equal(400, Status(await c.CreateRequest(new PrivacyDeletionRequestCreateDto { UserId = 0 }, default)));
        Assert.Equal(404, Status(await c.CreateRequest(new PrivacyDeletionRequestCreateDto { UserId = 99 }, default)));
        var created = await c.CreateRequest(new PrivacyDeletionRequestCreateDto { UserId = 1, Note = "ticket 12" }, default);
        Assert.Equal(201, Status(created));
        var id = ((PrivacyDeletionRequestDto)((ObjectResult)created.Result!).Value!).Id;
        Assert.Equal(409, Status(await c.CreateRequest(new PrivacyDeletionRequestCreateDto { UserId = 1 }, default)));

        var preview = await c.Execute(id, dryRun: true);
        Assert.Equal(PrivacyRequestStatus.Pending, ((PrivacyDeletionRequestDto)((OkObjectResult)preview.Result!).Value!).Status);
        var executed = await c.Execute(id);
        Assert.Equal(PrivacyRequestStatus.Completed, ((PrivacyDeletionRequestDto)((OkObjectResult)executed.Result!).Value!).Status);
        Assert.Equal(409, Status(await c.Cancel(id, default)));
        Assert.Equal(404, Status(await c.Execute(999)));

        Assert.Equal(400, Status(await c.GetRequests("later", default)));
        var completed = (List<PrivacyDeletionRequestDto>)((OkObjectResult)(await c.GetRequests("completed", default)).Result!).Value!;
        Assert.Equal(("deleted-1", 9), (Assert.Single(completed).Username, completed[0].ExecutedByUserId));
    }

    [Fact]
    public async Task Rebuild_ValidatesTheProjection_AndHonoursTheSwitch()
    {
        var statsDb = new StatisticsTestDb();
        var rebuild = new Mock<IStatisticsRebuildService>();
        rebuild.Setup(r => r.RebuildAsync("ledger", 1, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        StatisticsController Controller(bool enabled)
        {
            var options = Options.Create(new StatisticsOptions { Enabled = enabled });
            return new StatisticsController(new StatisticsIngestionService(statsDb.Repository(), options, null, null, statsDb.Time),
                statsDb.Query(), statsDb.Visibility(), new StatisticsViewerResolver(new Mock<IPermissionResolutionService>().Object), options,
                rebuild.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(9) }
            };
        }

        Assert.Equal(400, Status(await Controller(true).Rebuild(new StatisticsRebuildRequestDto { Projection = "economy" }, default)));
        var ok = await Controller(true).Rebuild(new StatisticsRebuildRequestDto { Projection = " Ledger ", UserId = 1 }, default);
        Assert.Equal(("ledger", 1, 4), (((StatisticsRebuildResultDto)((OkObjectResult)ok.Result!).Value!).Projection,
            ((StatisticsRebuildResultDto)((OkObjectResult)ok.Result!).Value!).UserId, ((StatisticsRebuildResultDto)((OkObjectResult)ok.Result!).Value!).Reprojected));
        Assert.Equal(503, Status(await Controller(false).Rebuild(null, default)));
        statsDb.Dispose();
    }
}

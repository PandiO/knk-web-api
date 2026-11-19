using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>The engine's staff endpoints (P3, #86): run-now and settle answer 409 while Production:Enabled is off (A21,
/// smoke § 4.1 step 6); the production view is 404 for a missing structure; workers outside 0..total are 400.</summary>
public class ProductionEngineControllersTests
{
    [Fact]
    public async Task RunNow_Is409WhileTheFeatureIsOff_And200WhileOn()
    {
        using var off = new ProductionEngineTestDb(productionEnabled: false);
        Assert.IsType<ConflictObjectResult>((await new ProductionController(off.Engine).RunNow(default)).Result);

        using var on = new ProductionEngineTestDb();
        await on.QuarryAsync(cap: null);
        on.Advance(10);
        var ok = Assert.IsType<OkObjectResult>((await new ProductionController(on.Engine).RunNow(default)).Result);
        Assert.Equal(1, Assert.IsType<ProductionSweepResultDto>(ok.Value).LinesSettled);
    }

    [Fact]
    public async Task Settle_Is409WhileOff_ProductionViewIs404ForAMissingStructure_AndWorkersAreChecked()
    {
        using var off = new ProductionEngineTestDb(productionEnabled: false);
        var (sid, line, _) = await off.QuarryAsync(cap: null);
        var lines = new ProductionLinesController(off.Lines);
        var settings = new knkwebapi_v2.Services.Territory.ProductionSettingsReader(off.Db);
        Assert.IsType<ConflictObjectResult>(await lines.Settle(line, off.Engine, settings));

        var structures = new ProductionStructuresController(off.Structures)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        Assert.IsType<NotFoundResult>(await structures.GetProduction(9999, off.Engine));
        var view = Assert.IsType<OkObjectResult>(await structures.GetProduction(sid, off.Engine));
        Assert.False(Assert.IsType<ProductionOverviewDto>(view.Value).FeatureEnabled);
        Assert.IsType<BadRequestObjectResult>(await structures.SetWorkers(sid, new WorkersPresentUpdateDto { WorkersPresent = 5 }, off.Engine));
        Assert.IsType<OkObjectResult>(await structures.SetWorkers(sid, new WorkersPresentUpdateDto { WorkersPresent = 1 }, off.Engine));
    }
}

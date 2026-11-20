using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>The transport endpoints (P5, #79): run-now is 409 while Production:Enabled is off (A21) and 200 while on;
/// a bad manual order is 400 with its reasons; cancel of a final order is 409; missing orders, lanes and structures are
/// 404; a duplicate lane is 409.</summary>
public class TransportControllersTests
{
    private static T WithContext<T>(T c) where T : ControllerBase
    {
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return c;
    }

    [Fact]
    public async Task RunNow_Is409WhileOff_And200WhileOn()
    {
        using var off = new TransportTestDb(productionEnabled: false);
        Assert.IsType<ConflictObjectResult>((await WithContext(new TransportOrdersController(off.Transport)).RunNow(default)).Result);
        using var on = new TransportTestDb();
        await on.SetUpAsync();
        var ok = Assert.IsType<OkObjectResult>((await WithContext(new TransportOrdersController(on.Transport)).RunNow(default)).Result);
        Assert.True(Assert.IsType<TransportSweepResultDto>(ok.Value).FeatureEnabled);
    }

    [Fact]
    public async Task Create_400_Cancel_409_And_404s()
    {
        using var t = new TransportTestDb();
        await t.SetUpAsync();
        var c = WithContext(new TransportOrdersController(t.Transport));
        var bad = Assert.IsType<BadRequestObjectResult>(await c.Create(t.Manual(t.NorthStorage, 0), default));
        Assert.Contains("positive", System.Text.Json.JsonSerializer.Serialize(bad.Value));
        await t.Put(t.QuarryOutput, 5);
        var created = Assert.IsType<CreatedAtRouteResult>(await c.Create(t.Manual(t.NorthStorage, 5), default));
        int id = Assert.IsType<TransportOrderDto>(created.Value).Id;
        Assert.IsType<OkObjectResult>(await c.Cancel(id, new TransportCancelDto { Reason = "test" }, default));
        Assert.IsType<ConflictObjectResult>(await c.Cancel(id, null, default));
        Assert.IsType<NotFoundResult>(await c.Cancel(9999, null, default));
        Assert.IsType<NotFoundResult>(await c.GetById(9999, default));

        var structures = WithContext(new ProductionStructuresController(t.E.Structures));
        Assert.IsType<NotFoundResult>(await structures.GetTransport(9999, t.Transport));
        Assert.IsType<OkObjectResult>(await structures.GetTransport(t.Quarry, t.Transport));

        var lanes = WithContext(new TransportLanesController(t.Lanes));
        Assert.IsType<ConflictObjectResult>(await lanes.Create(new TransportLaneDto { FromStructureId = t.Quarry, ToStructureId = t.North, RoadDistance = 1 }));
        Assert.IsType<BadRequestObjectResult>(await lanes.Create(new TransportLaneDto { FromStructureId = t.Quarry, ToStructureId = t.Quarry, RoadDistance = 1 }));
        Assert.IsType<NotFoundResult>(await lanes.Delete(9999));
        Assert.IsType<NotFoundResult>(await lanes.GetById(9999));
    }
}

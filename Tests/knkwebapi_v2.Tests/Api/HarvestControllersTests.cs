using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Tests.Services.Production;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// The P8a routes (PandiO/knk-workspace#685): reports are for the game server only, rules and pools follow the World
/// area, and results map to 400, 404 and 409 (<c>ProductionDisabled</c> while the flag is off, A21).
/// </summary>
public class HarvestControllersTests : IDisposable
{
    private readonly ProductionYieldTestDb _y = new();

    public void Dispose() => _y.Dispose();

    private HarvestService Harvest() =>
        new(_y.T.Db, _y.T.Stock, new ProductionSettingsReader(_y.T.Db), _y.Engine, _y.T.Clock);

    private HarvestRuleService Rules() =>
        new(_y.T.Db, new ProductionSettingsReader(_y.T.Db), new ProductionYieldCalculator(_y.T.Db), _y.T.Clock);

    [Fact]
    public void TheRoutes_CarryTheirAccessRules()
    {
        Assert.NotNull(typeof(HarvestController).GetMethod("Report")!.GetCustomAttribute<RequirePluginServiceAttribute>());
        Assert.Equal("HarvestRule", typeof(HarvestController).GetMethod("Catalog")!.GetCustomAttribute<DashboardReadsAttribute>()!.TypeName);
        foreach (var c in new[] { typeof(HarvestRulesController), typeof(ResourcePoolsController), typeof(PlayerHarvestsController) })
            Assert.Equal(StaffPermissions.ManageWorld, c.GetCustomAttribute<RequireServiceOrPermissionForWritesAttribute>()!.Node);
        Assert.Equal("World", DashboardAccess.Find("PlayerHarvest")!.Area.ToString());
        Assert.Null(DashboardAccess.Find("PlayerHarvest")!.Delete);
    }

    [Fact]
    public async Task Results_MapTo400_404_And409WhileOff()
    {
        var rules = new HarvestRulesController(Rules());
        Assert.IsType<BadRequestObjectResult>(await rules.Create(new HarvestRuleDto { ProductionKindId = ProductionYieldTestDb.LoggerKind, Name = "x" }));
        Assert.IsType<NotFoundObjectResult>(await rules.Balance(4242, default));

        _y.T.SetSettings(s => s.Enabled = false);
        var off = await new HarvestController(Harvest()).Report(new HarvestReportDto
        {
            Entries = new() { new HarvestReportEntryDto { IdempotencyKey = "k", UserId = 1, HarvestRuleId = 1, ProductionAreaId = 1 } },
        }, default);
        var conflict = Assert.IsType<ConflictObjectResult>(off);
        Assert.Contains("ProductionDisabled", System.Text.Json.JsonSerializer.Serialize(conflict.Value));
        var catalog = Assert.IsType<OkObjectResult>(await new HarvestController(Harvest()).Catalog(default));
        Assert.False(((HarvestCatalogDto)catalog.Value!).FeatureEnabled);
    }
}

using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>The P4 endpoints (#138) map the service's rules to status codes: an area over the cap is 400, an exclusive
/// overlap 409, a survey report 409 while Production:Enabled is off, a survey request 409 for a Configured structure
/// (G5), the yield view 404 for a missing structure, and a structure update 200 with its placement warnings (#831).</summary>
public class ProductionYieldControllersTests
{
    [Fact]
    public async Task AreasSurveysAndTheYieldView_AnswerWithTheRightStatus()
    {
        using var y = new ProductionYieldTestDb();
        int logger = await y.LoggerAsync();
        var areas = new ProductionAreasController(y.Areas);
        Assert.IsType<BadRequestObjectResult>(await areas.Create(new ProductionAreaDto
        {
            ProductionStructureId = logger, Name = "Too big", Shape = "Radius", Radius = 99, CenterX = 0, CenterZ = 0,
        }));
        var created = Assert.IsType<CreatedAtRouteResult>(await areas.Create(new ProductionAreaDto
        {
            ProductionStructureId = logger, Name = "Forest", Shape = "Radius", Radius = 10, CenterX = 0, CenterZ = 0,
        }));
        int areaId = Assert.IsType<ProductionAreaDto>(created.Value).Id;
        Assert.IsType<OkObjectResult>(await areas.Update(areaId, new ProductionAreaDto { Radius = 12 }));
        Assert.IsType<NotFoundResult>(await areas.Update(9999, new ProductionAreaDto { Radius = 12 }));
        Assert.IsType<OkObjectResult>(await areas.ReportSurvey(areaId, new AreaSurveyDto
        {
            Units = new() { new AreaSurveyUnitDto { UnitKey = "TREE:OAK:LARGE", Count = 3 } },
        }));
        Assert.IsType<BadRequestObjectResult>(await areas.ReportSurvey(areaId, new AreaSurveyDto
        {
            Units = new() { new AreaSurveyUnitDto { UnitKey = "not a key", Count = 3 } },
        }));

        var (quarry, _, _) = await y.T.QuarryAsync();
        Assert.IsType<CreatedAtRouteResult>(await areas.Create(new ProductionAreaDto
        {
            ProductionStructureId = quarry, Name = "Pit", Shape = "Cuboid", MinX = 0, MaxX = 5, MinY = 0, MaxY = 5, MinZ = 0, MaxZ = 5,
        }));
        Assert.IsType<ConflictObjectResult>(await areas.Create(new ProductionAreaDto
        {
            ProductionStructureId = quarry, Name = "Pit 2", Shape = "Cuboid", MinX = 5, MaxX = 9, MinY = 0, MaxY = 5, MinZ = 0, MaxZ = 5,
        }));

        var structures = new ProductionStructuresController(y.Structures)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        Assert.IsType<NotFoundResult>(await structures.GetYield(9999, y.Yield));
        Assert.IsType<OkObjectResult>(await structures.GetYield(logger, y.Yield));
        Assert.IsType<OkObjectResult>(await structures.RequestSurvey(logger, null, y.Areas));
        Assert.IsType<OkObjectResult>(await structures.Materialize(logger, y.Yield));
        var dto = (await y.Structures.GetByIdAsync(logger))!;
        dto.YieldSource = "Configured";
        var updated = Assert.IsType<OkObjectResult>(await structures.Update(logger, dto));
        Assert.NotNull(Assert.IsType<ProductionStructureDto>(updated.Value).PlacementWarnings);
        Assert.IsType<ConflictObjectResult>(await structures.RequestSurvey(logger, null, y.Areas));
        dto.Level = 42;
        var bad = Assert.IsType<BadRequestObjectResult>(await structures.Update(logger, dto));
        Assert.DoesNotContain("(Parameter", bad.Value!.ToString());
    }

    [Fact]
    public async Task WhileTheFeatureIsOff_TheQueueIsEmpty_AndReportsAre409()
    {
        using var y = new ProductionYieldTestDb(productionEnabled: false);
        int logger = await y.LoggerAsync();
        var area = await y.RadiusAsync(logger);
        var areas = new ProductionAreasController(y.Areas);
        var due = Assert.IsType<OkObjectResult>(await areas.SurveyDue());
        Assert.Empty(Assert.IsType<List<SurveyDueAreaDto>>(due.Value));
        Assert.IsType<ConflictObjectResult>(await areas.ReportSurvey(area.Id, new AreaSurveyDto { Units = new() }));
    }
}

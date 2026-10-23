using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>
/// KNG-273: POST /api/GateDoors/search - the flat, paged door list behind the web app's GateDoor
/// dashboard. Goes through the repository's search with a cleared change tracker, so a missing
/// Include (the gate's name) shows up.
/// </summary>
public class GateDoorSearchTests : IDisposable
{
    private readonly KnKDbContext _ctx =
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<GateDoorMappingProfile>()).CreateMapper();

    public GateDoorSearchTests()
    {
        _ctx.Set<GateStructure>().AddRange(
            new GateStructure { Id = 35, Name = "Garrison", Description = "", WgRegionId = "gate_35" },
            new GateStructure { Id = 36, Name = "Northern Gate", Description = "", WgRegionId = "gate_36" });
        _ctx.GateDoors.AddRange(
            new GateDoor { Id = 17, GateStructureId = 35, Name = "Main gate", GateType = GateType.SLIDING, OpenedState = GateDoorOpenState.JAMMED, IsActive = true, HealthCurrent = 2000, HealthMax = 2000 },
            new GateDoor { Id = 18, GateStructureId = 35, Name = "Postern", GateType = GateType.DOUBLE_DOORS, OpenedState = GateDoorOpenState.CLOSED, IsActive = false },
            new GateDoor { Id = 19, GateStructureId = 36, Name = "Drawbridge", GateType = GateType.DRAWBRIDGE, MotionType = MotionType.ROTATION, OpenedState = GateDoorOpenState.OPEN, IsActive = true });
        _ctx.SaveChanges();
        _ctx.ChangeTracker.Clear();
    }

    public void Dispose() => _ctx.Dispose();

    private static PagedQuery Query(string? searchTerm = null, Dictionary<string, string>? filters = null, string? sortBy = null) =>
        new() { PageNumber = 1, PageSize = 25, SearchTerm = searchTerm, Filters = filters, SortBy = sortBy };

    [Fact]
    public async Task Rows_CarryTheGateName_AndTheListColumns()
    {
        var page = await new GateDoorRepository(_ctx).SearchAsync(Query());
        var rows = _mapper.Map<List<GateDoorListDto>>(page.Items);

        Assert.Equal(3, page.TotalCount);
        var main = rows.Single(r => r.Id == 17);
        Assert.Equal("Main gate", main.Name);
        Assert.Equal(35, main.GateStructureId);
        Assert.Equal("Garrison", main.GateStructureName);
        Assert.Equal(GateType.SLIDING, main.GateType);
        Assert.Equal(GateDoorOpenState.JAMMED, main.OpenedState);
        Assert.True(main.IsActive);
        Assert.Equal(2000, main.HealthCurrent);
        Assert.Equal(2000, main.HealthMax);
    }

    [Fact]
    public async Task DefaultOrder_IsGateNameThenDoorName()
    {
        var page = await new GateDoorRepository(_ctx).SearchAsync(Query());

        Assert.Equal(new[] { 17, 18, 19 }, page.Items.Select(d => d.Id));
    }

    [Theory]
    [InlineData("postern", new[] { 18 })]
    [InlineData("NORTHERN", new[] { 19 })]   // matches the gate's name
    [InlineData("gar", new[] { 17, 18 })]
    public async Task SearchTerm_MatchesDoorOrGateName(string term, int[] expectedIds)
    {
        var page = await new GateDoorRepository(_ctx).SearchAsync(Query(term));

        Assert.Equal(expectedIds, page.Items.Select(d => d.Id));
    }

    [Theory]
    [InlineData("gateStructureId", "36", new[] { 19 })]
    [InlineData("gateType", "double_doors", new[] { 18 })]
    [InlineData("openedState", "JAMMED", new[] { 17 })]
    [InlineData("isActive", "false", new[] { 18 })]
    [InlineData("gateType", "not-a-type", new[] { 17, 18, 19 })]   // an unknown value doesn't filter
    public async Task Filters_NarrowTheList(string key, string value, int[] expectedIds)
    {
        var page = await new GateDoorRepository(_ctx).SearchAsync(Query(filters: new Dictionary<string, string> { [key] = value }));

        Assert.Equal(expectedIds, page.Items.Select(d => d.Id));
    }

    [Fact]
    public async Task SortBy_Name_SortsByDoorName()
    {
        var page = await new GateDoorRepository(_ctx).SearchAsync(Query(sortBy: "name"));

        Assert.Equal(new[] { 19, 17, 18 }, page.Items.Select(d => d.Id));
    }

    [Fact]
    public async Task Paging_TakesThePageAndKeepsTheTotal()
    {
        var page = await new GateDoorRepository(_ctx).SearchAsync(new PagedQuery { PageNumber = 2, PageSize = 2 });

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { 19 }, page.Items.Select(d => d.Id));
    }
}

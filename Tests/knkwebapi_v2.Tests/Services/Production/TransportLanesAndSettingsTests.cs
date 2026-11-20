using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// Lanes (P5 #79, production DESIGN § 7.6): the game server's report upserts by pair, the due list for the lane measurer
/// (L15 #681) and its staleness; the transport settings (G14 speed) and their validation; and the #838 reads (kind name
/// on the structure list, blueprint names on consumed inputs).
/// </summary>
public class TransportLanesAndSettingsTests : IDisposable
{
    private readonly TransportTestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task TheDueList_HasEveryProducerToWarehousePair_AndWarehousePairs_UntilMeasured_ThenStaleAfterTheMaxAge()
    {
        await _t.SetUpAsync(lanes: false);
        var due = await _t.Lanes.DueAsync(100);
        Assert.Equal(new[] { (_t.Quarry, _t.North), (_t.Quarry, _t.South), (_t.North, _t.South) },
            due.Select(d => (d.FromStructureId, d.ToStructureId)).ToArray());
        Assert.All(due, d => Assert.Equal("Missing", d.Reason));

        var result = await _t.Lanes.ReportAsync(new TransportLaneReportDto
        {
            Lanes = new()
            {
                new() { FromStructureId = _t.Quarry, ToStructureId = _t.North, RoadDistance = 432m, StraightDistance = 300m, RouteEdgeIds = new() { 5, 6, 7 } },
                new() { FromStructureId = _t.South, ToStructureId = _t.Quarry, Reachable = false, StraightDistance = 500m },
            },
        });
        Assert.Equal((2, 0), (result.Created, result.Updated));
        due = await _t.Lanes.DueAsync(100);
        Assert.Equal((_t.North, _t.South), (due.Single().FromStructureId, due.Single().ToStructureId)); // either direction counts

        var lane = _t.E.Db.TransportLanes.AsNoTracking().Single(l => l.FromStructureId == _t.Quarry);
        Assert.Equal((TransportLaneSource.Plugin, "[5,6,7]"), (lane.Source, lane.RouteEdgeIdsJson));
        Assert.Null(_t.E.Db.TransportLanes.AsNoTracking().Single(l => l.FromStructureId == _t.South).RoadDistance);

        _t.Advance(TimeSpan.FromHours(25));
        due = await _t.Lanes.DueAsync(100);
        Assert.Equal(new[] { "Stale", "Stale", "Missing" }, due.Select(d => d.Reason).ToArray());
        result = await _t.Lanes.ReportAsync(new TransportLaneReportDto
        {
            Lanes = new() { new() { FromStructureId = _t.Quarry, ToStructureId = _t.North, RoadDistance = 440m } },
        });
        Assert.Equal((0, 1), (result.Created, result.Updated));
        Assert.Equal(440m, _t.E.Db.TransportLanes.AsNoTracking().Single(l => l.FromStructureId == _t.Quarry).RoadDistance);
        Assert.Single(await _t.Lanes.DueAsync(1));
    }

    [Fact]
    public async Task LaneValidation()
    {
        await _t.SetUpAsync(lanes: false);
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Lanes.CreateAsync(new TransportLaneDto { FromStructureId = _t.Quarry, ToStructureId = _t.North }));
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Lanes.CreateAsync(new TransportLaneDto { FromStructureId = _t.Quarry, ToStructureId = 9999, RoadDistance = 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Lanes.CreateAsync(new TransportLaneDto { FromStructureId = _t.Quarry, ToStructureId = _t.North, RoadDistance = -1 }));
        var lane = await _t.Lane(_t.Quarry, _t.North, 10m);
        Assert.Equal("Manual", lane.Source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Lane(_t.Quarry, _t.North, 12m));
        var page = await _t.Lanes.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 10, Filters = new() { ["structureId"] = _t.North.ToString() } });
        Assert.Equal(("Quarry", "North"), (page.Items.Single().FromStructureName, page.Items.Single().ToStructureName));
        await _t.Lanes.DeleteAsync(lane.Id!.Value);
        Assert.Empty(_t.E.Db.TransportLanes);
    }

    [Fact]
    public void TransportSettings_AreValidated_AndASaveWithoutTheSectionKeepsTheStoredOne()
    {
        var stored = new ProductionSettingsDto { Transport = new ProductionTransportSettingsDto { WalkingSpeedBlocksPerSecond = 2m, MaxReroutes = 0 } };
        var saved = ProductionSettings.Normalize(new ProductionSettingsDto { Enabled = true, Transport = null }, stored);
        Assert.Equal((2m, 0), (saved.Transport!.WalkingSpeedBlocksPerSecond, saved.Transport.MaxReroutes));
        Assert.Equal(4.317m, ProductionSettings.Normalize(new ProductionSettingsDto()).Transport!.WalkingSpeedBlocksPerSecond);
        Assert.Equal(4.317m, ProductionSettings.Read(null).Transport!.WalkingSpeedBlocksPerSecond);
        foreach (var bad in new Action<ProductionTransportSettingsDto>[]
        {
            t => t.WalkingSpeedBlocksPerSecond = 0, t => t.DefaultCarrierCapacity = 0, t => t.DefaultThresholdPercent = 101,
            t => t.UnmeasuredRouteFactor = 0.5m, t => t.AwaitingSpaceMinutes = 0, t => t.MaxReroutes = 11, t => t.LaneMaxAgeHours = 0,
        })
        {
            var dto = new ProductionTransportSettingsDto();
            bad(dto);
            Assert.Throws<ArgumentException>(() => ProductionSettings.Normalize(new ProductionSettingsDto { Transport = dto }));
        }
    }

    [Fact]
    public async Task Issue838_TheStructureListHasTheKindName_AndConsumedInputsHaveTheBlueprintName()
    {
        await _t.SetUpAsync(producing: true);
        var list = await _t.E.Structures.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 10 });
        Assert.Equal("Quarry", list.Items.Single(i => i.id == _t.Quarry).productionKindName);

        var record = new ProductionCycleRecord
        {
            ProductionLineId = _t.QuarryLine, ProductionStructureId = _t.Quarry, ItemBlueprintId = Cobblestone, ModifiersJson = "[]",
            InputsJson = "[{\"itemBlueprintId\":501,\"consumed\":3}]", SettledAt = DateTime.UtcNow, PeriodStart = DateTime.UtcNow, PeriodEnd = DateTime.UtcNow,
        };
        _t.E.Db.ProductionCycleRecords.Add(record);
        _t.E.Db.SaveChanges();
        _t.E.Db.ChangeTracker.Clear();
        var service = new ProductionCycleRecordService(_t.E.Db);
        var input = (await service.GetByIdAsync(record.Id))!.Inputs.Single();
        Assert.Equal(("Coal", 3L), (input.ItemBlueprintName, input.Consumed));
        var page = await service.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 10, Filters = new() { ["itemBlueprintId"] = Cobblestone.ToString() } });
        Assert.Equal("Coal", page.Items.Single().Inputs.Single().ItemBlueprintName);
        // The name is read, never stored in the record.
        Assert.DoesNotContain("itemBlueprintName", System.Text.Json.JsonSerializer.Serialize(new List<ConsumedInputDto> { new() { ItemBlueprintId = 1 } }));
    }
}

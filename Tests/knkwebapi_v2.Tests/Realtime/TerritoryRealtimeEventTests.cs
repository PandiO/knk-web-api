using knkwebapi_v2.Models;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using Xunit;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>
/// PandiO/knk-workspace#326: <c>TerritoryChanged</c> tells the game server to rebuild its territory chunk cache when an
/// input changed, and stays silent for the rows its own report writes, so a rebuild never triggers itself.
/// </summary>
public class TerritoryRealtimeEventTests
{
    private static RealtimeEntityChange Change(Type type, RealtimeEntityState state, params string[] modified) =>
        new(type, state, modified.ToHashSet(StringComparer.Ordinal),
            new Dictionary<string, object?> { ["Id"] = 7, ["WgRegionId"] = "domain_7" });

    private static bool Territory(params RealtimeEntityChange[] changes) =>
        RealtimeEntityChangeMap.Map(changes).Any(e => e.Type == ConfigEventTypes.TerritoryChanged);

    [Theory]
    [InlineData(nameof(Town.Power))]
    [InlineData(nameof(Town.KingdomId))]
    [InlineData(nameof(Town.ProvinceId))]
    public void TownTerritoryField_SendsIt(string property) =>
        Assert.True(Territory(Change(typeof(Town), RealtimeEntityState.Modified, property)));

    [Fact]
    public void TownDescription_DoesNot() =>
        Assert.False(Territory(Change(typeof(Town), RealtimeEntityState.Modified, nameof(Town.Description))));

    [Theory]
    [InlineData(nameof(District.IsCapital))]
    [InlineData(nameof(District.TownId))]
    [InlineData(nameof(Domain.WgRegionId))]
    public void DistrictInput_SendsIt(string property) =>
        Assert.True(Territory(Change(typeof(District), RealtimeEntityState.Modified, property)));

    [Fact]
    public void AddedOrDeletedDistrict_CrownLandKingdomProvinceAndSubRegion_SendIt()
    {
        Assert.True(Territory(Change(typeof(District), RealtimeEntityState.Added)));
        Assert.True(Territory(Change(typeof(District), RealtimeEntityState.Deleted)));
        Assert.True(Territory(Change(typeof(CrownLand), RealtimeEntityState.Modified, nameof(CrownLand.Range))));
        Assert.True(Territory(Change(typeof(Kingdom), RealtimeEntityState.Added)));
        Assert.True(Territory(Change(typeof(Province), RealtimeEntityState.Deleted)));
        Assert.True(Territory(Change(typeof(DomainSubRegion), RealtimeEntityState.Added)));
    }

    [Fact]
    public void ConflictResolution_SendsIt_ButTheReportsOwnWritesDoNot()
    {
        Assert.True(Territory(Change(typeof(TerritoryConflict), RealtimeEntityState.Modified, nameof(TerritoryConflict.Status),
            nameof(TerritoryConflict.Resolution), nameof(TerritoryConflict.AwardedHolderId))));
        Assert.True(Territory(Change(typeof(TerritoryConflict), RealtimeEntityState.Deleted)));
        Assert.False(Territory(Change(typeof(TerritoryConflict), RealtimeEntityState.Added)));
        Assert.False(Territory(Change(typeof(TerritoryConflict), RealtimeEntityState.Modified,
            nameof(TerritoryConflict.ChunksJson), nameof(TerritoryConflict.LastReportedAt), nameof(TerritoryConflict.StillOverlapping))));
    }

    [Fact]
    public void ProductionSettings_SendIt_OtherGameSettingsDoNot()
    {
        Assert.True(Territory(Change(typeof(GameSettings), RealtimeEntityState.Modified, nameof(GameSettings.ProductionSettingsJson))));
        Assert.False(Territory(Change(typeof(GameSettings), RealtimeEntityState.Modified, nameof(GameSettings.Motd))));
    }

    [Fact]
    public void AStructuresTerritoryFlag_SendsNoEntityChanged_ButOtherEditsStillDo()
    {
        var flagOnly = RealtimeEntityChangeMap.Map(new[] { Change(typeof(Structure), RealtimeEntityState.Modified,
            nameof(Structure.TerritoryFlag), nameof(Structure.TerritoryFlaggedAt), nameof(Structure.TerritoryConflictId)) });
        Assert.DoesNotContain(flagOnly, e => e.Type == ConfigEventTypes.EntityChanged);

        var rename = RealtimeEntityChangeMap.Map(new[] { Change(typeof(Structure), RealtimeEntityState.Modified, nameof(Domain.Name)) });
        Assert.Contains(rename, e => e.Type == ConfigEventTypes.EntityChanged);
        Assert.Contains(RealtimeEntityChangeMap.Map(new[] { Change(typeof(Structure), RealtimeEntityState.Modified, nameof(Structure.LoyaltyTownId)) }),
            e => e.Type == ConfigEventTypes.EntityChanged);
    }
}

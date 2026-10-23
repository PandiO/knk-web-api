using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-234: the navigation compass item rule (material + display name) and where the compass works. A single
/// row, created by the first save; defaults while none exists.
/// </summary>
public class NavigationCompassSettingsTests
{
    private readonly string _dbName = $"nav-compass-{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    [Fact]
    public async Task WithoutARowTheDefaultsAreAnswered()
    {
        await using var db = NewContext();

        var settings = await new NavigationCompassSettingsService(db).GetAsync();

        Assert.True(settings.Enabled);
        Assert.Equal("COMPASS", settings.Material);
        Assert.Equal("Navigation Compass", settings.DisplayName);
        Assert.Equal("Exact", settings.NameMatch);
        Assert.True(settings.SiegeEnabled);
        Assert.False(settings.GeneralEnabled);
        Assert.Equal("", settings.GeneralTargets);
        Assert.Null(settings.UpdatedAt);
        Assert.Empty(db.NavigationCompassSettingsRows);
    }

    [Fact]
    public async Task TheFirstSaveCreatesTheSingleRowAndLaterSavesUpdateIt()
    {
        await using (var db = NewContext())
        {
            var saved = await new NavigationCompassSettingsService(db).UpdateAsync(
                new UpdateNavigationCompassSettingsDto { Material = "minecraft:recovery_compass", DisplayName = "Siege Compass" });
            Assert.Equal("minecraft:recovery_compass", saved.Material);
            Assert.NotNull(saved.UpdatedAt);
        }
        await using (var db = NewContext())
        {
            await new NavigationCompassSettingsService(db).UpdateAsync(new UpdateNavigationCompassSettingsDto { NameMatch = "contains" });
        }
        await using (var db = NewContext())
        {
            var row = Assert.Single(db.NavigationCompassSettingsRows);
            Assert.Equal(NavigationCompassSettings.SingletonId, row.Id);
            Assert.Equal("minecraft:recovery_compass", row.Material);
            Assert.Equal("Siege Compass", row.DisplayName);
            Assert.Equal(NavigationCompassNameMatch.Contains, row.NameMatch);
        }
    }

    [Fact]
    public async Task NullFieldsLeaveTheSavedValues()
    {
        await using var db = NewContext();
        var service = new NavigationCompassSettingsService(db);
        await service.UpdateAsync(new UpdateNavigationCompassSettingsDto { Enabled = false, SiegeEnabled = false, GeneralEnabled = true });

        var after = await service.UpdateAsync(new UpdateNavigationCompassSettingsDto { DisplayName = "Pathfinder" });

        Assert.False(after.Enabled);
        Assert.False(after.SiegeEnabled);
        Assert.True(after.GeneralEnabled);
        Assert.Equal("Pathfinder", after.DisplayName);
    }

    [Fact]
    public async Task AnEmptyDisplayNameIsAllowedAndMeansAnyName()
    {
        await using var db = NewContext();

        var saved = await new NavigationCompassSettingsService(db).UpdateAsync(new UpdateNavigationCompassSettingsDto { DisplayName = "  " });

        Assert.Equal("", saved.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not a material")]
    [InlineData("compass;drop")]
    public async Task ABadMaterialIsRefused(string material)
    {
        await using var db = NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new NavigationCompassSettingsService(db).UpdateAsync(new UpdateNavigationCompassSettingsDto { Material = material }));
        Assert.Empty(db.NavigationCompassSettingsRows);
    }

    [Fact]
    public async Task ABadNameMatchIsRefused()
    {
        await using var db = NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new NavigationCompassSettingsService(db).UpdateAsync(new UpdateNavigationCompassSettingsDto { NameMatch = "StartsWith" }));
    }

    [Fact]
    public async Task ATooLongDisplayNameIsRefused()
    {
        await using var db = NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new NavigationCompassSettingsService(db).UpdateAsync(
                new UpdateNavigationCompassSettingsDto { DisplayName = new string('x', NavigationCompassSettings.MaxDisplayNameLength + 1) }));
    }

    [Fact]
    public async Task TargetsAreStoredOnePerLineWithoutBlanks()
    {
        await using var db = NewContext();

        var saved = await new NavigationCompassSettingsService(db).UpdateAsync(
            new UpdateNavigationCompassSettingsDto { GeneralTargets = " town:Kardenna \r\n\r\n  Market Square spawn\n" });

        Assert.Equal("town:Kardenna\nMarket Square spawn", saved.GeneralTargets);
    }

    [Fact]
    public async Task TooManyOrTooLongTargetsAreRefused()
    {
        await using var db = NewContext();
        var service = new NavigationCompassSettingsService(db);

        var many = string.Join("\n", Enumerable.Range(0, NavigationCompassSettings.MaxTargets + 1).Select(i => $"town:T{i}"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(new UpdateNavigationCompassSettingsDto { GeneralTargets = many }));

        var longOne = new string('x', NavigationCompassSettings.MaxTargetLength + 1);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(new UpdateNavigationCompassSettingsDto { GeneralTargets = longOne }));
    }

    [Fact]
    public async Task ClearingTheTargetsIsAllowed()
    {
        await using var db = NewContext();
        var service = new NavigationCompassSettingsService(db);
        await service.UpdateAsync(new UpdateNavigationCompassSettingsDto { GeneralTargets = "town:Kardenna" });

        var cleared = await service.UpdateAsync(new UpdateNavigationCompassSettingsDto { GeneralTargets = "" });

        Assert.Equal("", cleared.GeneralTargets);
    }

    [Theory]
    [InlineData("Exact", NavigationCompassNameMatch.Exact)]
    [InlineData("contains", NavigationCompassNameMatch.Contains)]
    [InlineData(" CONTAINS ", NavigationCompassNameMatch.Contains)]
    public void NameMatchParsesAnyCase(string value, NavigationCompassNameMatch expected)
    {
        Assert.Equal(expected, NavigationCompassSettingsService.ParseNameMatch(value));
    }

    [Fact]
    public void NameMatchRejectsUnknownWords()
    {
        Assert.Null(NavigationCompassSettingsService.ParseNameMatch("Equals"));
        Assert.Null(NavigationCompassSettingsService.ParseNameMatch(null));
    }
}

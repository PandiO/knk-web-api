using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>Visibility settings (DESIGN.md §F.4, D8, L1-3/L1-4; link 2 acceptance criterion 6).</summary>
public class StatisticsVisibilityServiceTests : IDisposable
{
    private readonly StatisticsTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static StatisticsVisibilityChangeDto Change(string setting, StatisticVisibility expected, StatisticVisibility to, string context = "") =>
        new() { SettingKey = setting, Context = context, Expected = expected, Visibility = to };

    private static StatisticsVisibilityUpdateDto Update(params StatisticsVisibilityChangeDto[] changes) => new() { Changes = changes.ToList() };

    private static PlayerStatVisibility Row(string setting, StatisticVisibility visibility, string context = "") =>
        new() { UserId = 1, SettingKey = setting, ContextKey = context, Visibility = visibility };

    [Fact]
    public async Task Defaults_AreNobody_ForEverySetting()
    {
        var dto = await _db.Visibility().GetAsync(1);

        Assert.NotNull(dto);
        Assert.False(dto!.FriendsAvailable);
        Assert.Equal(StatisticsCatalog.SettingKeys.OrderBy(k => k), dto.Settings.Select(s => s.SettingKey).OrderBy(k => k));
        Assert.All(dto.Settings, s => Assert.Equal(StatisticVisibility.Nobody, s.Visibility));
        Assert.Null(await _db.Visibility().GetAsync(99));
    }

    [Fact]
    public async Task Update_AppliesAllChangesAtomically()
    {
        var dto = await _db.Visibility().UpdateAsync(1, Update(
            Change("pvp_kills", StatisticVisibility.Nobody, StatisticVisibility.Everyone),
            Change("pvp_kills", StatisticVisibility.Nobody, StatisticVisibility.Nobody, "siege"),
            Change("economy", StatisticVisibility.Nobody, StatisticVisibility.Friends)));

        var pvp = dto!.Settings.Single(s => s.SettingKey == "pvp_kills");
        Assert.Equal(StatisticVisibility.Everyone, pvp.Visibility);
        Assert.Equal(new[] { ("siege", StatisticVisibility.Nobody, true) }, pvp.Contexts.Select(c => (c.Context, c.Visibility, c.IsOverride)));
        Assert.Equal(StatisticVisibility.Friends, dto.Settings.Single(s => s.SettingKey == "economy").Visibility);
        Assert.Equal(3, _db.Context.PlayerStatVisibilities.Count());
    }

    [Fact]
    public async Task AnyExpectedMismatch_RejectsTheWholeUpdate_WithTheCurrentSettings()
    {
        _db.Context.PlayerStatVisibilities.Add(Row("deaths", StatisticVisibility.Everyone));
        _db.Context.SaveChanges();

        var conflict = await Assert.ThrowsAsync<StatisticsVisibilityConflictException>(() => _db.Visibility().UpdateAsync(1, Update(
            Change("pvp_kills", StatisticVisibility.Nobody, StatisticVisibility.Everyone),
            Change("deaths", StatisticVisibility.Nobody, StatisticVisibility.Friends)))); // stale: it's Everyone

        Assert.Equal(StatisticVisibility.Everyone, conflict.Current.Settings.Single(s => s.SettingKey == "deaths").Visibility);
        var rows = _db.Context.PlayerStatVisibilities.AsNoTracking().ToList();
        Assert.Equal(new[] { ("deaths", StatisticVisibility.Everyone) }, rows.Select(r => (r.SettingKey, r.Visibility)));
    }

    [Fact]
    public async Task ContextExpected_IsTheInheritedValue_WhenThereIsNoOverride()
    {
        _db.Context.PlayerStatVisibilities.Add(Row("wins", StatisticVisibility.Everyone));
        _db.Context.SaveChanges();

        await _db.Visibility().UpdateAsync(1, Update(Change("wins", StatisticVisibility.Everyone, StatisticVisibility.Nobody, "siege")));

        Assert.Equal(StatisticVisibility.Nobody,
            _db.Context.PlayerStatVisibilities.AsNoTracking().Single(r => r.ContextKey == "siege").Visibility);
    }

    [Theory]
    [InlineData("nope", "", "UnknownSetting")]
    [InlineData("deaths", "siege", "NotContextual")]
    [InlineData("economy", "siege", "NotContextual")]
    [InlineData("pvp_kills", "Siege!", "InvalidContext")]
    public async Task InvalidChanges_Are400s(string setting, string context, string code)
    {
        var ex = await Assert.ThrowsAsync<StatisticsValidationException>(() =>
            _db.Visibility().UpdateAsync(1, Update(Change(setting, StatisticVisibility.Nobody, StatisticVisibility.Everyone, context))));

        Assert.Equal(code, ex.Code);
        Assert.Empty(_db.Context.PlayerStatVisibilities.AsNoTracking());
    }

    [Fact]
    public async Task DuplicateAndTooManyChanges_Are400s()
    {
        var duplicate = await Assert.ThrowsAsync<StatisticsValidationException>(() => _db.Visibility().UpdateAsync(1, Update(
            Change("logins", StatisticVisibility.Nobody, StatisticVisibility.Everyone),
            Change("logins", StatisticVisibility.Nobody, StatisticVisibility.Friends))));
        Assert.Equal("DuplicateChange", duplicate.Code);

        var many = Enumerable.Range(0, 65).Select(i => Change("pvp_kills", StatisticVisibility.Nobody, StatisticVisibility.Everyone, "ctx" + i)).ToArray();
        var tooMany = await Assert.ThrowsAsync<StatisticsValidationException>(() => _db.Visibility().UpdateAsync(1, Update(many)));
        Assert.Equal("TooManyChanges", tooMany.Code);
    }

    [Fact]
    public async Task ContextsWithData_AreListed_WithTheInheritedValue()
    {
        _db.Context.PlayerStatTotals.AddRange(
            new PlayerStatTotal { UserId = 1, MetricKey = "damage_dealt.mob", ContextKey = "open_world", Value = 3 },
            new PlayerStatTotal { UserId = 1, MetricKey = "damage_dealt.player", ContextKey = "siege", Value = 3 });
        _db.Context.PlayerStatVisibilities.Add(Row("damage_dealt", StatisticVisibility.Everyone));
        _db.Context.SaveChanges();

        var setting = (await _db.Visibility().GetAsync(1))!.Settings.Single(s => s.SettingKey == "damage_dealt");

        Assert.Equal(new[] { ("open_world", StatisticVisibility.Everyone, false), ("siege", StatisticVisibility.Everyone, false) },
            setting.Contexts.Select(c => (c.Context, c.Visibility, c.IsOverride)));
    }

    // ---- the pure rules

    [Fact]
    public void Rules_FriendsFailsClosed()
    {
        var rows = new[] { Row("logins", StatisticVisibility.Friends) };
        Assert.False(StatisticsVisibilityRules.IsPublic(rows, "logins"));
    }

    [Fact]
    public void Rules_ContextOverrideBeatsMetricLevel_BothWays()
    {
        var hiddenSiege = new[] { Row("pvp_kills", StatisticVisibility.Everyone), Row("pvp_kills", StatisticVisibility.Nobody, "siege") };
        Assert.True(StatisticsVisibilityRules.IsPublic(hiddenSiege, "pvp_kills", "open_world"));
        Assert.False(StatisticsVisibilityRules.IsPublic(hiddenSiege, "pvp_kills", "siege"));

        var shownSiege = new[] { Row("pvp_kills", StatisticVisibility.Nobody), Row("pvp_kills", StatisticVisibility.Everyone, "siege") };
        Assert.True(StatisticsVisibilityRules.IsPublic(shownSiege, "pvp_kills", "siege"));
        Assert.False(StatisticsVisibilityRules.IsPublic(shownSiege, "pvp_kills", "open_world"));
    }

    [Fact]
    public void Rules_TotalNeedsTheMetricLevelAndEveryOverridePublic()
    {
        Assert.True(StatisticsVisibilityRules.IsPublic(new[] { Row("wins", StatisticVisibility.Everyone) }, "wins"));
        Assert.False(StatisticsVisibilityRules.IsPublic(
            new[] { Row("wins", StatisticVisibility.Everyone), Row("wins", StatisticVisibility.Friends, "siege") }, "wins"));
        Assert.False(StatisticsVisibilityRules.IsPublic(
            new[] { Row("wins", StatisticVisibility.Nobody), Row("wins", StatisticVisibility.Everyone, "siege") }, "wins"));
        Assert.False(StatisticsVisibilityRules.IsPublic(Array.Empty<PlayerStatVisibility>(), "wins"));
    }

    [Fact]
    public void Rules_NonContextualSettings_IgnoreTheContext()
    {
        var rows = new[] { Row("deaths", StatisticVisibility.Everyone) };
        Assert.True(StatisticsVisibilityRules.IsPublic(rows, "deaths", "siege"));
        Assert.True(StatisticsVisibilityRules.IsPublic(rows, "deaths"));
    }
}

using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>
/// Reads × viewer kinds (DESIGN.md §F.3-§F.4; link 2 acceptance criterion 7): anonymous sees only
/// the always-public fields, signed-in viewers the Everyone settings, self and staff everything but
/// internal metrics; deaths per context for staff only; merged identities aggregated; first join.
/// </summary>
public class StatisticsQueryServiceTests : IDisposable
{
    private static readonly StatisticsViewer Anonymous = StatisticsViewer.Anonymous;
    private static readonly StatisticsViewer Bob = new(StatisticsViewerKind.SignedIn, 2);
    private static readonly StatisticsViewer Self = new(StatisticsViewerKind.Self, 1);
    private static readonly StatisticsViewer Staff = new(StatisticsViewerKind.Staff, 2);

    private readonly StatisticsTestDb _db = new();

    public StatisticsQueryServiceTests()
    {
        var ctx = _db.Context;
        // carol (3) was merged into alice (1): a MERGE_FORFEIT of carol's balances, sourced from alice.
        var carol = ctx.Users.Single(u => u.Id == 3);
        carol.IsActive = false;
        carol.AccountCreatedVia = AccountCreationMethod.MinecraftServer;
        carol.CreatedAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var forfeit = CurrencyLedgerSeed.Tx(CurrencyTransactionKind.Merge, CurrencyReasons.MergeForfeit, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            (3, Currency.Coins, -10));
        forfeit.SourceType = "User";
        forfeit.SourceRef = "1";
        ctx.CurrencyTransactions.Add(forfeit);
        var alice = ctx.Users.Single(u => u.Id == 1);
        alice.ExperiencePoints = 150;
        alice.Coins = 42;
        ctx.TitleBrackets.AddRange(
            new TitleBracket { Id = 10, MaleName = "Serf", FemaleName = "Serf", MinExperience = 0 },
            new TitleBracket { Id = 11, MaleName = "Peasant", FemaleName = "Peasant woman", MinExperience = 100 });

        void Total(int user, string metric, string context, decimal value) =>
            ctx.PlayerStatTotals.Add(new PlayerStatTotal { UserId = user, MetricKey = metric, ContextKey = context, Value = value });
        Total(1, "active_playtime", "", 3600);
        Total(1, "afk_time", "", 600);
        Total(1, "logins", "", 5);
        Total(1, "pvp_kills", "open_world", 4);
        Total(1, "pvp_kills", "siege", 6);
        Total(1, "deaths", "open_world", 2);
        Total(1, "deaths", "siege", 3);
        Total(1, "deaths_by_cause.player", "open_world", 2);
        Total(1, "highest_killstreak", "open_world", 3);
        Total(1, "highest_killstreak", "siege", 5);
        Total(1, "coins_earned", "", 100);
        Total(1, "xp_gained", "", 50);
        Total(1, "distance.foot", "", 120.7m);
        Total(3, "pvp_kills", "open_world", 1);
        Total(3, "active_playtime", "", 100);
        Total(3, "highest_killstreak", "open_world", 7);

        void Day(int user, int day, string metric, string context, decimal value) =>
            ctx.PlayerStatDailies.Add(new PlayerStatDaily { UserId = user, Day = new DateOnly(2026, 10, day), MetricKey = metric, ContextKey = context, Value = value });
        Day(1, 1, "pvp_kills", "open_world", 1);
        Day(1, 3, "pvp_kills", "open_world", 2);
        Day(3, 3, "pvp_kills", "open_world", 1);
        Day(1, 3, "pvp_kills", "siege", 6);
        Day(1, 2, "highest_killstreak", "open_world", 3);
        Day(1, 3, "highest_killstreak", "open_world", 2);

        void Visible(string setting, StatisticVisibility visibility, string context = "") =>
            ctx.PlayerStatVisibilities.Add(new PlayerStatVisibility { UserId = 1, SettingKey = setting, ContextKey = context, Visibility = visibility });
        Visible("pvp_kills", StatisticVisibility.Everyone);
        Visible("pvp_kills", StatisticVisibility.Nobody, "siege");
        Visible("deaths", StatisticVisibility.Everyone);
        Visible("economy", StatisticVisibility.Everyone);
        Visible("highest_killstreak", StatisticVisibility.Friends);

        ctx.Streets.Add(new Street { Id = 1, Name = "Main" });
        ctx.Towns.Add(new Town { Id = 1, Name = "Rivia", Description = "", WgRegionId = "town_rivia" });
        ctx.Districts.Add(new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "district_oq", TownId = 1 });
        ctx.Structures.Add(new Structure { Id = 3, Name = "Smithy", Description = "", WgRegionId = "structure_smithy", StreetId = 1, DistrictId = 2 });
        ctx.UserDomainDiscoveries.AddRange(
            new UserDomainDiscovery { UserId = 1, DomainId = 1, DiscoveredAt = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc) },
            new UserDomainDiscovery { UserId = 3, DomainId = 1, DiscoveredAt = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc) },
            new UserDomainDiscovery { UserId = 3, DomainId = 2, DiscoveredAt = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc) });
        ctx.PlayerStatProfiles.Add(new PlayerStatProfile { UserId = 1, FirstSessionAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc) });
        ctx.PlayerTitleChanges.Add(new PlayerTitleChange
        {
            UserId = 1, FromTitleName = "Serf", ToTitleName = "Peasant", ToTitleBracketId = 11, Direction = TitleChangeDirection.Promotion,
            CurrencyEntryId = 1000, ChangedAt = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc)
        });
        ctx.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private async Task<Dictionary<string, PlayerStatisticMetricDtoView>> Metrics(StatisticsViewer viewer, string? period = null, DateOnly? date = null)
    {
        var dto = await _db.Query().GetAsync(1, viewer, period, date);
        Assert.NotNull(dto);
        return dto!.Metrics.ToDictionary(m => m.Key, m => new PlayerStatisticMetricDtoView(m.Value, m.Contexts?.ToDictionary(c => c.Context, c => c.Value)));
    }

    private sealed record PlayerStatisticMetricDtoView(decimal? Value, Dictionary<string, decimal>? Contexts);

    [Fact]
    public async Task Anonymous_SeesOnlyTheAlwaysPublicFields()
    {
        var dto = await _db.Query().GetAsync(1, Anonymous, null, null);

        Assert.Equal("anonymous", dto!.Viewer);
        Assert.Equal(new[] { "active_playtime", "afk_time", "xp_gained" }, dto.Metrics.Select(m => m.Key).OrderBy(k => k));
        Assert.Null(dto.Economy);
        Assert.Null(dto.Discoveries);
        Assert.Equal(("Peasant", 150, 42), (dto.Profile.TitleName, dto.Profile.Experience, dto.Profile.Coins));
    }

    [Fact]
    public async Task SignedIn_SeesEveryoneSettings_WithTheContextAndTotalRules()
    {
        var metrics = await Metrics(Bob);

        Assert.Equal(new[] { "active_playtime", "afk_time", "deaths", "pvp_kills", "xp_gained" }, metrics.Keys.OrderBy(k => k));
        // Siege is overridden to Nobody: the total is hidden, open world is shown (merged carol's kill included).
        Assert.Null(metrics["pvp_kills"].Value);
        Assert.Equal(new Dictionary<string, decimal> { ["open_world"] = 5 }, metrics["pvp_kills"].Contexts);
        // Deaths: total only.
        Assert.Equal(5m, metrics["deaths"].Value);
        Assert.Null(metrics["deaths"].Contexts);

        var dto = await _db.Query().GetAsync(1, Bob, null, null);
        Assert.Equal(100, dto!.Economy!.CoinsEarned);
        Assert.Null(dto.Discoveries); // discoveries.counts is Nobody
    }

    [Fact]
    public async Task Self_SeesEverythingButInternalMetrics_DeathsAsATotal()
    {
        var metrics = await Metrics(Self);

        Assert.Contains("logins", metrics.Keys);
        Assert.Contains("highest_killstreak", metrics.Keys); // Friends hides it from others, not from the player
        Assert.DoesNotContain(metrics.Keys, k => k.StartsWith("deaths_by_cause") || k == "distance.swim");
        Assert.Equal(11m, metrics["pvp_kills"].Value); // 4 + 6 + carol's 1
        Assert.Equal(new Dictionary<string, decimal> { ["open_world"] = 5, ["siege"] = 6 }, metrics["pvp_kills"].Contexts);
        Assert.Null(metrics["deaths"].Contexts);
        Assert.Equal(7m, metrics["highest_killstreak"].Value); // max across contexts and merged identities
        Assert.Equal(120m, metrics["distance.foot"].Value);    // floored for display
    }

    [Fact]
    public async Task Staff_SeesDeathsPerContext()
    {
        var metrics = await Metrics(Staff);

        Assert.Equal(new Dictionary<string, decimal> { ["open_world"] = 2, ["siege"] = 3 }, metrics["deaths"].Contexts);
        Assert.Equal("staff", (await _db.Query().GetAsync(1, Staff, null, null))!.Viewer);
    }

    [Fact]
    public async Task Profile_AggregatesMergedIdentities_AndDerivesFirstJoin()
    {
        var dto = await _db.Query().GetAsync(1, Anonymous, null, null);

        Assert.Equal(3700, dto!.Profile.ActivePlaytimeSeconds);
        Assert.Equal(600, dto.Profile.AfkSeconds);
        // carol's Minecraft-created account (Aug 1) is older than alice's (Sep 1) and her first session (Oct 1).
        Assert.Equal(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc), dto.Profile.FirstJoinedAt);
    }

    [Fact]
    public async Task FirstJoin_IsNullForAWebAccountThatNeverJoined()
    {
        var dto = await _db.Query().GetAsync(2, Anonymous, null, null);

        Assert.Null(dto!.Profile.FirstJoinedAt);
    }

    [Fact]
    public async Task Periods_ReadDailyRows()
    {
        var day = await Metrics(Self, "day", new DateOnly(2026, 10, 3));
        Assert.Equal(new Dictionary<string, decimal> { ["open_world"] = 3, ["siege"] = 6 }, day["pvp_kills"].Contexts);
        Assert.Equal(2m, day["highest_killstreak"].Value);

        var week = await Metrics(Self, "week", new DateOnly(2026, 10, 3)); // Mon Sep 28 – Sun Oct 4
        Assert.Equal(10m, week["pvp_kills"].Value);
        Assert.Equal(3m, week["highest_killstreak"].Value); // max over the week's days

        var dto = await _db.Query().GetAsync(1, Self, "month", new DateOnly(2026, 10, 15));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)), (dto!.PeriodStart!.Value, dto.PeriodEndExclusive!.Value));
        Assert.Equal(0, dto.Discoveries!.Total); // each domain counts at its earliest discovery (carol's, in September)
    }

    [Fact]
    public async Task Discoveries_CountEachDomainOnceAcrossMergedIdentities()
    {
        var lifetime = await _db.Query().GetAsync(1, Self, null, null);

        Assert.Equal((2, 1, 1, 0), (lifetime!.Discoveries!.Total, lifetime.Discoveries.Towns, lifetime.Discoveries.Districts, lifetime.Discoveries.Structures));
    }

    [Fact]
    public async Task UnknownOrInactiveUsers_AreNull_AndBadPeriodsThrow()
    {
        Assert.Null(await _db.Query().GetAsync(99, Self, null, null));
        Assert.Null(await _db.Query().GetAsync(3, Staff, null, null)); // merged away
        var ex = await Assert.ThrowsAsync<StatisticsValidationException>(() => _db.Query().GetAsync(1, Self, "year", null));
        Assert.Equal("InvalidPeriod", ex.Code);
    }

    [Fact]
    public async Task Series_HonoursVisibility_AndZeroFills()
    {
        var series = await _db.Query().GetSeriesAsync(1, Bob, "pvp_kills", "open_world", "day", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3));

        Assert.Equal(new[] { 1m, 0m, 3m }, series!.Points.Select(p => p.Value));
        await Assert.ThrowsAsync<StatisticsHiddenException>(() =>
            _db.Query().GetSeriesAsync(1, Bob, "pvp_kills", "siege", "day", null, null));
        await Assert.ThrowsAsync<StatisticsHiddenException>(() =>
            _db.Query().GetSeriesAsync(1, Bob, "pvp_kills", null, "day", null, null)); // total hidden
        await Assert.ThrowsAsync<StatisticsHiddenException>(() =>
            _db.Query().GetSeriesAsync(1, Self, "deaths", "siege", "day", null, null)); // deaths per context: staff only
        await Assert.ThrowsAsync<StatisticsValidationException>(() =>
            _db.Query().GetSeriesAsync(1, Self, "deaths_by_cause.player", null, "day", null, null)); // internal

        var weekly = await _db.Query().GetSeriesAsync(1, Self, "pvp_kills", null, "week", new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 3));
        Assert.Equal(new[] { (new DateOnly(2026, 9, 21), 0m), (new DateOnly(2026, 9, 28), 10m) }, weekly!.Points.Select(p => (p.PeriodStart, p.Value)));

        var tooLong = await Assert.ThrowsAsync<StatisticsValidationException>(() =>
            _db.Query().GetSeriesAsync(1, Self, "pvp_kills", null, "day", new DateOnly(2025, 1, 1), new DateOnly(2026, 10, 3)));
        Assert.Equal("RangeTooLarge", tooLong.Code);
    }

    [Fact]
    public async Task TitleHistory_IsPrivateByDefault()
    {
        var own = await _db.Query().GetTitleHistoryAsync(1, Self, 1, 20);
        Assert.Equal(("Serf", "Peasant", TitleChangeDirection.Promotion), (own!.Items.Single().FromTitleName, own.Items.Single().ToTitleName, own.Items.Single().Direction));

        await Assert.ThrowsAsync<StatisticsHiddenException>(() => _db.Query().GetTitleHistoryAsync(1, Bob, 1, 20));
        await Assert.ThrowsAsync<StatisticsHiddenException>(() => _db.Query().GetTitleHistoryAsync(1, Anonymous, 1, 20));
    }

    [Fact]
    public async Task DiscoveryList_UsesItsOwnSetting()
    {
        await Assert.ThrowsAsync<StatisticsHiddenException>(() => _db.Query().GetDiscoveriesAsync(1, Bob, 1, 20));
        _db.Context.PlayerStatVisibilities.Add(new PlayerStatVisibility { UserId = 1, SettingKey = "discoveries.list", Visibility = StatisticVisibility.Everyone });
        _db.Context.SaveChanges();

        var list = await _db.Query().GetDiscoveriesAsync(1, Bob, 1, 20);

        Assert.Equal(new[] { "Old Quarter", "Rivia" }, list!.Items.Select(i => i.Name));
        Assert.Equal(new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc), list.Items.Single(i => i.Name == "Rivia").DiscoveredAt);
    }

    [Fact]
    public void Catalog_ListsNoInternalMetrics_AndNamesTheZone()
    {
        var catalog = _db.Query().GetCatalog();

        Assert.Equal("Europe/Amsterdam", catalog.TimeZone);
        Assert.DoesNotContain(catalog.Metrics, m => m.Key.StartsWith("deaths_by_cause") || m.Key == "distance.swim");
        Assert.False(catalog.Metrics.Single(m => m.Key == "deaths").Contextual);
        Assert.Equal(5, catalog.Groups.Count);
        Assert.Equal(22, catalog.Settings.Count);
    }
}

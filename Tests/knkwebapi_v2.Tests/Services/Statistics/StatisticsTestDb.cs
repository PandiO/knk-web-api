using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Statistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>A fixed clock for the statistics services.</summary>
internal sealed class FixedTime : TimeProvider
{
    public FixedTime(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
}

/// <summary>
/// EF InMemory database with the real statistics services wired by hand (pattern of
/// DiscoveryServiceTests). Users 1 (alice), 2 (bob), 3 (carol) exist; "now" is
/// 2026-10-03 12:00 UTC (a Saturday, 14:00 in Amsterdam).
/// </summary>
internal sealed class StatisticsTestDb : IDisposable
{
    public static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    public StatisticsTestDb(StatisticsOptions? options = null)
    {
        Context = NewContext(Guid.NewGuid().ToString());
        Options = options ?? new StatisticsOptions { ProjectionSafetyLagSeconds = 0 };
        Time = new FixedTime(Now);
        Context.Users.AddRange(
            new User { Id = 1, Username = "alice", Uuid = "uuid-a", AccountCreatedVia = AccountCreationMethod.MinecraftServer, CreatedAt = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc) },
            new User { Id = 2, Username = "bob", Uuid = "uuid-b", AccountCreatedVia = AccountCreationMethod.WebApp, CreatedAt = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc) },
            new User { Id = 3, Username = "carol", Uuid = "uuid-c", Gender = Gender.Female });
        Context.SaveChanges();
    }

    public KnKDbContext Context { get; }

    public StatisticsOptions Options { get; }

    public FixedTime Time { get; }

    public static KnKDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(name).Options);

    public StatisticsRepository Repository() => new(Context);

    public StatisticsIngestionService Ingestion() =>
        new(Repository(), Microsoft.Extensions.Options.Options.Create(Options), new StatisticsMetrics(),
            NullLogger<StatisticsIngestionService>.Instance, Time);

    public CurrencyService Currency() =>
        new(new CurrencyRepository(Context), new UserRepository(Context), NullLogger<CurrencyService>.Instance);

    public LedgerStatisticsProjector LedgerProjector() =>
        new(Repository(), Microsoft.Extensions.Options.Options.Create(Options), null, NullLogger<LedgerStatisticsProjector>.Instance, Time);

    public SiegeStatisticsProjector SiegeProjector() =>
        new(Repository(), Microsoft.Extensions.Options.Options.Create(Options), null, NullLogger<SiegeStatisticsProjector>.Instance, Time);

    public StatisticsVisibilityService Visibility() => new(Repository(), Currency(), Time);

    public StatisticsQueryService Query() =>
        new(Repository(), Currency(), new TitleService(new TitleBracketRepository(Context)), new DiscoveryRepository(Context),
            Microsoft.Extensions.Options.Options.Create(Options), Time);

    public LeaderboardRepository LeaderboardRepository() => new(Context);

    public knkwebapi_v2.Services.Leaderboards.LeaderboardSnapshotBuilder LeaderboardBuilder(LeaderboardsOptions? options = null) =>
        new(LeaderboardRepository(), Microsoft.Extensions.Options.Options.Create(options ?? new LeaderboardsOptions()),
            Microsoft.Extensions.Options.Options.Create(Options), Time);

    public knkwebapi_v2.Services.Leaderboards.LeaderboardQueryService LeaderboardQuery() => new(LeaderboardRepository(), Time);

    public List<PlayerStatDaily> Daily(int userId, string metric) =>
        Context.PlayerStatDailies.AsNoTracking().Where(d => d.UserId == userId && d.MetricKey == metric).OrderBy(d => d.Day).ThenBy(d => d.ContextKey).ToList();

    public decimal? Total(int userId, string metric, string context = "") =>
        Context.PlayerStatTotals.AsNoTracking().SingleOrDefault(t => t.UserId == userId && t.MetricKey == metric && t.ContextKey == context)?.Value;

    public void Dispose() => Context.Dispose();
}

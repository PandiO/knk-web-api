using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Privacy;
using knkwebapi_v2.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace knkwebapi_v2.Tests.Services.Telemetry;

/// <summary>
/// EF InMemory database (shared by name, so background-service scopes see the same rows) with the
/// link-6 services wired by hand. Users 1 (alice), 2 (bob), 3 (carol); "now" is 2026-10-03 12:00 UTC.
/// The audit log is a mock so tests can assert what was recorded.
/// </summary>
internal sealed class TelemetryTestDb : IDisposable
{
    public static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _name = Guid.NewGuid().ToString();

    public TelemetryTestDb()
    {
        Context = NewContext();
        Context.Users.AddRange(
            new User { Id = 1, Username = "alice", Uuid = "uuid-a", Email = "a@example.org", PasswordHash = "hash", Gender = Gender.Female },
            new User { Id = 2, Username = "bob", Uuid = "uuid-b" },
            new User { Id = 3, Username = "carol", Uuid = "uuid-c" });
        Context.SaveChanges();
    }

    public KnKDbContext Context { get; }

    public Mock<IAuditLogService> Audit { get; } = new();

    public DiagnosticTelemetryOptions TelemetryOptions { get; } = new();

    public PrivacyOptions PrivacyOptions { get; } = new();

    public DateTime Clock { get; set; } = Now;

    public KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_name).Options);

    public TelemetryRepository Repository() => new(Context);

    public TelemetryWriteQueue Queue(int capacity = 100) => new(capacity, new TelemetryMetrics());

    public TelemetryIngestionService Ingestion(TelemetryWriteQueue queue) =>
        new(Repository(), queue, TelemetryOptions, new TelemetryMetrics(), () => Clock);

    public TelemetryQueryService Query(TelemetryWriteQueue? queue = null) =>
        new(Repository(), queue ?? Queue(), Audit.Object, TelemetryOptions, () => Clock);

    public CurrencyService Currency() =>
        new(new CurrencyRepository(Context), new UserRepository(Context), NullLogger<CurrencyService>.Instance);

    public PrivacyDeletionService Privacy() =>
        new(new PrivacyRepository(Context), Currency(), Audit.Object, PrivacyOptions, () => Clock);

    /// <summary>A provider whose scopes resolve repositories/services on fresh contexts of this database.</summary>
    public ServiceProvider Provider(TelemetryWriteQueue? queue = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        services.AddScoped<IPrivacyRepository, PrivacyRepository>();
        services.AddScoped<IPrivacyDeletionService>(sp => new PrivacyDeletionService(sp.GetRequiredService<IPrivacyRepository>(),
            new CurrencyService(new CurrencyRepository(sp.GetRequiredService<KnKDbContext>()),
                new UserRepository(sp.GetRequiredService<KnKDbContext>()), NullLogger<CurrencyService>.Instance),
            Audit.Object, PrivacyOptions, () => Clock));
        if (queue != null) services.AddSingleton(queue);
        return services.BuildServiceProvider();
    }

    /// <summary>A valid plugin event (baseline <c>session.join</c> unless changed).</summary>
    public static TelemetryEventDto Event(string name = "session.join", int? userId = 1, DateTime? at = null,
        string outcome = "succeeded", string? level = null) => new()
    {
        EventId = Guid.NewGuid(),
        Name = name,
        SchemaVersion = 1,
        OccurredAt = at ?? Now.AddMinutes(-1),
        ServerName = "paper-1",
        ServerSeq = 1,
        AppVersion = "1.0.0",
        Level = level,
        UserId = userId,
        Outcome = outcome
    };

    /// <summary>A stored event.</summary>
    public static TelemetryEvent Stored(string name, int? userId, DateTime at, string? correlationId = null,
        Enums.TelemetryLevel level = Enums.TelemetryLevel.Baseline, long seq = 0, int? matchId = null) => new()
    {
        EventId = Guid.NewGuid(),
        Name = name,
        Level = level,
        OccurredAt = at,
        ReceivedAt = at,
        ServerName = "paper-1",
        ServerSeq = seq,
        AppVersion = "1.0.0",
        UserId = userId,
        CorrelationId = correlationId,
        MatchId = matchId,
        Feature = name.Split('.')[0],
        Action = name.Split('.')[1],
        Outcome = Enums.TelemetryOutcome.Succeeded
    };

    public void Dispose() => Context.Dispose();
}

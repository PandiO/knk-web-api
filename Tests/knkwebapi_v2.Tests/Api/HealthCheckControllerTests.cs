using knkwebapi_v2.Controllers;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>KNG-115: /health/ready reflects the database; /health/live stays process-only.
/// The Minecraft plugin's ApiConnectivity probe reads /health/ready.</summary>
[Trait("Category", "API")]
public class HealthCheckControllerTests
{
    private static HealthReport Report(HealthStatus database) => new(
        new Dictionary<string, HealthReportEntry>
        {
            ["self"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
            [DatabaseHealthCheck.Name] = new(database, null, TimeSpan.Zero, null, null, new[] { DatabaseHealthCheck.Tag }),
        },
        TimeSpan.Zero);

    private static HealthCheckController Controller(HealthReport report, out Mock<HealthCheckService> service)
    {
        service = new Mock<HealthCheckService>();
        service.Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        return new HealthCheckController(service.Object, NullLogger<HealthCheckController>.Instance);
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, 200, "healthy")]
    [InlineData(HealthStatus.Degraded, 200, "degraded")]
    [InlineData(HealthStatus.Unhealthy, 503, "unhealthy")]
    public async Task Ready_MapsTheReportToStatusCodeAndBody(HealthStatus database, int expectedCode, string expectedStatus)
    {
        var result = Assert.IsType<ObjectResult>(await Controller(Report(database), out _).GetReadiness());

        Assert.Equal(expectedCode, result.StatusCode);
        var body = Assert.IsType<HealthResponse>(result.Value);
        Assert.Equal(expectedStatus, body.Status);
        Assert.Equal(expectedStatus, body.Checks[DatabaseHealthCheck.Name]);
        Assert.Equal("healthy", body.Checks["self"]);
    }

    [Fact]
    public void Live_DoesNotRunAnyCheck()
    {
        var controller = Controller(Report(HealthStatus.Unhealthy), out var service);

        var result = Assert.IsType<OkObjectResult>(controller.GetLiveness());

        Assert.Equal("healthy", Assert.IsType<HealthResponse>(result.Value).Status);
        service.Verify(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static HealthCheckService Registered(Action<IServiceCollection> addDb)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        addDb(services);
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy())
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, HealthStatus.Unhealthy,
                new[] { DatabaseHealthCheck.Tag }, TimeSpan.FromSeconds(3));
        return services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
    }

    [Fact]
    public async Task DatabaseCheck_IsHealthyWhenTheDatabaseAnswers()
    {
        var service = Registered(s => s.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase("health-ok")));

        var report = await service.CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(HealthStatus.Healthy, report.Entries[DatabaseHealthCheck.Name].Status);
    }

    [Fact]
    public async Task DatabaseCheck_IsUnhealthyWhenMySqlIsUnreachable()
    {
        // Port 1 on loopback: nothing listens, the connection is refused at once.
        const string unreachable = "Server=127.0.0.1;Port=1;Database=knk;User=knk;Password=x;Connection Timeout=2";
        var service = Registered(s => s.AddDbContext<KnKDbContext>(o =>
            o.UseMySql(unreachable, new MySqlServerVersion(new Version(8, 0, 36)))));

        var report = await service.CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal(HealthStatus.Unhealthy, report.Entries[DatabaseHealthCheck.Name].Status);
        Assert.Equal(HealthStatus.Healthy, report.Entries["self"].Status);
    }

    [Fact]
    public async Task DatabaseCheck_IsUnhealthyWhenBuildingTheDbContextThrows()
    {
        // Program.cs's ServerVersion.AutoDetect throws while the DbContext is built when MySQL is
        // down; that must read as unhealthy, not escape as a 500 (live test 2026-10-11).
        var service = Registered(s => s.AddScoped<KnKDbContext>(_ =>
            throw new InvalidOperationException("Unable to connect to any of the specified MySQL hosts.")));

        var report = await service.CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal(HealthStatus.Unhealthy, report.Entries[DatabaseHealthCheck.Name].Status);
    }

    [Fact]
    public async Task Ready_IsA503WhenTheHealthCheckServiceThrows()
    {
        var service = new Mock<HealthCheckService>();
        service.Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var controller = new HealthCheckController(service.Object, NullLogger<HealthCheckController>.Instance);

        var result = Assert.IsType<ObjectResult>(await controller.GetReadiness());

        Assert.Equal(503, result.StatusCode);
        Assert.Equal("unhealthy", Assert.IsType<HealthResponse>(result.Value).Status);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Ready_LogsAnOutageOnceAndTheRecoveryOnce()
    {
        var logger = new ListLogger<HealthCheckController>();
        async Task Probe(HealthStatus db)
        {
            var service = new Mock<HealthCheckService>();
            service.Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Report(db));
            await new HealthCheckController(service.Object, logger).GetReadiness();
        }

        await Probe(HealthStatus.Healthy); // settle the shared last-status
        logger.Entries.Clear();

        await Probe(HealthStatus.Unhealthy);
        await Probe(HealthStatus.Unhealthy);
        await Probe(HealthStatus.Unhealthy);
        await Probe(HealthStatus.Healthy);
        await Probe(HealthStatus.Healthy);

        var levels = logger.Entries.Where(e => e.Level >= LogLevel.Information).Select(e => e.Level).ToList();
        Assert.Equal(new[] { LogLevel.Warning, LogLevel.Information }, levels);
        Assert.Contains("database=Unhealthy", logger.Entries[0].Message);
    }

    [Fact]
    public async Task DatabaseCheck_FailureCarriesNoExceptionSoItIsNotLoggedWithAStack()
    {
        var service = Registered(s => s.AddScoped<KnKDbContext>(_ =>
            throw new InvalidOperationException("Unable to connect to any of the specified MySQL hosts.")));

        var entry = (await service.CheckHealthAsync()).Entries[DatabaseHealthCheck.Name];

        Assert.Null(entry.Exception);
        Assert.Contains("Unable to connect", entry.Description);
    }

    [Fact]
    public async Task DatabaseCheck_CarriesTheReadyTag()
    {
        var service = Registered(s => s.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase("health-tag")));

        var tagged = await service.CheckHealthAsync(r => r.Tags.Contains(DatabaseHealthCheck.Tag));

        Assert.Equal(new[] { DatabaseHealthCheck.Name }, tagged.Entries.Keys);
    }
}

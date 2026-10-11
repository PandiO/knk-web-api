using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MySqlConnector;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.LocationRetention;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Background loops while MySQL is down (KNG-115 live-test follow-up): a connectivity failure logs
/// one short line when it starts and one when the loop recovers, instead of a stack trace on every
/// iteration; any other failure still logs LogError with its exception every time.
/// </summary>
public class ConnectivityAwareFailureLoggerTests
{
    [Fact]
    public void RepeatedConnectivityFailures_LogOnce_ThenRecoveryLogsOnce()
    {
        var logger = new ListLogger();
        var failures = new ConnectivityAwareFailureLogger(logger, "Rank expiry sweep");

        for (var i = 0; i < 5; i++)
        {
            failures.Failed(Unreachable(), "Rank expiry sweep failed");
        }

        var outage = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, outage.Level);
        Assert.Null(outage.Exception);
        Assert.Contains("Rank expiry sweep: database unreachable", outage.Message);
        Assert.True(failures.DatabaseUnreachable);

        failures.Succeeded();
        failures.Succeeded();

        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal((LogLevel.Information, "Rank expiry sweep: database reachable again"),
            (logger.Entries[1].Level, logger.Entries[1].Message));
        Assert.False(failures.DatabaseUnreachable);

        // A second outage is announced again.
        failures.Failed(Unreachable(), "Rank expiry sweep failed");
        Assert.Equal(3, logger.Entries.Count);
    }

    [Fact]
    public void SuccessWithoutOutage_LogsNothing()
    {
        var logger = new ListLogger();
        new ConnectivityAwareFailureLogger(logger, "x").Succeeded();
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void OtherExceptions_LogErrorWithStackTraceEveryTime_EvenDuringAnOutage()
    {
        var logger = new ListLogger();
        var failures = new ConnectivityAwareFailureLogger(logger, "Rank expiry sweep");
        failures.Failed(Unreachable(), "Rank expiry sweep failed");

        var duplicate = new InvalidOperationException("save failed", MySql(MySqlErrorCode.DuplicateKeyEntry, "Duplicate entry"));
        for (var i = 0; i < 3; i++)
        {
            failures.Failed(new InvalidOperationException("bug"), "Rank expiry sweep failed");
            failures.Failed(duplicate, "Rank expiry sweep failed");
        }

        var errors = logger.Entries.Skip(1).ToList();
        Assert.Equal(6, errors.Count);
        Assert.All(errors, e =>
        {
            Assert.Equal(LogLevel.Error, e.Level);
            Assert.NotNull(e.Exception);
            Assert.Equal("Rank expiry sweep failed", e.Message);
        });
    }

    [Fact]
    public void IsDatabaseConnectivityError_FindsTheMySqlExceptionInTheInnerChain()
    {
        Assert.True(ConnectivityAwareFailureLogger.IsDatabaseConnectivityError(Unreachable()));
        Assert.True(ConnectivityAwareFailureLogger.IsDatabaseConnectivityError(
            new InvalidOperationException("outer", new Exception("middle", Unreachable()))));
        Assert.False(ConnectivityAwareFailureLogger.IsDatabaseConnectivityError(new TimeoutException()));
        Assert.False(ConnectivityAwareFailureLogger.IsDatabaseConnectivityError(
            MySql(MySqlErrorCode.BadFieldError, "Unknown column")));
    }

    [Fact]
    public async Task CurrencyMonitor_DatabaseDown_LogsOncePerStep_AndRecoveryOncePerStep()
    {
        var down = true;
        var alerts = new Mock<ICurrencyAlertService>();
        alerts.Setup(a => a.FlushSignalsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        alerts.Setup(a => a.RunRulesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        await using var provider = new ServiceCollection()
            .AddScoped<ICurrencyAlertService>(_ => down ? throw Unreachable() : alerts.Object)
            .BuildServiceProvider();
        var logger = new ListLogger<CurrencyMonitorService>();
        var monitor = new CurrencyMonitorService(provider, logger, Options.Create(new CurrencyMonitorOptions()));
        var now = DateTime.UtcNow;

        // Signals, rules and reconciliation all run in the first cycle; later cycles keep failing.
        for (var minute = 0; minute <= 120; minute++)
        {
            await monitor.RunCycleAsync(now.AddMinutes(minute));
        }
        Assert.Equal(3, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.Equal((LogLevel.Warning, (Exception?)null), (e.Level, e.Exception)));

        down = false;
        await monitor.RunCycleAsync(now.AddMinutes(121));
        await monitor.RunCycleAsync(now.AddMinutes(200));
        await monitor.RunCycleAsync(now.AddMinutes(201));

        var recovered = logger.Entries.Skip(3).ToList();
        Assert.Equal(3, recovered.Count);
        Assert.All(recovered, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.Contains(recovered, e => e.Message == "Currency monitor step signals: database reachable again");
    }

    [Fact]
    public async Task LocationRetentionScheduler_DatabaseDown_LogsOnce_ThenRecovery()
    {
        var down = true;
        var retention = new Mock<ILocationRetentionService>();
        retention.Setup(r => r.DueScheduledSlotAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);
        await using var provider = new ServiceCollection()
            .AddScoped<ILocationRetentionService>(_ => down ? throw Unreachable() : retention.Object)
            .BuildServiceProvider();
        var logger = new ListLogger<LocationRetentionScheduler>();
        var scheduler = new LocationRetentionScheduler(provider, logger);

        for (var i = 0; i < 4; i++)
        {
            await scheduler.TickAsync(DateTime.UtcNow);
        }
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);

        down = false;
        await scheduler.TickAsync(DateTime.UtcNow);
        await scheduler.TickAsync(DateTime.UtcNow);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(LogLevel.Information, logger.Entries[1].Level);
    }

    /// <summary>What MySqlConnector throws when MySQL is stopped (e.g. from ServerVersion.AutoDetect).</summary>
    private static MySqlException Unreachable() =>
        MySql(MySqlErrorCode.UnableToConnectToHost, "Unable to connect to any of the specified MySQL hosts.");

    /// <summary>MySqlException has no public constructor; use its internal (code, message, ...) one.</summary>
    private static MySqlException MySql(MySqlErrorCode code, string message)
    {
        var ctor = typeof(MySqlException).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(c => c.GetParameters().Any(p => p.ParameterType == typeof(MySqlErrorCode))
                && c.GetParameters().Any(p => p.ParameterType == typeof(string)))
            .OrderBy(c => c.GetParameters().Length)
            .First();
        var args = ctor.GetParameters().Select(p =>
            p.ParameterType == typeof(MySqlErrorCode) ? code
            : p.ParameterType == typeof(string) ? message
            : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
            : null).ToArray();
        return (MySqlException)ctor.Invoke(args);
    }

    private record Entry(LogLevel Level, string Message, Exception? Exception);

    private class ListLogger : ILogger
    {
        public List<Entry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
    }

    private class ListLogger<T> : ListLogger, ILogger<T>
    {
    }
}

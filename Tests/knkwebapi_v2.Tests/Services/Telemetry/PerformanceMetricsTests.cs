using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Telemetry;

/// <summary>KNG-232: cgroup reading, the metrics endpoint gate, and the DB command meter.</summary>
public class PerformanceMetricsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "knk-cgroup-" + Guid.NewGuid());

    public PerformanceMetricsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void CgroupV2_WorkingSetSubtractsInactiveFileCache()
    {
        Write("memory.current", "4000000000\n");
        Write("memory.max", "5368709120\n");
        Write("memory.stat", "anon 1500000000\nfile 2400000000\ninactive_file 2000000000\n");
        Write("cpu.stat", "usage_usec 12500000\nthrottled_usec 2000000\nnr_throttled 7\n");

        var snapshot = new CgroupReader(_root).Read();

        snapshot.Should().NotBeNull();
        snapshot!.MemoryUsageBytes.Should().Be(4_000_000_000);
        snapshot.MemoryWorkingSetBytes.Should().Be(2_000_000_000);
        snapshot.MemoryLimitBytes.Should().Be(5_368_709_120);
        snapshot.CpuUsageSeconds.Should().Be(12.5);
        snapshot.CpuThrottledSeconds.Should().Be(2.0);
        snapshot.CpuThrottledPeriods.Should().Be(7);
    }

    [Fact]
    public void CgroupV2_UnlimitedMemoryHasNoLimit()
    {
        Write("memory.current", "100\n");
        Write("memory.max", "max\n");

        var snapshot = new CgroupReader(_root).Read();

        snapshot!.MemoryLimitBytes.Should().BeNull();
        snapshot.MemoryWorkingSetBytes.Should().Be(100);
    }

    [Fact]
    public void CgroupV1_FallbackReadsUsageAndTreatsHugeLimitAsUnlimited()
    {
        Write("memory/memory.usage_in_bytes", "3000\n");
        Write("memory/memory.limit_in_bytes", "9223372036854771712\n");
        Write("memory/memory.stat", "total_inactive_file 1000\n");
        Write("cpuacct/cpuacct.usage", "2000000000\n");

        var snapshot = new CgroupReader(_root).Read();

        snapshot!.MemoryUsageBytes.Should().Be(3000);
        snapshot.MemoryWorkingSetBytes.Should().Be(2000);
        snapshot.MemoryLimitBytes.Should().BeNull();
        snapshot.CpuUsageSeconds.Should().Be(2.0);
    }

    [Fact]
    public void NoCgroup_ReadsNull() => new CgroupReader(_root).Read().Should().BeNull();

    [Theory]
    [InlineData(false, "", false, null)]
    [InlineData(true, "", true, null)]
    [InlineData(true, "", false, "BearerToken")]
    [InlineData(true, "secret", false, null)]
    public void Problem_RequiresATokenOutsideDevelopment(bool enabled, string token, bool development, string? expectedFragment)
    {
        var options = new PrometheusExporterOptions { Enabled = enabled, BearerToken = token };

        var problem = MetricsEndpoint.Problem(options, development);

        if (expectedFragment == null) problem.Should().BeNull();
        else problem.Should().Contain(expectedFragment);
    }

    [Fact]
    public void Problem_EndpointMustStartWithSlash() =>
        MetricsEndpoint.Problem(new PrometheusExporterOptions { Enabled = true, Endpoint = "metrics", BearerToken = "x" }, false)
            .Should().Contain("must start with '/'");

    [Theory]
    [InlineData("Bearer secret", "secret", true)]
    [InlineData("bearer secret", "secret", true)]
    [InlineData("Bearer wrong", "secret", false)]
    [InlineData("secret", "secret", false)]
    [InlineData(null, "secret", false)]
    [InlineData(null, "", true)]
    public void IsAuthorized_ComparesTheBearerToken(string? header, string token, bool expected) =>
        MetricsEndpoint.IsAuthorized(header, token).Should().Be(expected);

    [Fact]
    public async Task ScrapeEndpoint_AnswersOnlyWithTheToken_AndServesTheMeters()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
        {
            metrics.AddMeter(DbCommandMetrics.MeterName);
            metrics.AddView(DbCommandMetrics.DurationName,
                new ExplicitBucketHistogramConfiguration { Boundaries = DbCommandMetrics.BucketBoundaries });
            metrics.AddPrometheusExporter();
        });
        var options = new PrometheusExporterOptions { Enabled = true, BearerToken = "secret" };
        await using var app = builder.Build();
        app.MapKnkMetrics(options);
        await app.StartAsync();
        using var dbMetrics = new DbCommandMetrics();
        dbMetrics.Record("reader", TimeSpan.FromMilliseconds(3), true);
        var client = app.GetTestClient();

        var denied = await client.GetAsync("/metrics");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");
        var allowed = await client.GetAsync("/metrics");
        var body = await allowed.Content.ReadAsStringAsync();

        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("knk_db_command_duration_seconds_bucket");
        body.Should().Contain("outcome=\"ok\"");
        await app.StopAsync();
    }

    [Fact]
    public void DbCommandMetrics_RecordsSecondsWithKindAndOutcome()
    {
        using var metrics = new DbCommandMetrics();
        var seen = new List<(double Value, string Kind, string Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DbCommandMetrics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            string kind = "", outcome = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "kind") kind = (string)tag.Value!;
                if (tag.Key == "outcome") outcome = (string)tag.Value!;
            }
            seen.Add((value, kind, outcome));
        });
        listener.Start();

        metrics.Record("write", TimeSpan.FromMilliseconds(250), true);
        metrics.Record("command", TimeSpan.FromSeconds(1), false);

        seen.Should().Equal((0.25, "write", "ok"), (1.0, "command", "error"));
    }
}

using System;
using System.Collections.Generic;
using FluentAssertions;
using knkwebapi_v2.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// The server version is resolved once per process, not per DbContext (KNG-115 live-test follow-up).
/// </summary>
public class MySqlServerVersionResolverTests
{
    private static readonly ServerVersion Detected = new MySqlServerVersion(new Version(8, 0, 36));

    [Fact]
    public void ConfiguredVersion_IsUsed_WithoutDetecting()
    {
        var resolver = new MySqlServerVersionResolver("8.0.36", () => throw new InvalidOperationException("must not detect"));

        resolver.IsConfigured.Should().BeTrue();
        resolver.Version.Version.Should().Be(new Version(8, 0, 36));
        resolver.Version.Type.Should().Be(ServerType.MySql);
    }

    [Fact]
    public void ConfiguredMariaDbVersion_IsParsed()
    {
        var resolver = new MySqlServerVersionResolver("10.11.6-mariadb", () => throw new InvalidOperationException("must not detect"));

        resolver.Version.Type.Should().Be(ServerType.MariaDb);
        resolver.Version.Version.Should().Be(new Version(10, 11, 6));
    }

    [Fact]
    public void ConfiguredVersion_IsReadFromTheDatabaseSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MySqlServerVersion"] = "8.0.36" })
            .Build();

        var resolver = new MySqlServerVersionResolver(configuration, connectionString: null);

        resolver.IsConfigured.Should().BeTrue();
        resolver.Version.Version.Should().Be(new Version(8, 0, 36));
    }

    [Fact]
    public void InvalidConfiguredVersion_FailsAtStartup()
    {
        var act = () => new MySqlServerVersionResolver("not-a-version", () => Detected);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Database:MySqlServerVersion*not-a-version*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankVersion_DetectsOnce_OnFirstUse(string? configured)
    {
        var calls = 0;
        var resolver = new MySqlServerVersionResolver(configured, () => { calls++; return Detected; });

        resolver.IsConfigured.Should().BeFalse();
        calls.Should().Be(0, "detection waits for the first DbContext");

        resolver.Version.Should().BeSameAs(Detected);
        resolver.Version.Should().BeSameAs(Detected);
        calls.Should().Be(1);
    }

    [Fact]
    public void FailedDetection_IsNotCached_SoTheNextDbContextRetries()
    {
        var calls = 0;
        var resolver = new MySqlServerVersionResolver(null, () =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("MySQL is down");
            }
            return Detected;
        });

        var first = () => resolver.Version;
        first.Should().Throw<InvalidOperationException>().WithMessage("MySQL is down");

        resolver.Version.Should().BeSameAs(Detected);
        resolver.Version.Should().BeSameAs(Detected);
        calls.Should().Be(2);
    }
}

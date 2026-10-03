using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-34 link 6: the telemetry/privacy migration is additive only (charter §3) — four new tables
/// and their indexes, no foreign keys (GDPR deletion removes rows explicitly), one check constraint
/// (an enhanced target names exactly one of user / test run).
/// </summary>
public class AddDiagnosticTelemetryAndPrivacyTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddDiagnosticTelemetryAndPrivacy().UpOperations;

    [Fact]
    public void Up_OnlyCreatesTheFourTables()
    {
        Assert.Equal(new[] { "privacy_deletion_requests", "telemetry_enhanced_targets", "telemetry_events", "telemetry_test_runs" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(Up(), op => Assert.True(op is CreateTableOperation or CreateIndexOperation, op.GetType().Name));
    }

    [Fact]
    public void Up_HasNoForeignKeys_AndOneCheckConstraint()
    {
        var tables = Up().OfType<CreateTableOperation>().ToList();

        Assert.Empty(tables.SelectMany(t => t.ForeignKeys));
        var check = Assert.Single(tables.SelectMany(t => t.CheckConstraints));
        Assert.Equal("CK_telemetry_enhanced_targets_OneTarget", check.Name);
    }

    [Fact]
    public void Up_EventIdIsUnique_AndTheTimelineIndexesExist()
    {
        var indexes = Up().OfType<CreateIndexOperation>().Where(i => i.Table == "telemetry_events").ToList();

        Assert.True(indexes.Single(i => i.Columns.SequenceEqual(new[] { "EventId" })).IsUnique);
        Assert.Contains(indexes, i => i.Columns.SequenceEqual(new[] { "UserId", "OccurredAt" }));
        Assert.Contains(indexes, i => i.Columns.SequenceEqual(new[] { "CorrelationId" }));
        Assert.Contains(indexes, i => i.Columns.SequenceEqual(new[] { "OccurredAt" }));
    }

    [Fact]
    public void Down_DropsOnlyTheFourTables()
    {
        Assert.All(new AddDiagnosticTelemetryAndPrivacy().DownOperations, op => Assert.IsType<DropTableOperation>(op));
        Assert.Equal(4, new AddDiagnosticTelemetryAndPrivacy().DownOperations.Count);
    }
}

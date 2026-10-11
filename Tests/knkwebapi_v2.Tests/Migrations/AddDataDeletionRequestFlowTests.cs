using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// The deletion request flow migration (developer decisions 2026-10-03) is additive: seven nullable
/// or defaulted columns on privacy_deletion_requests, two indexes, and one update that schedules
/// requests from before the flow. Earlier rows were all filed by the owner (Source = 2).
/// </summary>
public class AddDataDeletionRequestFlowTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddDataDeletionRequestFlow().UpOperations;

    [Fact]
    public void Up_OnlyAddsColumnsIndexesAndOneUpdate()
    {
        Assert.All(Up(), op => Assert.True(op is AddColumnOperation or CreateIndexOperation or SqlOperation, op.GetType().Name));
        var columns = Up().OfType<AddColumnOperation>().ToList();
        Assert.All(columns, c => Assert.Equal("privacy_deletion_requests", c.Table));
        Assert.Equal(new[] { "CancelledAt", "CancelledByUserId", "ConfirmationExpiresAt", "ConfirmationTokenHash", "ConfirmedAt", "ScheduledAt", "Source" },
            columns.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(columns.Where(c => c.Name != "Source"), c => Assert.True(c.IsNullable, c.Name));
        Assert.Equal((byte)2, columns.Single(c => c.Name == "Source").DefaultValue);
        Assert.Equal(2, Up().OfType<CreateIndexOperation>().Count());
    }

    [Fact]
    public void Up_SchedulesEarlierPendingRequests()
    {
        var sql = Assert.Single(Up().OfType<SqlOperation>()).Sql;

        Assert.Contains("`ScheduledAt` = DATE_ADD(`RequestedAt`, INTERVAL 5 DAY)", sql);
        Assert.Contains("WHERE `Status` = 0", sql);
    }
}

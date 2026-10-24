using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// The one-off cleanup for the "Residential District\n" rows: data only, one UPDATE per trimmed column, every
/// kind of leading/trailing whitespace (MySQL's TRIM() takes spaces only), clean rows left alone, nothing to undo.
/// </summary>
public class TrimDomainNamesTests
{
    private static IReadOnlyList<SqlOperation> Up() => new TrimDomainNames().UpOperations.Cast<SqlOperation>().ToList();

    [Fact]
    public void Up_IsDataOnly_OneStatementPerColumn()
    {
        var migration = new TrimDomainNames();

        Assert.All(migration.UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.Equal(new[] { "Name", "Description" }, TrimDomainNames.Columns);
        Assert.Equal(TrimDomainNames.Columns.Length, Up().Count);
        Assert.Equal(TrimDomainNames.TrimSql("Name"), Up()[0].Sql);
        Assert.Equal(TrimDomainNames.TrimSql("Description"), Up()[1].Sql);
    }

    [Fact]
    public void TrimSql_StripsEveryWhitespaceKindAtBothEnds_OnlyWhereThereIsAny()
    {
        var sql = TrimDomainNames.TrimSql("Name");

        Assert.StartsWith("UPDATE domains SET `Name` = REGEXP_REPLACE(`Name`, '^[[:space:]]+|[[:space:]]+$', '')", sql);
        Assert.Contains("WHERE `Name` REGEXP '^[[:space:]]|[[:space:]]$'", sql);
        Assert.DoesNotContain("TRIM(", sql);
    }

    [Fact]
    public void Down_DoesNothing()
    {
        Assert.Empty(new TrimDomainNames().DownOperations);
    }

    [Fact]
    public void RunsAfterTheSubRegionsMigration()
    {
        const string self = "20261024230750_TrimDomainNames";
        Assert.True(string.CompareOrdinal(self, "20261023144845_AddFieldValidationRunLogs") > 0);
    }
}

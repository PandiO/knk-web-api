using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// The one-off cleanup for the "Residential District\n" rows: data only, one UPDATE on domains.Name that strips
/// every kind of leading/trailing whitespace (MySQL's TRIM() takes spaces only, hence REGEXP_REPLACE with the POSIX
/// class), clean rows left alone, nothing to undo. TrimDomainNamesMySqlTests runs it against a real server.
/// </summary>
public class TrimDomainNamesTests
{
    [Fact]
    public void Up_IsOneDataOnlyStatement_ThatTrimsEveryWhitespaceKindAtBothEnds()
    {
        var migration = new TrimDomainNames();

        var sql = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations)).Sql;
        Assert.StartsWith("UPDATE domains SET Name = REGEXP_REPLACE(Name, '^[[:space:]]+|[[:space:]]+$', '')", sql);
        Assert.Contains("WHERE Name <> REGEXP_REPLACE(Name, '^[[:space:]]+|[[:space:]]+$', '')", sql);
        Assert.DoesNotContain("TRIM(", sql);
    }

    [Fact]
    public void Down_DoesNothing()
    {
        Assert.Empty(new TrimDomainNames().DownOperations);
    }
}

using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-132: data only; describes the GateDoor anchor/reference/geometry/motion distance fields, only where a GateDoor
/// form's field has no description yet, and Down removes only its own text.
/// </summary>
public class DescribeGateDoorGeometryFieldsTests
{
    private static IReadOnlyList<SqlOperation> Up() =>
        new DescribeGateDoorGeometryFields().UpOperations.Cast<SqlOperation>().ToList();

    [Fact]
    public void Up_FillsOnlyEmptyDescriptions_OfGateDoorFormFields()
    {
        Assert.All(new DescribeGateDoorGeometryFields().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.Equal(DescribeGateDoorGeometryFields.Descriptions.Count, Up().Count);
        Assert.All(Up(), op => Assert.Contains("(`f`.`Description` IS NULL OR `f`.`Description` = '')", op.Sql));
        Assert.All(Up(), op => Assert.Contains("`c`.`EntityTypeName` = 'GateDoor'", op.Sql));
        Assert.Contains(Up(), op => op.Sql.Contains("`f`.`FieldName` = 'ReferencePoint2Id'")
            && op.Sql.Contains("straight above the anchor (not above reference point 1)"));
    }

    [Fact]
    public void Apostrophes_AreEscaped()
    {
        Assert.Contains(Up(), op => op.Sql.Contains("the door''s bottom edge"));
        Assert.DoesNotContain(Up(), op => op.Sql.Contains("door's"));
    }

    [Fact]
    public void Down_ClearsOnlyItsOwnText()
    {
        var down = new DescribeGateDoorGeometryFields().DownOperations.Cast<SqlOperation>().ToList();

        Assert.Equal(Up().Count, down.Count);
        Assert.All(down, op => Assert.Contains("SET `f`.`Description` = NULL", op.Sql));
        Assert.All(down, op => Assert.Contains("AND `f`.`Description` = '", op.Sql));
    }

    [Fact]
    public void Sql_RunsOnMySql_WhenADumpPathIsGiven()
    {
        // Writes the statements for a manual MySQL run (KNG132_SQL_DUMP=path); nothing to check otherwise.
        var path = Environment.GetEnvironmentVariable("KNG132_SQL_DUMP");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllLines(path, DescribeGateDoorGeometryFields.UpSql());
        File.WriteAllLines(path + ".down", DescribeGateDoorGeometryFields.DownSql());
    }
}

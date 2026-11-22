using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Landmark domain A08 (LD15, LD24, PandiO/knk-workspace#984): the migration only seeds the <c>Landmark</c> discovery
/// type rule, disabled, with no ancestors; Down removes that one row. The Migrations workflow applies Up and Down on a
/// fresh MySQL database.
/// </summary>
public class AddLandmarkDiscoveryRuleTests
{
    [Fact]
    public void Up_SeedsOneDisabledLandmarkRule_AndChangesNoSchema()
    {
        var operations = new AddLandmarkDiscoveryRule().UpOperations;
        var insert = Assert.IsType<InsertDataOperation>(Assert.Single(operations));
        var columns = insert.Columns.ToList();
        object? Value(string column) => insert.Values[0, columns.IndexOf(column)];

        Assert.Equal("discovery_reward_rules", insert.Table);
        Assert.Equal(1, insert.Values.GetLength(0));
        Assert.Equal(("Landmark", false, false), (Value("DomainType"), Value("IsEnabled"), Value("IncludeAncestors")));
        Assert.Equal((0, 0), (Value("GemsMin"), Value("GemsMax")));
    }

    [Fact]
    public void Down_RemovesOnlyTheLandmarkRule()
    {
        var delete = Assert.IsType<DeleteDataOperation>(Assert.Single(new AddLandmarkDiscoveryRule().DownOperations));

        Assert.Equal("discovery_reward_rules", delete.Table);
        Assert.Equal("Landmark", delete.KeyValues[0, 0]);
    }
}

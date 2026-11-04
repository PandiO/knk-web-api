using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Chat hashtag tokens (PandiO/knk-workspace#233): every token for the Default group, the limit bypass for the staff
/// ranks only. Data only, the parent-chain check of package C's seed, and Down removes only these grants.
/// </summary>
public class SeedChatTokenNodesTests
{
    private static IReadOnlyList<string> Up() => new SeedChatTokenNodes().UpOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    private static IReadOnlyList<string> Down() => new SeedChatTokenNodes().DownOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    [Fact]
    public void Up_IsDataOnly_AndEveryGrantChecksTheGroupsParentChain()
    {
        Assert.All(new SeedChatTokenNodes().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.All(Up(), sql => Assert.Contains("WITH RECURSIVE anc", sql));
        Assert.Equal(SeedChatTokenNodes.NewGrants.Sum(g => SeedCommandPermissionNodes.Tiers[g.Tier].Length), Up().Count);
    }

    [Fact]
    public void EveryoneGetsTheTokens_ButOnlyStaffRanksTheBypass()
    {
        Assert.Contains(Up(), sql => sql.Contains("pg.Name = 'Default'") && sql.Contains("'knk.chat.token.*'"));
        Assert.DoesNotContain(Up(), sql => sql.Contains("pg.Name = 'Default'") && sql.Contains("knk.chat.tokens.bypass"));
        foreach (var group in new[] { "Staff", "Moderator", "Admin" })
        {
            Assert.Contains(Up(), sql => sql.Contains($"pg.Name = '{group}'") && sql.Contains("'knk.chat.tokens.bypass'"));
        }
        // The wildcard must not cover the bypass node.
        Assert.False("knk.chat.tokens.bypass".StartsWith("knk.chat.token."));
    }

    [Fact]
    public void Down_RemovesOnlyItsOwnGrants()
    {
        Assert.Equal(Up().Count, Down().Count);
        Assert.All(Down(), sql => Assert.Contains("g.Value = 1 AND g.ExpiresAt IS NULL", sql));
    }

    [Fact]
    public void RunsAfterTheSchemaMigrationOfThisFeature()
    {
        Assert.True(string.CompareOrdinal("20261104130000_SeedChatTokenNodes", "20261104121131_AddChatTokenSettings") > 0);
    }
}

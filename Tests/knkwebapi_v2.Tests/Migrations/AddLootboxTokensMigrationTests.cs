using FluentAssertions;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Lootboxes Phase 5: AddLootboxTokens creates the token and grant-rule tables and the uniqueness a single-use token
/// relies on (one claim per token, one token per id, one row per issue key and index). Applied, rolled back and
/// re-applied on MySQL 8 when written; the Migrations (fresh DB) workflow re-applies it on every push.
/// </summary>
public class AddLootboxTokensMigrationTests
{
    [Fact]
    public void AddLootboxTokens_CreatesTheTables_AndTheUniqueIndexes()
    {
        var ops = new AddLootboxTokens().UpOperations;

        ops.OfType<CreateTableOperation>().Select(t => t.Name).Should().BeEquivalentTo("lootbox_tokens", "lootbox_token_grants");
        ops.OfType<AddColumnOperation>().Select(c => (c.Table, c.Name, c.IsNullable)).Should().Equal(("lootbox_claims", "LootboxTokenId", true));
        ops.OfType<CreateIndexOperation>().Where(i => i.IsUnique).Select(i => (i.Table, string.Join(",", i.Columns))).Should().BeEquivalentTo(new[]
        {
            ("lootbox_claims", "LootboxTokenId"),
            ("lootbox_tokens", "Token"),
            ("lootbox_tokens", "IssueKey,IssueIndex"),
        });
    }
}

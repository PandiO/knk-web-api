using FluentAssertions;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Lootboxes Phase 1: the two migrations create exactly the planned tables and the uniqueness guarantees the claim
/// relies on (DESIGN.md §3.2-§3.3). Both were also applied, rolled back and re-applied on MySQL 8 when written; the
/// Migrations (fresh DB) workflow re-applies them on every push.
/// </summary>
public class AddLootboxesMigrationTests
{
    [Fact]
    public void AddItemInstances_CreatesTheInstanceTables()
    {
        var ops = new AddItemInstances().UpOperations;

        ops.OfType<CreateTableOperation>().Select(t => t.Name).Should().BeEquivalentTo("item_instances", "item_instance_enchantments");
        ops.OfType<CreateTableOperation>().Single(t => t.Name == "item_instances").Columns.Single(c => c.Name == "Id").ClrType.Should().Be(typeof(long));
    }

    [Fact]
    public void AddLootboxes_CreatesTheTenTables_AndTheUniqueIndexes()
    {
        var ops = new AddLootboxes().UpOperations;

        ops.OfType<CreateTableOperation>().Select(t => t.Name).Should().BeEquivalentTo(
            "lootbox_types", "lootbox_type_grade_weights", "lootbox_pool_entries", "lootbox_enchant_rolls", "lootbox_special_entries",
            "lootbox_spawn_areas", "lootbox_spawn_area_types", "lootbox_configurations", "lootbox_spawns", "lootbox_claims");

        ops.OfType<CreateIndexOperation>().Where(i => i.IsUnique).Select(i => (i.Table, string.Join(",", i.Columns))).Should().BeEquivalentTo(new[]
        {
            ("lootbox_types", "CategoryId"),
            ("lootbox_spawn_areas", "Name"),
            ("lootbox_spawns", "Token"),
            ("lootbox_claims", "LootboxSpawnId"),
            ("lootbox_claims", "ItemInstanceId"),
            ("lootbox_claims", "IdempotencyKey"),
        });
        ops.OfType<CreateIndexOperation>().Select(i => (i.Table, string.Join(",", i.Columns))).Should().Contain(new[]
        {
            ("lootbox_spawns", "Status,ExpiresAt"),
            ("lootbox_spawns", "SpawnAreaId,Status"),
            ("lootbox_claims", "UserId,ClaimedAt"),
        });
    }
}

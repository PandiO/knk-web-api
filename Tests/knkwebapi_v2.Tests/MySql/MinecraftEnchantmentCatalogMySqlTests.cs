using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-250 on a real MySQL: the migration keeps existing enchantment refs (as Unverified), and a schema 2
/// snapshot merges notation duplicates under the case-insensitive unique key index, repoints definitions and
/// links the seeded custom definition, in one transaction.
/// </summary>
[Trait("Category", "requires-mysql")]
public class MinecraftEnchantmentCatalogMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string MigrationBefore = "20261022182224_MinecraftCatalogConstraints";

    private readonly MySqlTestDatabase _db;

    public MinecraftEnchantmentCatalogMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private sealed class NoIcons : IMinecraftIconResolver
    {
        public Task<IReadOnlyDictionary<string, MinecraftIconResult>> ResolveAsync(
            string? minecraftVersion, IReadOnlyCollection<MinecraftIconRequest> entries, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, MinecraftIconResult>>(new Dictionary<string, MinecraftIconResult>());
    }

    [MySqlFact]
    public async Task Migration_KeepsRowsUnverified_ThenAScanMergesAndLinks()
    {
        await using (var context = _db.NewContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(MigrationBefore);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO minecraftenchantmentrefs (Id, NamespaceKey, IsCustom) VALUES (1, 'Minecraft:Sharpness', 0), (2, 'sharpness', 0), (3, 'chaos', 0)");
            await migrator.MigrateAsync();
        }

        int userId;
        await using (var context = _db.NewContext())
        {
            Assert.All(await context.MinecraftEnchantmentRefs.ToListAsync(), r => Assert.Equal(MinecraftCatalogStatus.Unverified, r.CatalogStatus));

            var user = new User { Username = "e" + Guid.NewGuid().ToString("N")[..12] };
            context.Users.Add(user);
            context.EnchantmentDefinitions.AddRange(
                new EnchantmentDefinition { Key = "minecraft:sharpness", DisplayName = "Sharpness", MaxLevel = 5, MinecraftEnchantmentRefId = 2 },
                new EnchantmentDefinition { Key = "chaos", DisplayName = "Chaos", IsCustom = true, MaxLevel = 1 });
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        var entries = new[] { new MinecraftCatalogEntryDto { Key = "minecraft:enchanted_book", IsItem = true, MaxStackSize = 1 } };
        var enchantments = new[]
        {
            new MinecraftCatalogEnchantmentDto { Key = "minecraft:sharpness", MaxLevel = 5, DisplayName = "Sharpness" },
            new MinecraftCatalogEnchantmentDto { Key = "knk:chaos", IsCustom = true, MaxLevel = 1, DisplayName = "Chaos", Category = "SUPPORT" },
        };
        MinecraftCatalogScanDto result;
        await using (var context = _db.NewContext())
        {
            var service = new MinecraftCatalogScanService(context, new NoIcons(), NullLogger<MinecraftCatalogScanService>.Instance);
            var scanId = (await service.RequestScanAsync(userId)).Id;
            result = await service.IngestAsync(scanId, new MinecraftCatalogSnapshotDto
            {
                SchemaVersion = 2, MinecraftVersion = "26.3", Entries = entries.ToList(), Enchantments = enchantments.ToList(),
                ContentHash = MinecraftCatalogSnapshotHash.Compute(entries, enchantments),
            });
        }

        Assert.Equal("Completed", result.Status);
        Assert.Equal((1, 1, 0), (result.EnchantmentsMerged, result.EnchantmentDefinitionsLinked, result.EnchantmentDefinitionsCreated));
        await using var check = _db.NewContext();
        var refs = await check.MinecraftEnchantmentRefs.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(new[] { (1, "minecraft:sharpness"), (3, "knk:chaos") }, refs.Select(r => (r.Id, r.NamespaceKey)));
        Assert.All(refs, r => Assert.Equal(MinecraftCatalogStatus.Active, r.CatalogStatus));
        var definitions = await check.EnchantmentDefinitions.ToDictionaryAsync(d => d.Key, d => d.MinecraftEnchantmentRefId);
        Assert.Equal(1, definitions["minecraft:sharpness"]);
        Assert.Equal(3, definitions["chaos"]);
    }
}

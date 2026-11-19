using knkwebapi_v2.Models;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static knkwebapi_v2.Tests.Services.ItemIdentity.ItemIdentityTestDb;

namespace knkwebapi_v2.Tests.Services.ItemIdentity;

/// <summary>
/// Item identity (#141): a catalogue merge repoints every reference, the vanilla match included, but the match is
/// unique per material, so after the merge exactly one blueprint keeps it.
/// </summary>
public class VanillaMatchMergeTests
{
    private readonly ItemIdentityTestDb _db = new();

    private static MinecraftCatalogMaintenanceService Maintenance(knkwebapi_v2.Properties.KnKDbContext context) =>
        new(context, NullLogger<MinecraftCatalogMaintenanceService>.Instance);

    [Fact]
    public async Task Merge_WhenBothMaterialsHaveAMatch_TheTargetsBlueprintKeepsIt()
    {
        await _db.SeedAsync(Item(1, "minecraft:oak_log"), Item(2, "oak_log"),
            new ItemBlueprint { Id = 10, Name = "Oak Log", VanillaMaterialMatchRefId = 1 },
            new ItemBlueprint { Id = 11, Name = "Oak Log (old key)", VanillaMaterialMatchRefId = 2, IconMaterialRefId = 2 });
        await using (var context = _db.NewContext())
        {
            var result = await Maintenance(context).MergeAsync(2, 1);
            Assert.False(result.Repointed.ContainsKey("ItemBlueprint.VanillaMaterialMatchRefId"));
            Assert.Equal(1, result.Repointed["ItemBlueprint.IconMaterialRefId"]);
        }

        await using var check = _db.NewContext();
        Assert.Equal(1, (await check.ItemBlueprints.SingleAsync(b => b.Id == 10)).VanillaMaterialMatchRefId);
        var old = await check.ItemBlueprints.SingleAsync(b => b.Id == 11);
        Assert.Equal(((int?)null, (int?)1), (old.VanillaMaterialMatchRefId, old.IconMaterialRefId));
    }

    [Fact]
    public async Task Merge_WhenOnlyTheSourceHasAMatch_ItMovesToTheTarget()
    {
        await _db.SeedAsync(Item(1, "minecraft:oak_log"), Item(2, "oak_log"),
            new ItemBlueprint { Id = 11, Name = "Oak Log", VanillaMaterialMatchRefId = 2 });
        await using (var context = _db.NewContext())
        {
            var result = await Maintenance(context).MergeAsync(2, 1);
            Assert.Equal(1, result.Repointed["ItemBlueprint.VanillaMaterialMatchRefId"]);
        }

        await using var check = _db.NewContext();
        Assert.Equal(1, (await check.ItemBlueprints.SingleAsync()).VanillaMaterialMatchRefId);
    }
}

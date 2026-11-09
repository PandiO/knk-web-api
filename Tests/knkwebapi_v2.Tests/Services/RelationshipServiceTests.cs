using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#188: Select / Unlink / Add change only the link (a column, a link-table row or a link entity),
/// never delete the related record, refuse cycles and required parents, and ask before moving a child.
/// </summary>
public class RelationshipServiceTests
{
    private static KnKDbContext NewDb() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase("relationships-" + Guid.NewGuid()).Options);

    private static RelationshipService Service(KnKDbContext db) => new(db);

    private static async Task<RelationshipException> Refused(Func<Task> change) =>
        await Assert.ThrowsAsync<RelationshipException>(change);

    // ===== catalog =====

    [Fact]
    public void Catalog_PinsTheReviewedRelationships_AndHowTheyAreStored()
    {
        using var db = NewDb();
        var supported = DashboardAccess.All
            .SelectMany(t => RelationshipCatalog.For(db.Model, t.TypeName))
            .Where(r => r.CanLink || r.CanUnlink)
            .Select(r => $"{r.OwnerType}.{r.Property} {r.Storage} {r.RelatedType} link={r.CanLink} unlink={r.CanUnlink}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[]
        {
            "Category.ChildCategories Children Category link=True unlink=True",
            "Category.IconMaterialRef ForeignKey MinecraftMaterialRef link=True unlink=False",
            "Category.ParentCategory ForeignKey Category link=True unlink=True",
            "Category.Tags LinkRow Tag link=True unlink=True",
            "District.Streets Many Street link=True unlink=True",
            "ItemBlueprint.Category ForeignKey Category link=True unlink=True",
            "ItemBlueprint.Grade ForeignKey Grade link=True unlink=True",
            "ItemBlueprint.IconMaterial ForeignKey MinecraftMaterialRef link=True unlink=False",
            "ItemBlueprint.Tags LinkRow Tag link=True unlink=True",
            "Street.Districts Many District link=True unlink=True",
            "Street.Town Many Town link=True unlink=True",
            "Town.Streets Many Street link=True unlink=True",
        }, supported);
        // Every reviewed pair exists in the model (a renamed navigation would silently drop out otherwise).
        Assert.Equal(RelationshipCatalog.Supported.Count, supported.Count);
    }

    [Fact]
    public void RequiredParents_AndLinkEntitiesWithSettings_GoThroughTheEditPage_WithAReason()
    {
        using var db = NewDb();

        var districts = RelationshipCatalog.Find(db.Model, "Town", "districts")!;
        Assert.Equal(RelationshipStorage.Children, districts.Storage);
        Assert.False(districts.CanUnlink);
        Assert.Equal("A District always belongs to one Town: delete it, or move it on its own Edit page.", districts.UnlinkReason);
        Assert.Contains("create a new one here", districts.LinkReason);

        var town = RelationshipCatalog.Find(db.Model, "District", "Town")!;
        Assert.False(town.CanLink);
        Assert.False(town.CanUnlink);
        Assert.Equal("A District always has a Town: change it on the Edit page.", town.UnlinkReason);

        // A default enchantment stores a level: a record of its own, not a link row.
        var enchantments = RelationshipCatalog.Find(db.Model, "ItemBlueprint", "DefaultEnchantments")!;
        Assert.Equal(RelationshipStorage.Children, enchantments.Storage);
        Assert.False(enchantments.CanLink);

        // A town's location decides its world: Edit page only.
        var location = RelationshipCatalog.Find(db.Model, "Town", "Location")!;
        Assert.False(location.CanLink);
        Assert.Equal(RelationshipCatalog.EditPageReason, location.LinkReason);
    }

    [Fact]
    public void PlayersArea_NeverChangesHere_AndChildrenNeedTheRelatedTypesWriteNodeToo()
    {
        using var db = NewDb();
        var parent = RelationshipCatalog.Find(db.Model, "PermissionGroup", "ParentGroup")!;
        Assert.False(parent.CanLink);
        Assert.False(parent.CanUnlink);

        // The grant row's own column would change: the group's and the grant's write nodes.
        var grants = RelationshipCatalog.Find(db.Model, "User", "Grants")!;
        Assert.Equal(new[] { StaffPermissions.ManageUsers, StaffPermissions.UserPermissions }, grants.Nodes);

        Assert.Equal(new[] { StaffPermissions.ManageContent }, RelationshipCatalog.Find(db.Model, "Category", "childCategories")!.Nodes);
        Assert.Equal(new[] { StaffPermissions.ManageWorld }, RelationshipCatalog.Find(db.Model, "Town", "streets")!.Nodes);
    }

    [Fact]
    public void Metadata_DescribesLinkRows_WithTheOtherSidesIdField()
    {
        using var db = NewDb();
        var tags = RelationshipCatalog.Find(db.Model, "ItemBlueprint", "Tags")!.ToDto();

        Assert.Equal("tags", tags.Property);
        Assert.Equal("Tag", tags.RelatedType);
        Assert.Equal("collection", tags.Kind);
        Assert.Equal("linkRow", tags.Storage);
        Assert.Equal("tagId", tags.RelatedIdField);
        Assert.Equal("tag", tags.RelatedNavigation);
        Assert.Null(tags.LinkReason);

        var icon = RelationshipCatalog.Find(db.Model, "Category", "IconMaterialRef")!.ToDto();
        Assert.Equal("reference", icon.Kind);
        Assert.True(icon.CanLink);
        Assert.Equal(RelationshipCatalog.IconRequiredReason, icon.UnlinkReason);
    }

    [Fact]
    public void MetadataService_ListsTheRelationships()
    {
        var services = new ServiceCollection();
        var name = "relationships-meta-" + Guid.NewGuid();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        using var provider = services.BuildServiceProvider();
        var metadata = new MetadataService(provider.GetRequiredService<IServiceScopeFactory>());

        var category = metadata.GetEntityMetadata("Category")!;

        Assert.Contains(category.Relationships, r => r.Property == "parentCategory" && r.CanLink && r.CanUnlink);
        Assert.Contains(category.Relationships, r => r.Property == "tags" && r.Storage == "linkRow");
    }

    // ===== reference (a column on this entity) =====

    [Fact]
    public async Task SelectAndUnlink_AParent_ChangeOnlyTheColumn()
    {
        using var db = NewDb();
        db.Categories.AddRange(new Category { Id = 1, Name = "Weapons" }, new Category { Id = 2, Name = "Swords" });
        await db.SaveChangesAsync();
        var service = Service(db);

        var linked = await service.LinkAsync("Category", 2, "parentCategory", 1, move: false);
        Assert.True(linked.Changed);
        Assert.Equal(1, (await db.Categories.FindAsync(2))!.ParentCategoryId);

        var again = await service.LinkAsync("Category", 2, "ParentCategory", 1, move: false);
        Assert.False(again.Changed);

        var unlinked = await service.UnlinkAsync("Category", 2, "parentCategory", null);
        Assert.True(unlinked.Changed);
        Assert.Equal(1, unlinked.PreviousId);
        Assert.Null((await db.Categories.FindAsync(2))!.ParentCategoryId);
        Assert.Equal(2, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task Select_RefusesACycle_AndItself()
    {
        using var db = NewDb();
        db.Categories.AddRange(
            new Category { Id = 1, Name = "Weapons" },
            new Category { Id = 2, Name = "Swords", ParentCategoryId = 1 },
            new Category { Id = 3, Name = "Long swords", ParentCategoryId = 2 });
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal("Cycle", (await Refused(() => service.LinkAsync("Category", 1, "parentCategory", 3, false))).Code);
        Assert.Equal("Cycle", (await Refused(() => service.LinkAsync("Category", 1, "parentCategory", 1, false))).Code);
        Assert.Equal("Cycle", (await Refused(() => service.LinkAsync("Category", 3, "childCategories", 1, true))).Code);
        Assert.Null((await db.Categories.FindAsync(1))!.ParentCategoryId);
    }

    [Fact]
    public async Task Unlink_AnIcon_IsRefused_SelectAnotherIsFine()
    {
        using var db = NewDb();
        db.MinecraftMaterialRefs.AddRange(
            new MinecraftMaterialRef { Id = 5, NamespaceKey = "minecraft:iron_sword", Category = "Item" },
            new MinecraftMaterialRef { Id = 6, NamespaceKey = "minecraft:gold_sword", Category = "Item" });
        db.Categories.Add(new Category { Id = 1, Name = "Weapons", IconMaterialRefId = 5 });
        await db.SaveChangesAsync();
        var service = Service(db);

        var refused = await Refused(() => service.UnlinkAsync("Category", 1, "iconMaterialRef", null));
        Assert.Equal(400, refused.Status);
        Assert.Equal(RelationshipCatalog.IconRequiredReason, refused.Message);

        await service.LinkAsync("Category", 1, "iconMaterialRef", 6, false);
        Assert.Equal(6, (await db.Categories.FindAsync(1))!.IconMaterialRefId);
    }

    // ===== children (the related row's column) =====

    [Fact]
    public async Task Add_AChildOfAnotherParent_AsksFirst_ThenMovesIt_AndUnlinkKeepsTheChild()
    {
        using var db = NewDb();
        db.Categories.AddRange(
            new Category { Id = 1, Name = "Weapons" },
            new Category { Id = 2, Name = "Tools" },
            new Category { Id = 3, Name = "Axes", ParentCategoryId = 2 });
        await db.SaveChangesAsync();
        var service = Service(db);

        var asked = await Refused(() => service.LinkAsync("Category", 1, "childCategories", 3, move: false));
        Assert.Equal(409, asked.Status);
        Assert.Equal("AlreadyLinked", asked.Code);
        Assert.Equal(2, asked.CurrentId);

        var moved = await service.LinkAsync("Category", 1, "childCategories", 3, move: true);
        Assert.Equal(2, moved.PreviousId);
        Assert.Equal(1, (await db.Categories.FindAsync(3))!.ParentCategoryId);

        await service.UnlinkAsync("Category", 1, "childCategories", 3);
        var axes = await db.Categories.FindAsync(3);
        Assert.NotNull(axes);
        Assert.Null(axes!.ParentCategoryId);

        Assert.Equal("NotLinked", (await Refused(() => service.UnlinkAsync("Category", 1, "childCategories", 3))).Code);
        Assert.Equal("InvalidRelatedId", (await Refused(() => service.UnlinkAsync("Category", 1, "childCategories", null))).Code);
    }

    [Fact]
    public async Task RequiredChildren_CantBeUnlinked()
    {
        using var db = NewDb();
        var refused = await Refused(() => Service(db).UnlinkAsync("Town", 1, "districts", 4));
        Assert.Equal(400, refused.Status);
        Assert.Equal("RelationshipNotEditable", refused.Code);
        Assert.StartsWith("A District always belongs to one Town", refused.Message);
    }

    // ===== link entity (only the two ids) =====

    [Fact]
    public async Task Tags_AddAndUnlink_TheLinkRowOnly_TheTagStays()
    {
        using var db = NewDb();
        db.Tags.AddRange(new Tag { Id = 7, Name = "Rare" }, new Tag { Id = 8, Name = "Event" });
        db.ItemBlueprints.Add(new ItemBlueprint { Id = 3, Name = "Iron Sword" });
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.True((await service.LinkAsync("ItemBlueprint", 3, "tags", 7, false)).Changed);
        Assert.False((await service.LinkAsync("ItemBlueprint", 3, "tags", 7, false)).Changed);
        await service.LinkAsync("ItemBlueprint", 3, "tags", 8, false);
        Assert.Equal(2, await db.Set<ItemBlueprintTag>().CountAsync(t => t.ItemBlueprintId == 3));

        await service.UnlinkAsync("ItemBlueprint", 3, "tags", 7);
        Assert.Equal(new[] { 8 }, await db.Set<ItemBlueprintTag>().Where(t => t.ItemBlueprintId == 3).Select(t => t.TagId).ToListAsync());
        Assert.Equal(2, await db.Tags.CountAsync());

        Assert.Equal("NotLinked", (await Refused(() => service.UnlinkAsync("ItemBlueprint", 3, "tags", 7))).Code);
        Assert.Equal("RelatedNotFound", (await Refused(() => service.LinkAsync("ItemBlueprint", 3, "tags", 99, false))).Code);
    }

    // ===== hidden link table =====

    [Fact]
    public async Task Streets_AddAndUnlink_OnATown_TheStreetStays()
    {
        using var db = NewDb();
        db.Streets.AddRange(new Street { Id = 11, Name = "Main Street" }, new Street { Id = 12, Name = "Harbour Road" });
        db.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "", WgRegionId = "town_cinix" });
        await db.SaveChangesAsync();
        var service = Service(db);

        await service.LinkAsync("Town", 1, "streets", 11, false);
        await service.LinkAsync("Town", 1, "streets", 12, false);
        Assert.False((await service.LinkAsync("Town", 1, "streets", 12, false)).Changed);
        Assert.Equal(2, (await db.Towns.Include(t => t.Streets).SingleAsync(t => t.Id == 1)).Streets.Count);

        await service.UnlinkAsync("Town", 1, "streets", 11);
        db.ChangeTracker.Clear();
        var town = await db.Towns.Include(t => t.Streets).SingleAsync(t => t.Id == 1);
        Assert.Equal(new[] { 12 }, town.Streets.Select(s => s.Id));
        Assert.Equal(2, await db.Streets.CountAsync());
    }

    // ===== not found =====

    [Fact]
    public async Task UnknownTypes_Relationships_Records_And_RelatedRecords_Are404()
    {
        using var db = NewDb();
        db.Categories.Add(new Category { Id = 1, Name = "Weapons" });
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal("UnknownEntityType", (await Refused(() => service.LinkAsync("Nope", 1, "x", 2, false))).Code);
        Assert.Equal("UnknownRelationship", (await Refused(() => service.LinkAsync("Category", 1, "nope", 2, false))).Code);
        Assert.Equal("NotFound", (await Refused(() => service.LinkAsync("Category", 9, "parentCategory", 1, false))).Code);
        var related = await Refused(() => service.LinkAsync("Category", 1, "parentCategory", 9, false));
        Assert.Equal(404, related.Status);
        Assert.Equal("RelatedNotFound", related.Code);
        Assert.Equal("InvalidRelatedId", (await Refused(() => service.LinkAsync("Category", 1, "parentCategory", 0, false))).Code);
    }
}

using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Lootboxes Phase 1 step 1 (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md): the minimal ItemInstance. BuildAsync
/// returns an unsaved instance (OwnerCount 1, flags false) the caller's transaction saves; the no-cascade rule
/// keeps a blueprint that instances were minted from.
/// </summary>
public class ItemInstanceServiceTests
{
    private readonly string _dbName = $"ItemInstanceServiceTestDb_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<ItemInstanceMappingProfile>();
        cfg.AddProfile<ItemBlueprintMappingProfile>();
        cfg.AddProfile<GradeMappingProfile>();
    }).CreateMapper();

    private async Task<(ItemBlueprint Sword, ItemBlueprint Bread, Grade Legendary, EnchantmentDefinition Sharpness, EnchantmentDefinition Poison, User Owner)> SeedAsync()
    {
        await using var db = NewContext();
        var legendary = new Grade { Name = "Legendary", Stars = 5, DropChance = 15m, EnchantLevelCapDivisor = 1 };
        var sword = new ItemBlueprint { Name = "Golemheart Sword", DefaultDisplayName = "&aGolemheart Sword", MaxStackSize = 1, Grade = legendary };
        var bread = new ItemBlueprint { Name = "Bread", DefaultDisplayName = "&9Bread", MaxStackSize = 64 };
        var sharpness = new EnchantmentDefinition { Key = "minecraft:sharpness", DisplayName = "Sharpness", MaxLevel = 5 };
        var poison = new EnchantmentDefinition { Key = "poison", DisplayName = "Poison", MaxLevel = 3, IsCustom = true };
        var owner = new User { Username = "alice" };
        db.AddRange(legendary, sword, bread, sharpness, poison, owner);
        await db.SaveChangesAsync();
        return (sword, bread, legendary, sharpness, poison, owner);
    }

    private ItemInstanceService Service(KnKDbContext db) => new(new ItemInstanceRepository(db), Mapper());

    [Fact]
    public async Task BuildAsync_ReturnsAnUnsavedInstanceWithItsEnchantments()
    {
        var (sword, _, legendary, sharpness, poison, owner) = await SeedAsync();
        await using var db = NewContext();

        var instance = await Service(db).BuildAsync(sword, legendary.Id, owner.Id, ItemInstanceOrigin.Lootbox,
            new[] { new ItemInstanceEnchantmentSpec(sharpness.Id, 5), new ItemInstanceEnchantmentSpec(poison.Id, 2) }, "claim-1");

        instance.Id.Should().Be(0);
        db.ChangeTracker.Entries().Should().BeEmpty("the caller's transaction adds and saves it");
        (instance.ItemBlueprintId, instance.GradeId, instance.OwnerUserId, instance.Origin, instance.OriginRef)
            .Should().Be((sword.Id, legendary.Id, owner.Id, ItemInstanceOrigin.Lootbox, "claim-1"));
        instance.OwnerCount.Should().Be(1);
        instance.IsSoulbound.Should().BeFalse();
        instance.IsGhosted.Should().BeFalse();
        instance.CustomDisplayName.Should().BeNull();
        instance.Enchantments.Select(e => (e.EnchantmentDefinitionId, e.Level))
            .Should().BeEquivalentTo(new[] { (sharpness.Id, 5), (poison.Id, 2) });
    }

    [Fact]
    public async Task BuildAsync_ThenSave_RoundTripsThroughGetAsync()
    {
        var (sword, _, legendary, sharpness, _, owner) = await SeedAsync();
        long id;
        await using (var db = NewContext())
        {
            var instance = await Service(db).BuildAsync(sword, legendary.Id, owner.Id, ItemInstanceOrigin.Lootbox,
                new[] { new ItemInstanceEnchantmentSpec(sharpness.Id, 3) });
            db.ItemInstances.Add(instance);
            await db.SaveChangesAsync();
            id = instance.Id;
        }

        await using var read = NewContext();
        var dto = await Service(read).GetAsync(id);

        dto.Should().NotBeNull();
        dto!.ItemBlueprint!.Name.Should().Be("Golemheart Sword");
        dto.Grade!.Stars.Should().Be(5);
        dto.OwnerUsername.Should().Be("alice");
        dto.Origin.Should().Be("Lootbox");
        dto.OwnerCount.Should().Be(1);
        dto.Enchantments.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { EnchantmentDefinitionId = sharpness.Id, Key = "minecraft:sharpness", IsCustom = false, Level = 3 });
        (await Service(read).GetAsync(id + 1000)).Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_DuplicateDefinition_KeepsTheHigherLevel()
    {
        var (sword, _, _, sharpness, _, _) = await SeedAsync();
        await using var db = NewContext();

        var instance = await Service(db).BuildAsync(sword, null, null, ItemInstanceOrigin.Admin,
            new[] { new ItemInstanceEnchantmentSpec(sharpness.Id, 2), new ItemInstanceEnchantmentSpec(sharpness.Id, 4) });

        instance.Enchantments.Should().ContainSingle().Which.Level.Should().Be(4);
    }

    [Fact]
    public async Task BuildAsync_StackableBlueprint_IsRefused()
    {
        var (_, bread, _, _, _, _) = await SeedAsync();
        await using var db = NewContext();

        var act = () => Service(db).BuildAsync(bread, null, null, ItemInstanceOrigin.Lootbox, Array.Empty<ItemInstanceEnchantmentSpec>());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stackable*");
    }

    [Fact]
    public async Task BuildAsync_UnknownDefinitionOrLevelBelowOne_IsRefused()
    {
        var (sword, _, _, sharpness, _, _) = await SeedAsync();
        await using var db = NewContext();

        var unknown = () => Service(db).BuildAsync(sword, null, null, ItemInstanceOrigin.Lootbox, new[] { new ItemInstanceEnchantmentSpec(9999, 1) });
        var zero = () => Service(db).BuildAsync(sword, null, null, ItemInstanceOrigin.Lootbox, new[] { new ItemInstanceEnchantmentSpec(sharpness.Id, 0) });

        await unknown.Should().ThrowAsync<ArgumentException>().WithMessage("*9999*");
        await zero.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BlueprintDelete_IsRefusedWhileAnInstanceReferencesIt()
    {
        var (sword, bread, _, _, _, _) = await SeedAsync();
        await using (var db = NewContext())
        {
            db.ItemInstances.Add(new ItemInstance { ItemBlueprintId = sword.Id, Origin = ItemInstanceOrigin.Lootbox });
            await db.SaveChangesAsync();
        }

        await using var context = NewContext();
        var repo = new ItemBlueprintRepository(context);
        (await repo.FindDeleteBlockerAsync(sword.Id)).Should().Contain("1 item instance");
        (await repo.FindDeleteBlockerAsync(bread.Id)).Should().BeNull();

        var service = new ItemBlueprintService(repo,
            Mock.Of<IMinecraftMaterialRefRepository>(), Mock.Of<IMinecraftMaterialCatalogService>(),
            Mock.Of<IEnchantmentDefinitionRepository>(), Mock.Of<ICategoryRepository>(), Mock.Of<IGradeRepository>(),
            Mock.Of<ITagRepository>(), Mock.Of<IDomainRepository>(), Mapper());
        var act = () => service.DeleteAsync(sword.Id);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't be deleted*");
        (await context.ItemBlueprints.AnyAsync(b => b.Id == sword.Id)).Should().BeTrue();

        await service.DeleteAsync(bread.Id);
        (await context.ItemBlueprints.AnyAsync(b => b.Id == bread.Id)).Should().BeFalse();
    }

    [Fact]
    public void Model_UsesRestrictToTheCatalogAndCascadesOnlyToItsOwnEnchantments()
    {
        using var db = NewContext();
        var instance = db.Model.FindEntityType(typeof(ItemInstance))!;
        var enchantment = db.Model.FindEntityType(typeof(ItemInstanceEnchantment))!;

        DeleteBehavior On(Microsoft.EntityFrameworkCore.Metadata.IEntityType e, string fk) =>
            e.GetForeignKeys().Single(k => k.Properties.Single().Name == fk).DeleteBehavior;

        On(instance, nameof(ItemInstance.ItemBlueprintId)).Should().Be(DeleteBehavior.Restrict);
        On(instance, nameof(ItemInstance.GradeId)).Should().Be(DeleteBehavior.Restrict);
        On(instance, nameof(ItemInstance.OwnerUserId)).Should().Be(DeleteBehavior.SetNull);
        On(enchantment, nameof(ItemInstanceEnchantment.ItemInstanceId)).Should().Be(DeleteBehavior.Cascade);
        On(enchantment, nameof(ItemInstanceEnchantment.EnchantmentDefinitionId)).Should().Be(DeleteBehavior.Restrict);
        instance.FindProperty(nameof(ItemInstance.Id))!.ClrType.Should().Be(typeof(long));
    }
}

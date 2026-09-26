using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Lootbox;

/// <summary>
/// Lootboxes Phase 1 (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md "LootboxTypeServiceTests"): type validation and the
/// one-type-per-category rule, child rows replaced on update, delete guards, the odds preview on the real seeded
/// catalog (it is the engine's own numbers), and the special-entry, spawn-area and configuration services. Runs on
/// EF InMemory with the real repositories and every item seed.
/// </summary>
public class LootboxConfigServicesTests
{
    private readonly string _dbName = $"LootboxConfigTestDb_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<LootboxMappingProfile>();
        cfg.AddProfile<ItemBlueprintMappingProfile>();
        cfg.AddProfile<GradeMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private async Task SeedAllAsync()
    {
        // The real enchantment catalog (Data/, copied to the test output) gives vanilla definitions their true max
        // levels, as at startup.
        var enchantmentCatalog = new MinecraftEnchantmentCatalogService(NullLogger<MinecraftEnchantmentCatalogService>.Instance);
        await using var context = NewContext();
        await AbilityDefinition.SeedCanonicalAsync(context);
        await ItemBlueprintV1Seed.SeedCanonicalAsync(context, enchantmentCatalog: enchantmentCatalog);
        await EnchantBookSeed.SeedCanonicalAsync(context);
        await LootboxSeed.SeedCanonicalAsync(context, enchantmentCatalog: enchantmentCatalog);
    }

    private static LootboxTypeService TypeService(KnKDbContext db) => new(new LootboxTypeRepository(db), Mapper());

    private async Task<int> TypeIdAsync(string categoryName)
    {
        await using var db = NewContext();
        return await db.LootboxTypes.Where(t => t.Category.Name == categoryName).Select(t => t.Id).SingleAsync();
    }

    private async Task<int> CategoryIdAsync(string name)
    {
        await using var db = NewContext();
        return await db.Categories.Where(c => c.Name == name).Select(c => c.Id).SingleAsync();
    }

    [Fact]
    public void MappingProfile_IsValid()
    {
        // Only this profile plus the nav maps it relies on (the ItemBlueprint profile's own create maps aren't
        // complete, so the whole app configuration can't be asserted).
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<LootboxMappingProfile>();
            cfg.CreateMap<Category, CategoryNavDto>();
            cfg.CreateMap<ItemBlueprint, ItemBlueprintNavDto>();
            cfg.CreateMap<MinecraftMaterialRef, MinecraftMaterialRefNavDto>();
            cfg.CreateMap<Grade, GradeNavDto>();
        });

        config.AssertConfigurationIsValid();
    }

    // ===== Odds preview =====

    [Fact]
    public async Task Odds_WeaponsFiveStar_OnTheSeededCatalog_MatchesTheAcceptanceNumbers()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");

        await using var db = NewContext();
        var odds = await TypeService(db).GetOddsAsync(weapons, 5);

        odds.Should().NotBeNull();
        odds!.ItemGrades.Select(g => (g.Stars, g.Percent)).Should().Equal((3, 50.0), (4, 31.25), (5, 18.75));
        odds.Specials.Select(s => (s.Name, s.ChancePercent)).Should().Equal(
            ("Flaming Samurai", 0.05), ("Skull splitter", 0.2), ("Lavonian Bow", 0.2));
        odds.Specials[0].Percent.Should().Be(0.05);
        odds.Items.Select(i => i.Name).Should().Contain("Golemheart Sword")
            .And.NotContain(new[] { "Skull splitter", "Lavonian Bow", "Flaming Samurai" }, "specials are out of the normal pool");
        odds.BoxGrades.Select(g => g.Stars).Should().Equal(1, 2, 3, 4, 5);
        odds.Enchantments.Single(e => e.Key == "minecraft:knockback").LevelsByGrade
            .Select(l => (l.Stars, l.MaxLevel)).Should().Equal((3, (int?)null), (4, 1), (5, 2));
    }

    [Fact]
    public async Task Odds_AreTheEnginesOwnNumbers_ForTheSameInput()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");

        await using var db = NewContext();
        var service = TypeService(db);
        var dto = await service.GetOddsAsync(weapons, 4);
        var engine = LootboxRollEngine.ComputeOdds((await service.BuildRollInputAsync(weapons))!, 4);

        dto!.Items.Select(i => (i.ItemBlueprintId, i.Percent))
            .Should().Equal(engine.Items.Select(i => (i.Item.BlueprintId, Math.Round(i.Probability * 100, 6))));
        dto.Specials.Should().BeEmpty("the seeded specials need a ★5 box");
        dto.NormalRollPercent.Should().Be(100);
    }

    [Fact]
    public async Task Odds_UnknownTypeIsNull_AndStarsOutsideOneToFiveAreRefused()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        await using var db = NewContext();
        var service = TypeService(db);

        (await service.GetOddsAsync(99_999, 5)).Should().BeNull();
        await service.Invoking(s => s.GetOddsAsync(weapons, 6)).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.GetOddsAsync(weapons, 0)).Should().ThrowAsync<ArgumentException>();
        (await service.GetOddsAsync(weapons, null))!.BoxStars.Should().Be(5, "defaults to the type's MaxBoxStars");
    }

    [Fact]
    public async Task RollInput_GivesBooksNoRolls_AndLeavesUngradedBooksOutOfThePool()
    {
        await SeedAllAsync();
        var books = await TypeIdAsync(EnchantBookSeed.CategoryName);
        await using (var db = NewContext())
        {
            // The seeded books are ungraded; give one a grade via a pool entry.
            var book = await db.ItemBlueprints.FirstAsync(b => b.Name == "Enchanted Book (Sharpness V)");
            var type = await db.LootboxTypes.FindAsync(books);
            db.LootboxPoolEntries.Add(new LootboxPoolEntry { LootboxTypeId = type!.Id, ItemBlueprintId = book.Id, GradeIdOverride = (await db.Grades.FirstAsync(g => g.Stars == 5)).Id });
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var input = await TypeService(read).BuildRollInputAsync(books);

        input!.Pool.Should().ContainSingle().Which.Should().Match<LootItem>(i => i.IsBook && i.Name == "Enchanted Book (Sharpness V)");
        LootboxRollEngine.CanRollEnchantments(input.Pool[0]).Should().BeFalse();
    }

    // ===== Type CRUD + validation =====

    private static LootboxTypeDto NewType(int categoryId) => new() { Name = "Test Lootbox", CategoryId = categoryId };

    [Fact]
    public async Task Create_SecondTypeForACategory_IsCategoryTaken()
    {
        await SeedAllAsync();
        var weaponsCategory = await CategoryIdAsync("Weapons");
        await using var db = NewContext();

        var act = () => TypeService(db).CreateAsync(NewType(weaponsCategory));

        (await act.Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("CategoryTaken");
    }

    public static IEnumerable<object[]> InvalidTypes()
    {
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.MaxBoxStars = 6) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.MinBoxStars = 0) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => { t.MinBoxStars = 4; t.MaxBoxStars = 3; }) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.ItemStarSpread = 10) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.ItemStarSpread = -1) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.SpawnWeight = -1) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.MaxClaimsPerPlayerPerDay = 0) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.Name = " ") };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.CategoryId = 99_999) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.DisplayMaterialRefId = 99_999) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.GradeWeights.Add(new LootboxTypeGradeWeightDto { GradeId = 99_999, Weight = 1 })) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.PoolEntries.Add(new LootboxPoolEntryDto { ItemBlueprintId = 99_999 })) };
        yield return new object[] { (Action<LootboxTypeDto>)(t => t.EnchantRolls.Add(new LootboxEnchantRollDto { EnchantmentDefinitionId = 99_999, ChancePercent = 10, MinLevel = 1, MaxLevel = 1 })) };
    }

    [Theory]
    [MemberData(nameof(InvalidTypes))]
    public async Task Create_InvalidType_IsRefused(Action<LootboxTypeDto> breakIt)
    {
        await SeedAllAsync();
        var category = await CategoryIdAsync("Weapons");
        await using (var db = NewContext())
        {
            // Free the Weapons category so only the broken field can fail.
            db.LootboxSpecialEntries.RemoveRange(db.LootboxSpecialEntries);
            db.LootboxTypes.RemoveRange(db.LootboxTypes.Where(t => t.CategoryId == category));
            await db.SaveChangesAsync();
        }

        var dto = NewType(category);
        breakIt(dto);
        await using var context = NewContext();

        await TypeService(context).Invoking(s => s.CreateAsync(dto)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnchantRoll_LevelsMustFitTheDefinition_AndChanceMustBeAPercent()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        int knockbackId;
        await using (var db = NewContext())
            knockbackId = await db.EnchantmentDefinitions.Where(d => d.Key == "minecraft:knockback").Select(d => d.Id).SingleAsync();

        async Task UpdateWith(LootboxEnchantRollDto roll)
        {
            await using var db = NewContext();
            var service = TypeService(db);
            var dto = (await service.GetByIdAsync(weapons))!;
            dto.EnchantRolls = new List<LootboxEnchantRollDto> { roll };
            await service.UpdateAsync(weapons, dto);
        }

        await FluentActions.Invoking(() => UpdateWith(new() { EnchantmentDefinitionId = knockbackId, ChancePercent = 50, MinLevel = 1, MaxLevel = 3 }))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*<= 2*");
        await FluentActions.Invoking(() => UpdateWith(new() { EnchantmentDefinitionId = knockbackId, ChancePercent = 50, MinLevel = 2, MaxLevel = 1 }))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => UpdateWith(new() { EnchantmentDefinitionId = knockbackId, ChancePercent = 100.5m, MinLevel = 1, MaxLevel = 1 }))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => UpdateWith(new() { EnchantmentDefinitionId = knockbackId, ChancePercent = 50, MinLevel = 1, MaxLevel = 2, MinBoxStars = 6 }))
            .Should().ThrowAsync<ArgumentException>();
        await UpdateWith(new() { EnchantmentDefinitionId = knockbackId, ChancePercent = 50, MinLevel = 1, MaxLevel = 2 });
    }

    [Fact]
    public async Task Update_ReplacesTheChildRows()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        int bladedSteel, legendary;
        await using (var db = NewContext())
        {
            bladedSteel = await db.ItemBlueprints.Where(b => b.Name == "Bladed Steel Sword").Select(b => b.Id).SingleAsync();
            legendary = await db.Grades.Where(g => g.Stars == 5).Select(g => g.Id).SingleAsync();
        }

        await using (var db = NewContext())
        {
            var service = TypeService(db);
            var dto = (await service.GetByIdAsync(weapons))!;
            dto.EnchantRolls.Should().HaveCount(9, "the seeded Weapons profile");
            dto.Enabled = true;
            dto.EnchantRolls.RemoveAll(r => r.EnchantmentKey != "minecraft:sharpness");
            dto.GradeWeights.Add(new LootboxTypeGradeWeightDto { GradeId = legendary, Weight = 70m });
            dto.PoolEntries.Add(new LootboxPoolEntryDto { ItemBlueprintId = bladedSteel, Mode = "exclude" });
            await service.UpdateAsync(weapons, dto);
        }

        await using var read = NewContext();
        var saved = (await TypeService(read).GetByIdAsync(weapons))!;
        saved.Enabled.Should().BeTrue();
        saved.EnchantRolls.Should().ContainSingle().Which.EnchantmentKey.Should().Be("minecraft:sharpness");
        saved.GradeWeights.Should().ContainSingle().Which.Weight.Should().Be(70m);
        saved.PoolEntries.Should().ContainSingle().Which.Mode.Should().Be("Exclude");
        (await read.LootboxEnchantRolls.CountAsync(r => r.LootboxTypeId == weapons)).Should().Be(1, "the old rows are gone, not orphaned");

        var odds = await TypeService(read).GetOddsAsync(weapons, 5);
        odds!.Items.Select(i => i.Name).Should().NotContain("Bladed Steel Sword");
        odds.BoxGrades.Single(g => g.Stars == 5).Percent.Should().BeApproximately(70.0 / 265 * 100, 1e-4);
    }

    [Fact]
    public async Task Update_KeepingTheSameChildKeys_UpdatesThemInPlace()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        int steel, legendary;
        await using (var db = NewContext())
        {
            steel = await db.ItemBlueprints.Where(b => b.Name == "Steel Sword").Select(b => b.Id).SingleAsync();
            legendary = await db.Grades.Where(g => g.Stars == 5).Select(g => g.Id).SingleAsync();
        }

        foreach (var weight in new[] { 10m, 20m })
        {
            await using var db = NewContext();
            var service = TypeService(db);
            var dto = (await service.GetByIdAsync(weapons))!;
            dto.GradeWeights = new List<LootboxTypeGradeWeightDto> { new() { GradeId = legendary, Weight = weight } };
            dto.PoolEntries = new List<LootboxPoolEntryDto> { new() { ItemBlueprintId = steel, WeightOverride = weight } };
            await service.UpdateAsync(weapons, dto);
        }

        await using var read = NewContext();
        var saved = (await TypeService(read).GetByIdAsync(weapons))!;
        saved.GradeWeights.Should().ContainSingle().Which.Weight.Should().Be(20m);
        saved.PoolEntries.Should().ContainSingle().Which.WeightOverride.Should().Be(20m);
        saved.EnchantRolls.Should().HaveCount(9);
    }

    [Fact]
    public async Task SpecialEntry_Update_CanMoveItToAnotherBlueprintAndType()
    {
        await SeedAllAsync();
        var armor = await TypeIdAsync("Armor");
        int entryId, golemheartSword;
        await using (var db = NewContext())
        {
            entryId = await db.LootboxSpecialEntries.Where(s => s.ItemBlueprint.Name == "Skull splitter").Select(s => s.Id).SingleAsync();
            golemheartSword = await db.ItemBlueprints.Where(b => b.Name == "Golemheart Sword").Select(b => b.Id).SingleAsync();
        }

        await using (var db = NewContext())
        {
            var service = new LootboxSpecialEntryService(new LootboxSpecialEntryRepository(db), Mapper());
            var dto = (await service.GetByIdAsync(entryId))!;
            dto.ItemBlueprintId = golemheartSword;
            dto.LootboxTypeId = armor;
            dto.ChancePerMillion = 7;
            await service.UpdateAsync(entryId, dto);
        }

        await using var read = NewContext();
        var saved = await new LootboxSpecialEntryService(new LootboxSpecialEntryRepository(read), Mapper()).GetByIdAsync(entryId);
        (saved!.ItemBlueprint!.Name, saved.LootboxType!.Name, saved.ChancePerMillion).Should().Be(("Golemheart Sword", "Armor Lootbox", 7));
    }

    [Fact]
    public async Task Update_InvalidPoolMode_IsRefused()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        await using var db = NewContext();
        var service = TypeService(db);
        var dto = (await service.GetByIdAsync(weapons))!;
        dto.PoolEntries.Add(new LootboxPoolEntryDto { ItemBlueprintId = dto.Category!.Id, Mode = "Sometimes" });

        await service.Invoking(s => s.UpdateAsync(weapons, dto)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Delete_TypeWithSpecialEntries_IsInUse_OtherwiseDeleted()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        var food = await TypeIdAsync("Food");
        await using var db = NewContext();
        var service = TypeService(db);

        (await service.Invoking(s => s.DeleteAsync(weapons)).Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("InUse");
        await service.DeleteAsync(food);
        (await db.LootboxTypes.AnyAsync(t => t.Id == food)).Should().BeFalse();
        await service.Invoking(s => s.DeleteAsync(food)).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Search_FindsByNameOrCategory()
    {
        await SeedAllAsync();
        await using var db = NewContext();

        var result = await TypeService(db).SearchAsync(new PagedQueryDto { SearchTerm = "weap", PageSize = 50 });

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Weapons Lootbox");
    }

    [Fact]
    public async Task BlueprintInAPoolOrSpecialEntry_CantBeDeleted()
    {
        await SeedAllAsync();
        await using var db = NewContext();
        var repo = new ItemBlueprintRepository(db);
        var samurai = await db.ItemBlueprints.Where(b => b.Name == LootboxSeed.FlamingSamuraiName).Select(b => b.Id).SingleAsync();
        var steel = await db.ItemBlueprints.Where(b => b.Name == "Steel Sword").Select(b => b.Id).SingleAsync();

        (await repo.FindDeleteBlockerAsync(samurai)).Should().Contain("special");
        (await repo.FindDeleteBlockerAsync(steel)).Should().BeNull();

        db.LootboxPoolEntries.Add(new LootboxPoolEntry { LootboxTypeId = await TypeIdAsync("Armor"), ItemBlueprintId = steel });
        await db.SaveChangesAsync();
        (await repo.FindDeleteBlockerAsync(steel)).Should().Contain("pool");
    }

    // ===== Special entries =====

    [Fact]
    public async Task SpecialEntry_CreateTagsTheBlueprint_AndValidates()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        int golemheart;
        await using (var db = NewContext())
            golemheart = await db.ItemBlueprints.Where(b => b.Name == "Golemheart Sword").Select(b => b.Id).SingleAsync();

        await using (var db = NewContext())
        {
            var service = new LootboxSpecialEntryService(new LootboxSpecialEntryRepository(db), Mapper());
            await service.Invoking(s => s.CreateAsync(new LootboxSpecialEntryDto { ItemBlueprintId = golemheart, ChancePerMillion = 1_000_001 }))
                .Should().ThrowAsync<ArgumentException>();
            await service.Invoking(s => s.CreateAsync(new LootboxSpecialEntryDto { ItemBlueprintId = golemheart, ChancePerMillion = 10, MinBoxStars = 6 }))
                .Should().ThrowAsync<ArgumentException>();
            await service.Invoking(s => s.CreateAsync(new LootboxSpecialEntryDto { ItemBlueprintId = golemheart, LootboxTypeId = 99_999, ChancePerMillion = 10 }))
                .Should().ThrowAsync<ArgumentException>();

            var created = await service.CreateAsync(new LootboxSpecialEntryDto { ItemBlueprintId = golemheart, LootboxTypeId = weapons, ChancePerMillion = 100 });
            created.ItemBlueprint!.Name.Should().Be("Golemheart Sword");
            created.LootboxType!.Name.Should().Be("Weapons Lootbox");
        }

        await using var read = NewContext();
        (await read.Set<ItemBlueprintTag>().Include(t => t.Tag).CountAsync(t => t.ItemBlueprintId == golemheart && t.Tag.Name == LootboxSeed.SpecialTag))
            .Should().Be(1);
        var odds = await TypeService(read).GetOddsAsync(weapons, 5);
        odds!.Items.Select(i => i.Name).Should().NotContain("Golemheart Sword");
        odds.Specials.Select(s => s.Name).Should().Contain("Golemheart Sword");
    }

    // ===== Spawn areas =====

    private static LootboxSpawnAreaService AreaService(KnKDbContext db) => new(new LootboxSpawnAreaRepository(db), Mapper());

    private static LootboxSpawnAreaDto NewArea(string name = "spawn") => new() { Name = name, World = "world", WgRegionId = "lootbox_spawn" };

    [Fact]
    public async Task SpawnArea_CreateUsesTheDefaults_AndNamesAreUniqueIgnoringCase()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        await using var db = NewContext();
        var service = AreaService(db);

        var dto = NewArea();
        dto.ExcludedRegionIds = " plot_1, plot_2 ,,plot_1";
        dto.AllowedTypes.Add(new LootboxSpawnAreaTypeDto { LootboxTypeId = weapons });
        var created = await service.CreateAsync(dto);

        (created.MaxActive, created.SpawnIntervalSeconds, created.SpawnChancePercent, created.MinOnlinePlayers, created.MinDistanceFromPlayers, created.LifetimeMinutes)
            .Should().Be((3, 600, 100m, 3, 24, 30));
        created.ExcludedRegionIds.Should().Be("plot_1,plot_2");
        created.AllowedTypes.Should().ContainSingle().Which.LootboxType!.Name.Should().Be("Weapons Lootbox");

        (await service.Invoking(s => s.CreateAsync(NewArea("SPAWN"))).Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("NameTaken");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("a23456789012345678901234567890123")]
    [InlineData("dots.not.allowed")]
    public async Task SpawnArea_InvalidName_IsRefused(string name)
    {
        await using var db = NewContext();

        await AreaService(db).Invoking(s => s.CreateAsync(NewArea(name))).Should().ThrowAsync<ArgumentException>();
    }

    public static IEnumerable<object[]> InvalidAreas()
    {
        yield return new object[] { (Action<LootboxSpawnAreaDto>)(a => a.SpawnIntervalSeconds = 59) };
        yield return new object[] { (Action<LootboxSpawnAreaDto>)(a => a.MaxActive = -1) };
        yield return new object[] { (Action<LootboxSpawnAreaDto>)(a => a.SpawnChancePercent = 101) };
        yield return new object[] { (Action<LootboxSpawnAreaDto>)(a => a.LifetimeMinutes = 0) };
        yield return new object[] { (Action<LootboxSpawnAreaDto>)(a => a.MinOnlinePlayers = -1) };
        yield return new object[] { (Action<LootboxSpawnAreaDto>)(a => a.World = "") };
        yield return new object[] { (Action<LootboxSpawnAreaDto>)(a => a.WgRegionId = " ") };
        yield return new object[] { (Action<LootboxSpawnAreaDto>)(a => a.AllowedTypes.Add(new LootboxSpawnAreaTypeDto { LootboxTypeId = 99_999 })) };
    }

    [Theory]
    [MemberData(nameof(InvalidAreas))]
    public async Task SpawnArea_InvalidLimits_AreRefused(Action<LootboxSpawnAreaDto> breakIt)
    {
        var dto = NewArea();
        breakIt(dto);
        await using var db = NewContext();

        await AreaService(db).Invoking(s => s.CreateAsync(dto)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SpawnArea_Delete_RemovesItsActiveBoxes_AndKeepsTheirHistory()
    {
        await SeedAllAsync();
        var weapons = await TypeIdAsync("Weapons");
        int areaId;
        await using (var db = NewContext())
        {
            var created = await AreaService(db).CreateAsync(NewArea());
            areaId = created.Id;
            var grade = await db.Grades.FirstAsync(g => g.Stars == 1);
            db.LootboxSpawns.AddRange(
                new LootboxSpawn { LootboxTypeId = weapons, BoxGradeId = grade.Id, SpawnAreaId = areaId, World = "world", ExpiresAt = DateTime.UtcNow.AddMinutes(30) },
                new LootboxSpawn { LootboxTypeId = weapons, BoxGradeId = grade.Id, SpawnAreaId = areaId, World = "world", Status = LootboxSpawnStatus.Claimed, ExpiresAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await AreaService(db).DeleteAsync(areaId);

        await using var read = NewContext();
        (await read.LootboxSpawnAreas.AnyAsync()).Should().BeFalse();
        var spawns = await read.LootboxSpawns.OrderBy(s => s.Id).ToListAsync();
        spawns.Select(s => s.Status).Should().Equal(LootboxSpawnStatus.Removed, LootboxSpawnStatus.Claimed);
        // The spawn rows stay; MySQL's ON DELETE SET NULL clears their SpawnAreaId.
        read.Model.FindEntityType(typeof(LootboxSpawn))!.GetForeignKeys()
            .Single(k => k.Properties.Single().Name == nameof(LootboxSpawn.SpawnAreaId)).DeleteBehavior
            .Should().Be(DeleteBehavior.SetNull);
    }

    // ===== Configuration =====

    [Fact]
    public async Task Configuration_DefaultsToTheDesignValues_AndValidatesUpdates()
    {
        await using var db = NewContext();
        var service = new LootboxConfigurationService(new LootboxConfigurationRepository(db), Mapper());

        var config = await service.GetAsync();
        (config.Enabled, config.GlobalMaxActive, config.MaxClaimsPerPlayerPerDay, config.AnnounceMinItemStars, config.AnnounceSpawnMinBoxStars)
            .Should().Be((true, 15, (int?)10, 5, 6));
        config.DropAnnouncementTemplate.Should().Be("&6{player} &efound {item} &ein a {box}!");
        config.SpawnAnnouncementTemplate.Should().Be("&eA {box} &eappeared in &6{area}&e!");

        await service.Invoking(s => s.UpdateAsync(new UpdateLootboxConfigurationDto { MaxClaimsPerPlayerPerDay = 0 })).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.UpdateAsync(new UpdateLootboxConfigurationDto { GlobalMaxActive = -1 })).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.UpdateAsync(new UpdateLootboxConfigurationDto { AnnounceMinItemStars = 0 })).Should().ThrowAsync<ArgumentException>();

        var updated = await service.UpdateAsync(new UpdateLootboxConfigurationDto { MaxClaimsPerPlayerPerDay = null, GlobalMaxActive = 4, DropAnnouncementTemplate = "  " });
        (updated.MaxClaimsPerPlayerPerDay, updated.GlobalMaxActive).Should().Be(((int?)null, 4));
        updated.DropAnnouncementTemplate.Should().Be(LootboxConfiguration.DefaultDropAnnouncementTemplate, "blank = default");
        (await db.LootboxConfigurations.CountAsync()).Should().Be(1);
    }
}

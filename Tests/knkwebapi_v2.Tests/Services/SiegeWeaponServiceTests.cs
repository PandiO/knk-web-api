using Microsoft.EntityFrameworkCore;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege weapons phase 1 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 9; PandiO/knk-workspace#782,
/// acceptance A01): templates, ammo types (with structure damage), a scenario's weapon spots and player unlocks save
/// and read back through the real service and repository (InMemory), the save-time rules refuse bad values, and the
/// delete rules keep paid unlocks and allowed ammo. Also the new scenario and team fields.
/// </summary>
public class SiegeWeaponServiceTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeWeapons");
        await SiegeTestData.SeedValidScenarioAsync(_context);
        _context.Users.Add(new User { Id = 900, Username = "A1", Email = "a1@example.test" });
        _context.Users.Add(new User { Id = 901, Username = "Gone", Email = "gone@example.test", DeletedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private SiegeWeaponService Service() => new(new SiegeWeaponRepository(_context), SiegeTestData.Mapper());

    private SiegeScenarioService ScenarioService() => new(
        new SiegeScenarioRepository(_context), new Mock<ILocationService>().Object,
        SiegeTestData.NoRules(), SiegeTestData.Mapper());

    private static SiegeAmmoTypeUpsertDto Boulder() => new()
    {
        Name = "Heavy boulder", Effect = SiegeAmmoEffect.Impact, Damage = 12, StructureDamage = 120, BlastRadius = 2,
        Knockback = 1, PricePerShot = 20, ProjectileBlock = "STONE"
    };

    private static SiegeWeaponTemplateUpsertDto Trebuchet(params int[] ammoIds) => new()
    {
        Name = "Trebuchet", Kind = SiegeWeaponKind.Indirect, SizeClass = SiegeWeaponSizeClass.Large, MaxHealth = 400,
        BuildTimeSeconds = 60, ReloadSeconds = 20, MaxRange = 120, MinRange = 20, StartSpreadRadius = 8,
        MinSpreadRadius = 3, RangingStep = 1.5, FlightTimeAtMaxRangeSeconds = 4, ArcHeight = 30, GateDamageMultiplier = 1,
        WeaponDamageMultiplier = 1, SpotCooldownSeconds = 120, MatchPrice = 800, UnlockPrice = 5000,
        AllowedAmmoTypes = ammoIds.Select(id => new SiegeWeaponTemplateAmmoTypeUpsertDto { SiegeAmmoTypeId = id }).ToList()
    };

    // ---- Ammo types ----

    [Fact]
    public async Task AmmoType_SavesEveryField_IncludingStructureDamage()
    {
        var created = await Service().CreateAmmoTypeAsync(Boulder());
        _context.ChangeTracker.Clear();

        var read = (await Service().GetAmmoTypeByIdAsync(created.Id))!;
        Assert.Equal("Heavy boulder", read.Name);
        Assert.Equal(SiegeAmmoEffect.Impact, read.Effect);
        Assert.Equal(12, read.Damage);
        Assert.Equal(120, read.StructureDamage);
        Assert.Equal(2, read.BlastRadius);
        Assert.Equal(1, read.Knockback);
        Assert.Equal(20, read.PricePerShot);
        Assert.Equal("STONE", read.ProjectileBlock);
    }

    public static IEnumerable<object[]> BadAmmo() => new List<object[]>
    {
        new object[] { (Action<SiegeAmmoTypeUpsertDto>)(d => d.Name = " "), "Name" },
        new object[] { (Action<SiegeAmmoTypeUpsertDto>)(d => d.Damage = -1), "damage" },
        new object[] { (Action<SiegeAmmoTypeUpsertDto>)(d => d.StructureDamage = -1), "structureDamage" },
        new object[] { (Action<SiegeAmmoTypeUpsertDto>)(d => d.BlastRadius = 65), "blastRadius" },
        new object[] { (Action<SiegeAmmoTypeUpsertDto>)(d => d.ArmorPiercing = 1.5), "armorPiercing" },
        new object[] { (Action<SiegeAmmoTypeUpsertDto>)(d => d.EffectLevel = 0), "effectLevel" },
        new object[] { (Action<SiegeAmmoTypeUpsertDto>)(d => d.PricePerShot = -5), "pricePerShot" },
    };

    [Theory]
    [MemberData(nameof(BadAmmo))]
    public async Task AmmoType_RefusesBadValues(Action<SiegeAmmoTypeUpsertDto> breakIt, string field)
    {
        var dto = Boulder();
        breakIt(dto);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateAmmoTypeAsync(dto));
        Assert.Contains(field, ex.Message);
        Assert.Empty(_context.SiegeAmmoTypes);
    }

    [Fact]
    public async Task AmmoType_NameIsUnique()
    {
        await Service().CreateAmmoTypeAsync(Boulder());
        var dto = Boulder();
        dto.Name = "heavy BOULDER";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CreateAmmoTypeAsync(dto));
    }

    [Fact]
    public async Task AmmoType_AllowedOnATemplate_CantBeDeleted()
    {
        var ammo = await Service().CreateAmmoTypeAsync(Boulder());
        await Service().CreateTemplateAsync(Trebuchet(ammo.Id));
        _context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().DeleteAmmoTypeAsync(ammo.Id));
        Assert.Contains("Trebuchet", ex.Message);
        Assert.Single(_context.SiegeAmmoTypes);
    }

    // ---- Templates ----

    [Fact]
    public async Task Template_SavesEveryField_AndTheAmmoLink()
    {
        var ammo = await Service().CreateAmmoTypeAsync(Boulder());
        var created = await Service().CreateTemplateAsync(Trebuchet(ammo.Id));
        _context.ChangeTracker.Clear();

        var read = (await Service().GetTemplateByIdAsync(created.Id))!;
        Assert.Equal(SiegeWeaponKind.Indirect, read.Kind);
        Assert.Equal(SiegeWeaponSizeClass.Large, read.SizeClass);
        Assert.Equal(400, read.MaxHealth);
        Assert.Equal(120, read.MaxRange);
        Assert.Equal(20, read.MinRange);
        Assert.Equal(8, read.StartSpreadRadius);
        Assert.Equal(3, read.MinSpreadRadius);
        Assert.Equal(1.5, read.RangingStep);
        Assert.Equal(4, read.FlightTimeAtMaxRangeSeconds);
        Assert.Equal(30, read.ArcHeight);
        Assert.Equal(120, read.SpotCooldownSeconds);
        Assert.Equal(800, read.MatchPrice);
        Assert.Equal(5000, read.UnlockPrice);
        var link = Assert.Single(read.AllowedAmmoTypes);
        Assert.Equal(ammo.Id, link.SiegeAmmoTypeId);
        Assert.Equal("Heavy boulder", link.SiegeAmmoTypeName);
    }

    [Fact]
    public async Task Template_Update_ReplacesTheAmmoSet_NullKeepsIt()
    {
        var boulder = await Service().CreateAmmoTypeAsync(Boulder());
        var bombDto = Boulder();
        bombDto.Name = "Heavy bomb";
        bombDto.Effect = SiegeAmmoEffect.Explosion;
        var bomb = await Service().CreateAmmoTypeAsync(bombDto);
        var template = await Service().CreateTemplateAsync(Trebuchet(boulder.Id));
        _context.ChangeTracker.Clear();

        await Service().UpdateTemplateAsync(template.Id, Trebuchet(bomb.Id));
        _context.ChangeTracker.Clear();
        Assert.Equal(new[] { bomb.Id }, (await Service().GetTemplateByIdAsync(template.Id))!.AllowedAmmoTypes.Select(a => a.SiegeAmmoTypeId));

        var keep = Trebuchet();
        keep.AllowedAmmoTypes = null;
        keep.ReloadSeconds = 25;
        await Service().UpdateTemplateAsync(template.Id, keep);
        _context.ChangeTracker.Clear();
        var read = (await Service().GetTemplateByIdAsync(template.Id))!;
        Assert.Equal(25, read.ReloadSeconds);
        Assert.Equal(new[] { bomb.Id }, read.AllowedAmmoTypes.Select(a => a.SiegeAmmoTypeId));
    }

    public static IEnumerable<object[]> BadTemplates() => new List<object[]>
    {
        // Smoke § 1 step 4: minimum spread above the start spread, min range above the max range.
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.MinSpreadRadius = 9), "minSpreadRadius" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.MinRange = 130), "minRange" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.MaxHealth = 0), "maxHealth" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.MaxRange = 0), "maxRange" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.FlightTimeAtMaxRangeSeconds = 1), "flightTimeAtMaxRangeSeconds" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.RangingStep = -1), "rangingStep" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.GateDamageMultiplier = -1), "multipliers" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.LeaseIdleTimeoutSeconds = 0), "leaseIdleTimeoutSeconds" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.UnlockPrice = -1), "Prices" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => { d.Kind = SiegeWeaponKind.Direct; d.ConeHalfAngleDegrees = 0; }), "coneHalfAngleDegrees" },
        new object[] { (Action<SiegeWeaponTemplateUpsertDto>)(d => d.AllowedAmmoTypes = new() { new() { SiegeAmmoTypeId = 999 } }), "999" },
    };

    [Theory]
    [MemberData(nameof(BadTemplates))]
    public async Task Template_RefusesBadValues(Action<SiegeWeaponTemplateUpsertDto> breakIt, string field)
    {
        var dto = Trebuchet();
        breakIt(dto);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateTemplateAsync(dto));
        Assert.Contains(field, ex.Message);
        Assert.Empty(_context.SiegeWeaponTemplates);
    }

    [Fact]
    public async Task DirectTemplate_IgnoresTheIndirectOnlyFields()
    {
        var dto = Trebuchet();
        dto.Name = "Ballista";
        dto.Kind = SiegeWeaponKind.Direct;
        dto.SizeClass = SiegeWeaponSizeClass.Small;
        dto.MinSpreadRadius = 99; // indirect only, not checked for a ballista
        dto.ConeHalfAngleDegrees = 60;
        dto.GateDamageMultiplier = 3;
        var created = await Service().CreateTemplateAsync(dto);
        Assert.Equal(3, created.GateDamageMultiplier);
        Assert.Equal(60, created.ConeHalfAngleDegrees);
    }

    [Fact]
    public async Task Template_NameIsUnique()
    {
        await Service().CreateTemplateAsync(Trebuchet());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CreateTemplateAsync(Trebuchet()));
    }

    [Fact]
    public async Task Template_WithUnlocks_CantBeDeleted_WithoutItCascadesItsAmmoLinks()
    {
        var ammo = await Service().CreateAmmoTypeAsync(Boulder());
        var unlocked = await Service().CreateTemplateAsync(Trebuchet(ammo.Id));
        var other = Trebuchet(ammo.Id);
        other.Name = "Catapult";
        var free = await Service().CreateTemplateAsync(other);
        await Service().CreateUnlockAsync(new UserSiegeWeaponUnlockUpsertDto { UserId = 900, SiegeWeaponTemplateId = unlocked.Id });
        _context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().DeleteTemplateAsync(unlocked.Id));

        await Service().DeleteTemplateAsync(free.Id);
        _context.ChangeTracker.Clear();
        Assert.Equal(new[] { unlocked.Id }, _context.SiegeWeaponTemplateAmmoTypes.Select(j => j.SiegeWeaponTemplateId).ToList());
        Assert.Single(_context.SiegeAmmoTypes);
    }

    // ---- Weapon spots ----

    private static SiegeWeaponSpotUpsertDto Spot(int teamId = 202) => new()
    {
        // 9 along x, 17 high, 15 along z: large facing north (phase 2 works the class out from bounds and facing).
        SiegeTeamId = teamId, Name = "North wall", Facing = SiegeWeaponFacing.North, SizeClass = SiegeWeaponSizeClass.Large,
        MinX = 0, MinY = 64, MinZ = 0, MaxX = 8, MaxY = 80, MaxZ = 14
    };

    // ---- Phase 2 (#783): size class from bounds and facing (W70, A02-A03) ----

    [Fact]
    public async Task Spot_SizeClass_IsWorkedOutFromTheBoundsAndFacing_NotTheTypedValue()
    {
        // 7 along x, 5 high, 9 along z: small facing north, whatever was typed.
        var small = Spot();
        small.MaxX = 6; small.MaxY = 68; small.MaxZ = 8;
        small.SizeClass = SiegeWeaponSizeClass.Large;
        Assert.Equal(SiegeWeaponSizeClass.Small, (await Service().CreateSpotAsync(100, small)).SizeClass);

        // The large footprint turned east is 15 wide x 9 long: medium.
        var east = Spot();
        east.Facing = SiegeWeaponFacing.East;
        Assert.Equal(SiegeWeaponSizeClass.Medium, (await Service().CreateSpotAsync(100, east)).SizeClass);
        Assert.Equal(SiegeWeaponSizeClass.Large, (await Service().CreateSpotAsync(100, Spot())).SizeClass);
    }

    [Fact]
    public async Task Spot_ChangingTheFacing_WorksTheClassOutAgain()
    {
        var created = await Service().CreateSpotAsync(100, Spot());
        _context.ChangeTracker.Clear();
        var turned = Spot();
        turned.Facing = SiegeWeaponFacing.West;
        await Service().UpdateSpotAsync(created.Id, turned);
        _context.ChangeTracker.Clear();
        var read = (await Service().GetSpotByIdAsync(created.Id))!;
        Assert.Equal(SiegeWeaponFacing.West, read.Facing);
        Assert.Equal(SiegeWeaponSizeClass.Medium, read.SizeClass);
    }

    [Fact]
    public async Task Spot_BoundsSmallerThanTheSmallClass_AreRefused()
    {
        var tiny = Spot();
        tiny.MaxX = 4; tiny.MaxY = 68; tiny.MaxZ = 4;
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateSpotAsync(100, tiny));
        Assert.Equal("The bounds are too small for a weapon facing North: 5 wide x 5 long x 5 high, the small class "
            + "needs at least 7 wide x 9 long x 5 high.", error.Message);

        // 7 x 9 facing east is 9 wide x 7 long: too short along the facing.
        var sideways = Spot();
        sideways.MaxX = 6; sideways.MaxY = 68; sideways.MaxZ = 8; sideways.Facing = SiegeWeaponFacing.East;
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateSpotAsync(100, sideways));
        _context.ChangeTracker.Clear();
        Assert.Empty(_context.SiegeWeaponSpots);
    }

    [Fact]
    public async Task Spot_WithoutBounds_KeepsTheTypedClass()
    {
        var none = Spot();
        none.MinX = none.MinY = none.MinZ = none.MaxX = none.MaxY = none.MaxZ = null;
        none.SizeClass = SiegeWeaponSizeClass.Medium;
        none.Facing = SiegeWeaponFacing.East;
        Assert.Equal(SiegeWeaponSizeClass.Medium, (await Service().CreateSpotAsync(100, none)).SizeClass);
    }

    [Theory]
    [InlineData(SiegeWeaponFacing.North, 0, 64, 0, 6, 68, 8, SiegeWeaponSizeClass.Small)]
    [InlineData(SiegeWeaponFacing.South, 0, 64, 0, 6, 71, 8, SiegeWeaponSizeClass.Medium)]
    [InlineData(SiegeWeaponFacing.West, 0, 64, 0, 8, 68, 6, SiegeWeaponSizeClass.Small)]
    [InlineData(SiegeWeaponFacing.North, 0, 64, 0, 20, 71, 20, SiegeWeaponSizeClass.Medium)]
    [InlineData(SiegeWeaponFacing.East, 0, 64, 0, 14, 79, 8, SiegeWeaponSizeClass.Large)]
    public void Sizing_LargestClassThatFits(SiegeWeaponFacing facing, int minX, int minY, int minZ, int maxX, int maxY,
        int maxZ, SiegeWeaponSizeClass expected)
    {
        Assert.Equal(expected, SiegeWeaponSpotSizing.ClassFor(minX, minY, minZ, maxX, maxY, maxZ, facing));
    }

    [Fact]
    public void Sizing_TooLowOrTooShort_HasNoClass()
    {
        Assert.Null(SiegeWeaponSpotSizing.ClassFor(0, 64, 0, 6, 67, 8, SiegeWeaponFacing.North));
        Assert.Null(SiegeWeaponSpotSizing.ClassFor(0, 64, 0, 6, 68, 8, SiegeWeaponFacing.West));
    }

    [Fact]
    public async Task Spot_IsCreatedUnderItsScenario_AndShowsOnTheScenario()
    {
        var created = await Service().CreateSpotAsync(100, Spot());
        Assert.Equal(0, created.SortOrder);
        Assert.Equal("Raiders", created.SiegeTeamName);
        _context.ChangeTracker.Clear();

        var second = await Service().CreateSpotAsync(100, Spot());
        Assert.Equal(1, second.SortOrder);
        _context.ChangeTracker.Clear();

        var scenario = (await ScenarioService().GetByIdAsync(100))!;
        Assert.Equal(new[] { created.Id, second.Id }, scenario.WeaponSpots.Select(w => w.Id));
        var spot = scenario.WeaponSpots[0];
        Assert.Equal(SiegeWeaponFacing.North, spot.Facing);
        Assert.Equal(SiegeWeaponSizeClass.Large, spot.SizeClass);
        Assert.Equal(202, spot.SiegeTeamId);
        Assert.Equal(14, spot.MaxZ);
    }

    [Fact]
    public async Task Spot_TeamMustBelongToTheScenario()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateSpotAsync(100, Spot(teamId: 999)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service().CreateSpotAsync(12345, Spot()));
    }

    [Fact]
    public async Task Spot_BoundsAreAllOrNothing_AndOrdered()
    {
        var partial = Spot();
        partial.MaxZ = null;
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateSpotAsync(100, partial));

        var reversed = Spot();
        reversed.MinX = 10;
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateSpotAsync(100, reversed));

        var none = Spot();
        none.MinX = none.MinY = none.MinZ = none.MaxX = none.MaxY = none.MaxZ = null;
        var created = await Service().CreateSpotAsync(100, none);
        Assert.Null(created.MinX);
    }

    [Fact]
    public async Task Spot_CantMoveToAnotherScenario()
    {
        var created = await Service().CreateSpotAsync(100, Spot());
        _context.ChangeTracker.Clear();
        var dto = Spot();
        dto.SiegeScenarioId = 101;
        await Assert.ThrowsAsync<ArgumentException>(() => Service().UpdateSpotAsync(created.Id, dto));
    }

    [Fact]
    public async Task DeletingTheTeam_DeletesItsSpots_DeletingTheScenario_DeletesTheRest()
    {
        await Service().CreateSpotAsync(100, Spot(teamId: 202));
        await Service().CreateSpotAsync(100, Spot(teamId: 201));
        _context.ChangeTracker.Clear();

        await ScenarioService().DeleteTeamAsync(202);
        _context.ChangeTracker.Clear();
        Assert.Equal(new[] { 201 }, _context.SiegeWeaponSpots.Select(w => w.SiegeTeamId).ToList());

        await ScenarioService().DeleteAsync(100);
        _context.ChangeTracker.Clear();
        Assert.Empty(_context.SiegeWeaponSpots);
    }

    // ---- Unlocks ----

    [Fact]
    public async Task Unlock_IsListedForThatPlayerOnly_AndOncePerType()
    {
        var template = await Service().CreateTemplateAsync(Trebuchet());
        var created = await Service().CreateUnlockAsync(new UserSiegeWeaponUnlockUpsertDto { UserId = 900, SiegeWeaponTemplateId = template.Id });
        Assert.Equal("A1", created.Username);
        Assert.Equal("Trebuchet", created.SiegeWeaponTemplateName);
        Assert.True(created.UnlockedAt > DateTime.UtcNow.AddMinutes(-1));
        _context.ChangeTracker.Clear();

        Assert.Single(await Service().GetUnlocksForUserAsync(900));
        Assert.Empty(await Service().GetUnlocksForUserAsync(901));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().CreateUnlockAsync(new UserSiegeWeaponUnlockUpsertDto { UserId = 900, SiegeWeaponTemplateId = template.Id }));
    }

    [Fact]
    public async Task Unlock_NeedsAnExistingPlayerAndTemplate()
    {
        var template = await Service().CreateTemplateAsync(Trebuchet());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().CreateUnlockAsync(new UserSiegeWeaponUnlockUpsertDto { UserId = 901, SiegeWeaponTemplateId = template.Id }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().CreateUnlockAsync(new UserSiegeWeaponUnlockUpsertDto { UserId = 900, SiegeWeaponTemplateId = 999 }));
    }

    [Fact]
    public async Task Unlock_Search_FiltersByPlayer()
    {
        var template = await Service().CreateTemplateAsync(Trebuchet());
        await Service().CreateUnlockAsync(new UserSiegeWeaponUnlockUpsertDto { UserId = 900, SiegeWeaponTemplateId = template.Id });
        var page = await Service().SearchUnlocksAsync(new PagedQueryDto
        {
            PageNumber = 1, PageSize = 10, Filters = new Dictionary<string, string> { ["userId"] = "900" }
        });
        Assert.Equal(1, page.TotalCount);
        var none = await Service().SearchUnlocksAsync(new PagedQueryDto
        {
            PageNumber = 1, PageSize = 10, Filters = new Dictionary<string, string> { ["userId"] = "12" }
        });
        Assert.Equal(0, none.TotalCount);
    }

    // ---- Scenario and team fields ----

    [Fact]
    public async Task Scenario_FriendlyFireAndCooldownOverride_TeamActiveSpots_Save()
    {
        var scenario = (await ScenarioService().GetByIdAsync(100))!;
        Assert.False(scenario.FriendlyFire);
        Assert.Null(scenario.WeaponSpotCooldownOverrideSeconds);
        Assert.All(scenario.Teams, t => Assert.Equal(0, t.ActiveWeaponSpots));
        _context.ChangeTracker.Clear();

        var dto = new SiegeScenarioUpsertDto
        {
            Name = scenario.Name, TownId = 1, HubLocationId = 1000, PlayersMin = 2, PlayersMax = 20,
            FriendlyFire = true, WeaponSpotCooldownOverrideSeconds = 90
        };
        await ScenarioService().UpdateAsync(100, dto);
        _context.ChangeTracker.Clear();
        await ScenarioService().UpdateTeamAsync(202, new SiegeTeamUpsertDto
        {
            Role = SiegeTeamRole.Attacker, AllianceGroup = 2, Name = "Raiders", ChatColor = "RED", BannerDesignId = 51,
            ActiveWeaponSpots = 2
        });
        _context.ChangeTracker.Clear();

        var read = (await ScenarioService().GetByIdAsync(100))!;
        Assert.True(read.FriendlyFire);
        Assert.Equal(90, read.WeaponSpotCooldownOverrideSeconds);
        Assert.Equal(2, read.Teams.Single(t => t.Id == 202).ActiveWeaponSpots);
        Assert.Equal(0, read.Teams.Single(t => t.Id == 201).ActiveWeaponSpots);

        dto.WeaponSpotCooldownOverrideSeconds = -1;
        await Assert.ThrowsAsync<ArgumentException>(() => ScenarioService().UpdateAsync(100, dto));
        await Assert.ThrowsAsync<ArgumentException>(() => ScenarioService().UpdateTeamAsync(202, new SiegeTeamUpsertDto
        {
            Role = SiegeTeamRole.Attacker, AllianceGroup = 2, Name = "Raiders", ChatColor = "RED", BannerDesignId = 51,
            ActiveWeaponSpots = -1
        }));
    }

    // ---- Model delete rules (they become the migration's ON DELETE clauses) ----

    [Theory]
    [InlineData(typeof(SiegeWeaponSpot), nameof(SiegeWeaponSpot.SiegeScenarioId), DeleteBehavior.Cascade)]
    [InlineData(typeof(SiegeWeaponSpot), nameof(SiegeWeaponSpot.SiegeTeamId), DeleteBehavior.Cascade)]
    [InlineData(typeof(SiegeWeaponTemplateAmmoType), nameof(SiegeWeaponTemplateAmmoType.SiegeWeaponTemplateId), DeleteBehavior.Cascade)]
    [InlineData(typeof(SiegeWeaponTemplateAmmoType), nameof(SiegeWeaponTemplateAmmoType.SiegeAmmoTypeId), DeleteBehavior.Restrict)]
    [InlineData(typeof(UserSiegeWeaponUnlock), nameof(UserSiegeWeaponUnlock.UserId), DeleteBehavior.Restrict)]
    [InlineData(typeof(UserSiegeWeaponUnlock), nameof(UserSiegeWeaponUnlock.SiegeWeaponTemplateId), DeleteBehavior.Restrict)]
    public void DeleteRules(Type entity, string foreignKey, DeleteBehavior expected)
    {
        var fk = _context.Model.FindEntityType(entity)!.GetForeignKeys()
            .Single(f => f.Properties.Any(p => p.Name == foreignKey));
        Assert.Equal(expected, fk.DeleteBehavior);
    }
}

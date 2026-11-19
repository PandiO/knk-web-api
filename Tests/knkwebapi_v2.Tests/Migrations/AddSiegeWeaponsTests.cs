using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Siege weapons phase 1 (PandiO/knk-workspace#782, charter § 3.3): the chain's L1 migration is additive. Five new
/// tables and their indexes, three new columns with defaults that keep existing scenarios as they are (no friendly
/// fire, no cooldown override, no active weapon spots), and nothing dropped, renamed or altered.
/// </summary>
public class AddSiegeWeaponsTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddSiegeWeapons().UpOperations;

    [Fact]
    public void Up_IsAdditiveOnly()
    {
        Assert.All(Up(), op => Assert.True(op is CreateTableOperation or CreateIndexOperation or AddColumnOperation, op.GetType().Name));
        Assert.Equal(
            new[] { "siege_ammo_types", "siege_weapon_spots", "siege_weapon_template_ammo_types", "siege_weapon_templates", "user_siege_weapon_unlocks" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Up_NewColumns_DefaultToTheOldBehaviour()
    {
        var columns = Up().OfType<AddColumnOperation>().ToDictionary(c => $"{c.Table}.{c.Name}");
        Assert.Equal(3, columns.Count);

        var active = columns["siege_teams.ActiveWeaponSpots"];
        Assert.False(active.IsNullable);
        Assert.Equal(0, active.DefaultValue);

        var friendlyFire = columns["siege_scenarios.FriendlyFire"];
        Assert.False(friendlyFire.IsNullable);
        Assert.Equal(false, friendlyFire.DefaultValue);

        Assert.True(columns["siege_scenarios.WeaponSpotCooldownOverrideSeconds"].IsNullable);
    }

    [Fact]
    public void Up_TemplateAndAmmo_HaveTheSection3Fields()
    {
        var tables = Up().OfType<CreateTableOperation>().ToDictionary(t => t.Name);
        var template = tables["siege_weapon_templates"].Columns.ToDictionary(c => c.Name);
        foreach (var name in new[] { "StartSpreadRadius", "MinSpreadRadius", "RangingStep", "FlightTimeAtMaxRangeSeconds",
                     "ArcHeight", "GateDamageMultiplier", "WeaponDamageMultiplier", "ConeHalfAngleDegrees", "SpotCooldownSeconds",
                     "MatchPrice", "UnlockPrice", "MaxRange", "ReloadSeconds", "BuildTimeSeconds", "MaxHealth" })
            Assert.False(template[name].IsNullable, name);
        foreach (var name in new[] { "MinRange", "LeaseIdleTimeoutSeconds", "ArmedSchematicId", "FiredSchematicId", "WreckSchematicId" })
            Assert.True(template[name].IsNullable, name);
        Assert.Equal(typeof(string), template["Kind"].ClrType);

        var ammo = tables["siege_ammo_types"].Columns.ToDictionary(c => c.Name);
        foreach (var name in new[] { "Damage", "StructureDamage", "BlastRadius", "Knockback", "ArmorPiercing", "PricePerShot" })
            Assert.False(ammo[name].IsNullable, name);
        Assert.Equal(typeof(string), ammo["Effect"].ClrType);
    }

    [Fact]
    public void Up_SchematicReferences_HaveNoForeignKeyYet()
    {
        // The schematic pipeline (PandiO/knk-workspace#614) has no table yet; its link adds the keys.
        var template = Up().OfType<CreateTableOperation>().Single(t => t.Name == "siege_weapon_templates");
        Assert.Empty(template.ForeignKeys);
    }

    [Fact]
    public void Up_OneUnlockPerPlayerAndTemplate()
    {
        var index = Up().OfType<CreateIndexOperation>()
            .Single(i => i.Table == "user_siege_weapon_unlocks" && i.Columns.Contains("SiegeWeaponTemplateId") && i.Columns.Contains("UserId"));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Down_DropsOnlyWhatUpAdded()
    {
        var down = new AddSiegeWeapons().DownOperations;
        Assert.All(down, op => Assert.True(op is DropTableOperation or DropColumnOperation, op.GetType().Name));
        Assert.Equal(5, down.OfType<DropTableOperation>().Count());
        Assert.Equal(3, down.OfType<DropColumnOperation>().Count());
    }
}

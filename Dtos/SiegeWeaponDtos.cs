using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Siege weapons phase 1 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 9; PandiO/knk-workspace#782):
    // templates with their allowed ammo types (M2M set), ammo types, a scenario's weapon spots and player unlocks.
    // Validation lives in SiegeWeaponService (ArgumentException -> 400, KeyNotFoundException -> 404,
    // InvalidOperationException -> 409).

    // ---- SiegeWeaponTemplate ----

    public class SiegeWeaponTemplateReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("kind")] public SiegeWeaponKind Kind { get; set; }
        [JsonPropertyName("sizeClass")] public SiegeWeaponSizeClass SizeClass { get; set; }
        [JsonPropertyName("armedSchematicId")] public int? ArmedSchematicId { get; set; }
        [JsonPropertyName("firedSchematicId")] public int? FiredSchematicId { get; set; }
        [JsonPropertyName("wreckSchematicId")] public int? WreckSchematicId { get; set; }
        [JsonPropertyName("maxHealth")] public int MaxHealth { get; set; }
        [JsonPropertyName("buildTimeSeconds")] public int BuildTimeSeconds { get; set; }
        [JsonPropertyName("reloadSeconds")] public int ReloadSeconds { get; set; }
        [JsonPropertyName("maxRange")] public double MaxRange { get; set; }
        [JsonPropertyName("minRange")] public double? MinRange { get; set; }
        [JsonPropertyName("startSpreadRadius")] public double StartSpreadRadius { get; set; }
        [JsonPropertyName("minSpreadRadius")] public double MinSpreadRadius { get; set; }
        [JsonPropertyName("rangingStep")] public double RangingStep { get; set; }
        [JsonPropertyName("flightTimeAtMaxRangeSeconds")] public double FlightTimeAtMaxRangeSeconds { get; set; }
        [JsonPropertyName("arcHeight")] public double ArcHeight { get; set; }
        [JsonPropertyName("leaseIdleTimeoutSeconds")] public int? LeaseIdleTimeoutSeconds { get; set; }
        [JsonPropertyName("coneHalfAngleDegrees")] public double ConeHalfAngleDegrees { get; set; }
        [JsonPropertyName("gateDamageMultiplier")] public double GateDamageMultiplier { get; set; }
        [JsonPropertyName("weaponDamageMultiplier")] public double WeaponDamageMultiplier { get; set; }
        [JsonPropertyName("spotCooldownSeconds")] public int SpotCooldownSeconds { get; set; }
        [JsonPropertyName("matchPrice")] public int MatchPrice { get; set; }
        [JsonPropertyName("unlockPrice")] public int UnlockPrice { get; set; }
        // Siege shop L4 (#869): the TitleBracket id a player needs to unlock the type; null = none.
        [JsonPropertyName("unlockMinTitleLevel")] public int? UnlockMinTitleLevel { get; set; }
        [JsonPropertyName("unlockMinTitleName")] public string? UnlockMinTitleName { get; set; }
        [JsonPropertyName("allowedAmmoTypes")] public List<SiegeWeaponTemplateAmmoTypeDto> AllowedAmmoTypes { get; set; } = new();
    }

    // AllowedAmmoTypes is replaced as a set; null leaves the current set unchanged, [] clears it.
    public class SiegeWeaponTemplateUpsertDto
    {
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("kind")] public SiegeWeaponKind Kind { get; set; } = SiegeWeaponKind.Indirect;
        [JsonPropertyName("sizeClass")] public SiegeWeaponSizeClass SizeClass { get; set; } = SiegeWeaponSizeClass.Medium;
        [JsonPropertyName("armedSchematicId")] public int? ArmedSchematicId { get; set; }
        [JsonPropertyName("firedSchematicId")] public int? FiredSchematicId { get; set; }
        [JsonPropertyName("wreckSchematicId")] public int? WreckSchematicId { get; set; }
        [JsonPropertyName("maxHealth")] public int MaxHealth { get; set; } = 300;
        [JsonPropertyName("buildTimeSeconds")] public int BuildTimeSeconds { get; set; } = 60;
        [JsonPropertyName("reloadSeconds")] public int ReloadSeconds { get; set; } = 20;
        [JsonPropertyName("maxRange")] public double MaxRange { get; set; } = 100;
        [JsonPropertyName("minRange")] public double? MinRange { get; set; }
        [JsonPropertyName("startSpreadRadius")] public double StartSpreadRadius { get; set; } = 8;
        [JsonPropertyName("minSpreadRadius")] public double MinSpreadRadius { get; set; } = 3;
        [JsonPropertyName("rangingStep")] public double RangingStep { get; set; } = 1.5;
        [JsonPropertyName("flightTimeAtMaxRangeSeconds")] public double FlightTimeAtMaxRangeSeconds { get; set; } = 4;
        [JsonPropertyName("arcHeight")] public double ArcHeight { get; set; } = 20;
        [JsonPropertyName("leaseIdleTimeoutSeconds")] public int? LeaseIdleTimeoutSeconds { get; set; }
        [JsonPropertyName("coneHalfAngleDegrees")] public double ConeHalfAngleDegrees { get; set; } = 60;
        [JsonPropertyName("gateDamageMultiplier")] public double GateDamageMultiplier { get; set; } = 1;
        [JsonPropertyName("weaponDamageMultiplier")] public double WeaponDamageMultiplier { get; set; } = 1;
        [JsonPropertyName("spotCooldownSeconds")] public int SpotCooldownSeconds { get; set; } = 120;
        [JsonPropertyName("matchPrice")] public int MatchPrice { get; set; } = 200;
        [JsonPropertyName("unlockPrice")] public int UnlockPrice { get; set; } = 1000;
        [JsonPropertyName("unlockMinTitleLevel")] public int? UnlockMinTitleLevel { get; set; }
        [JsonPropertyName("allowedAmmoTypes")] public List<SiegeWeaponTemplateAmmoTypeUpsertDto>? AllowedAmmoTypes { get; set; }
    }

    public class SiegeWeaponTemplateListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("kind")] public SiegeWeaponKind Kind { get; set; }
        [JsonPropertyName("sizeClass")] public SiegeWeaponSizeClass SizeClass { get; set; }
        [JsonPropertyName("matchPrice")] public int MatchPrice { get; set; }
        [JsonPropertyName("unlockPrice")] public int UnlockPrice { get; set; }
        [JsonPropertyName("unlockMinTitleLevel")] public int? UnlockMinTitleLevel { get; set; }
        [JsonPropertyName("ammoTypeCount")] public int AmmoTypeCount { get; set; }
    }

    public class SiegeWeaponTemplateAmmoTypeDto
    {
        [JsonPropertyName("siegeWeaponTemplateId")] public int SiegeWeaponTemplateId { get; set; }
        [JsonPropertyName("siegeAmmoTypeId")] public int SiegeAmmoTypeId { get; set; }
        [JsonPropertyName("siegeAmmoTypeName")] public string? SiegeAmmoTypeName { get; set; }
    }

    public class SiegeWeaponTemplateAmmoTypeUpsertDto
    {
        [JsonPropertyName("siegeAmmoTypeId")] public int SiegeAmmoTypeId { get; set; }
    }

    // ---- SiegeAmmoType ----

    public class SiegeAmmoTypeReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("effect")] public SiegeAmmoEffect Effect { get; set; }
        [JsonPropertyName("damage")] public double Damage { get; set; }
        [JsonPropertyName("structureDamage")] public double StructureDamage { get; set; }
        [JsonPropertyName("blastRadius")] public double BlastRadius { get; set; }
        [JsonPropertyName("knockback")] public double Knockback { get; set; }
        [JsonPropertyName("armorPiercing")] public double ArmorPiercing { get; set; }
        [JsonPropertyName("projectileBlock")] public string? ProjectileBlock { get; set; }
        [JsonPropertyName("particleTrail")] public string? ParticleTrail { get; set; }
        [JsonPropertyName("effectRadius")] public double? EffectRadius { get; set; }
        [JsonPropertyName("effectDurationSeconds")] public double? EffectDurationSeconds { get; set; }
        [JsonPropertyName("effectLevel")] public int? EffectLevel { get; set; }
        [JsonPropertyName("pricePerShot")] public int PricePerShot { get; set; }
    }

    public class SiegeAmmoTypeUpsertDto
    {
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("effect")] public SiegeAmmoEffect Effect { get; set; } = SiegeAmmoEffect.Impact;
        [JsonPropertyName("damage")] public double Damage { get; set; } = 10;
        [JsonPropertyName("structureDamage")] public double StructureDamage { get; set; } = 100;
        [JsonPropertyName("blastRadius")] public double BlastRadius { get; set; } = 2;
        [JsonPropertyName("knockback")] public double Knockback { get; set; } = 1;
        [JsonPropertyName("armorPiercing")] public double ArmorPiercing { get; set; }
        [JsonPropertyName("projectileBlock")] public string? ProjectileBlock { get; set; }
        [JsonPropertyName("particleTrail")] public string? ParticleTrail { get; set; }
        [JsonPropertyName("effectRadius")] public double? EffectRadius { get; set; }
        [JsonPropertyName("effectDurationSeconds")] public double? EffectDurationSeconds { get; set; }
        [JsonPropertyName("effectLevel")] public int? EffectLevel { get; set; }
        [JsonPropertyName("pricePerShot")] public int PricePerShot { get; set; } = 10;
    }

    public class SiegeAmmoTypeListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("effect")] public SiegeAmmoEffect Effect { get; set; }
        [JsonPropertyName("damage")] public double Damage { get; set; }
        [JsonPropertyName("structureDamage")] public double StructureDamage { get; set; }
        [JsonPropertyName("blastRadius")] public double BlastRadius { get; set; }
        [JsonPropertyName("pricePerShot")] public int PricePerShot { get; set; }
    }

    // ---- SiegeWeaponSpot ----

    public class SiegeWeaponSpotReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeTeamId")] public int SiegeTeamId { get; set; }
        [JsonPropertyName("siegeTeamName")] public string? SiegeTeamName { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("wgRegionId")] public string? WgRegionId { get; set; }
        [JsonPropertyName("facing")] public SiegeWeaponFacing Facing { get; set; }
        [JsonPropertyName("sizeClass")] public SiegeWeaponSizeClass SizeClass { get; set; }
    }

    // SortOrder null on create = after the existing spots; null on update = keep. SizeClass is kept only when the game
    // server can't be asked for the region's bounds (#988); otherwise the API works it out.
    public class SiegeWeaponSpotUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int? SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeTeamId")] public int SiegeTeamId { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("wgRegionId")] public string? WgRegionId { get; set; }
        [JsonPropertyName("facing")] public SiegeWeaponFacing Facing { get; set; } = SiegeWeaponFacing.North;
        [JsonPropertyName("sizeClass")] public SiegeWeaponSizeClass SizeClass { get; set; } = SiegeWeaponSizeClass.Small;
    }

    // ---- UserSiegeWeaponUnlock ----

    public class UserSiegeWeaponUnlockReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("siegeWeaponTemplateId")] public int SiegeWeaponTemplateId { get; set; }
        [JsonPropertyName("siegeWeaponTemplateName")] public string? SiegeWeaponTemplateName { get; set; }
        [JsonPropertyName("unlockedAt")] public DateTime UnlockedAt { get; set; }
    }

    // UnlockedAt null = now.
    public class UserSiegeWeaponUnlockUpsertDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("siegeWeaponTemplateId")] public int SiegeWeaponTemplateId { get; set; }
        [JsonPropertyName("unlockedAt")] public DateTime? UnlockedAt { get; set; }
    }
}

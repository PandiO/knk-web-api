namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// Which items a vanilla enchantment can go on, and which vanilla enchantments exclude each other, so the lootbox
/// roll never records an enchantment the delivered <c>ItemStack</c> can't carry (Sharpness on a bow in a Weapons
/// box). A small copy of Minecraft 1.21's <c>supported_items</c> and <c>exclusive_set</c> tags, keyed by
/// enchantment and matched on the blueprint's material key (<c>minecraft:iron_sword</c>). Where the vanilla tag is
/// uncertain it errs narrow: a roll it refuses is just a roll that didn't hit, while one it wrongly allows is what
/// the plugin's <c>canEnchantItem</c> safety net would have to drop from a minted item.
/// <para>
/// Only for <b>rolled</b> enchantments: blueprint defaults are admin-authored and applied as-is (v1's one-offs have
/// Sharpness on pickaxes, applied with <c>addUnsafeEnchantment</c>). Custom (knk) enchantments are lore and fit any
/// item, so they're never filtered here.
/// </para>
/// </summary>
public static class VanillaEnchantmentRules
{
    private const string Namespace = "minecraft:";

    // Item groups, as material-name suffixes or exact names (without the namespace). Suffixes keep new tiers (copper
    // tools/armor in 1.21.9) working without a code change.
    private static readonly string[] Swords = { "_sword" };
    private static readonly string[] Axes = { "_axe" };
    private static readonly string[] MiningTools = { "_pickaxe", "_axe", "_shovel", "_hoe" };
    private static readonly string[] Helmets = { "_helmet" };
    private static readonly string[] Chestplates = { "_chestplate" };
    private static readonly string[] Leggings = { "_leggings" };
    private static readonly string[] Boots = { "_boots" };
    private static readonly string[] Armor = { "_helmet", "_chestplate", "_leggings", "_boots" };

    private static readonly string[] Durability =
    {
        "_sword", "_axe", "_pickaxe", "_shovel", "_hoe", "_helmet", "_chestplate", "_leggings", "_boots",
        "=bow", "=crossbow", "=trident", "=mace", "=fishing_rod", "=shears", "=shield", "=elytra", "=flint_and_steel",
        "=carrot_on_a_stick", "=warped_fungus_on_a_stick", "=brush",
    };

    private static string[] Exact(params string[] names) => names.Select(n => "=" + n).ToArray();
    private static string[] Union(params string[][] groups) => groups.SelectMany(g => g).Distinct().ToArray();

    // Enchantment (without namespace) → the item groups it supports.
    private static readonly IReadOnlyDictionary<string, string[]> Supported = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // Melee
        ["sharpness"] = Union(Swords, Axes),
        ["smite"] = Union(Swords, Axes, Exact("mace")),
        ["bane_of_arthropods"] = Union(Swords, Axes, Exact("mace")),
        ["knockback"] = Swords,
        ["fire_aspect"] = Union(Swords, Exact("mace")),
        ["looting"] = Swords,
        ["sweeping_edge"] = Swords,
        // Mace
        ["density"] = Exact("mace"),
        ["breach"] = Exact("mace"),
        ["wind_burst"] = Exact("mace"),
        // Tools
        ["efficiency"] = Union(MiningTools, Exact("shears")),
        ["silk_touch"] = MiningTools,
        ["fortune"] = MiningTools,
        // Armor
        ["protection"] = Armor,
        ["fire_protection"] = Armor,
        ["blast_protection"] = Armor,
        ["projectile_protection"] = Armor,
        ["thorns"] = Armor,
        ["respiration"] = Helmets,
        ["aqua_affinity"] = Helmets,
        ["feather_falling"] = Boots,
        ["depth_strider"] = Boots,
        ["frost_walker"] = Boots,
        ["soul_speed"] = Boots,
        ["swift_sneak"] = Leggings,
        // Ranged
        ["power"] = Exact("bow"),
        ["punch"] = Exact("bow"),
        ["flame"] = Exact("bow"),
        ["infinity"] = Exact("bow"),
        ["multishot"] = Exact("crossbow"),
        ["piercing"] = Exact("crossbow"),
        ["quick_charge"] = Exact("crossbow"),
        ["loyalty"] = Exact("trident"),
        ["impaling"] = Exact("trident"),
        ["riptide"] = Exact("trident"),
        ["channeling"] = Exact("trident"),
        ["luck_of_the_sea"] = Exact("fishing_rod"),
        ["lure"] = Exact("fishing_rod"),
        // Anything with durability
        ["unbreaking"] = Durability,
        ["mending"] = Durability,
        ["vanishing_curse"] = Durability,
        ["binding_curse"] = Union(Armor, Exact("elytra")),
    };

    // Vanilla exclusive sets: two different enchantments in one set can't be on the same item. Riptide is exclusive
    // with Loyalty and with Channeling, but those two go together, hence two sets.
    private static readonly string[][] ExclusiveSets =
    {
        new[] { "sharpness", "smite", "bane_of_arthropods", "density", "breach" },
        new[] { "protection", "fire_protection", "blast_protection", "projectile_protection" },
        new[] { "depth_strider", "frost_walker" },
        new[] { "silk_touch", "fortune" },
        new[] { "infinity", "mending" },
        new[] { "multishot", "piercing" },
        new[] { "riptide", "loyalty" },
        new[] { "riptide", "channeling" },
    };

    /// <summary>
    /// True when the vanilla enchantment <paramref name="enchantmentKey"/> can go on an item of
    /// <paramref name="materialKey"/>. An enchantment this table doesn't know is refused (errs narrow).
    /// </summary>
    public static bool CanApply(string enchantmentKey, string materialKey)
    {
        var enchantment = Strip(enchantmentKey);
        var material = Strip(materialKey);
        if (enchantment == null || material == null) return false;
        if (!Supported.TryGetValue(enchantment, out var groups)) return false;
        return groups.Any(g => g.StartsWith('=') ? material == g[1..] : material.EndsWith(g, StringComparison.Ordinal));
    }

    /// <summary>
    /// True when the two vanilla enchantments can't be on one item. The same enchantment never conflicts with itself
    /// (a second one just raises the level).
    /// </summary>
    public static bool Conflicts(string enchantmentKeyA, string enchantmentKeyB)
    {
        var a = Strip(enchantmentKeyA);
        var b = Strip(enchantmentKeyB);
        if (a == null || b == null || a == b) return false;
        return ExclusiveSets.Any(set => set.Contains(a) && set.Contains(b));
    }

    /// <summary>
    /// Lower-cased key without the <c>minecraft:</c> namespace; null for blank keys and for keys in another
    /// namespace (not a vanilla enchantment or material).
    /// </summary>
    private static string? Strip(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var normalized = key.Trim().ToLowerInvariant();
        if (normalized.StartsWith(Namespace, StringComparison.Ordinal)) return normalized[Namespace.Length..];
        return normalized.Contains(':') ? null : normalized;
    }
}

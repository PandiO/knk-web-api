using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

/// <summary>
/// The menu item (the nether star of PandiO/knk-workspace#160) in the game settings (siege shop DESIGN § 4.3, § 6.1.8,
/// P03, P26): its look, one action per click type and per-situation overrides. Stored as JSON in
/// <c>GameSettings.MenuItemSettingsJson</c>; a missing value reads as <see cref="Services.MenuItemSettings.Defaults"/>
/// (today's <c>menus.menu-item.*</c> keys plus the siege overrides of § 4.3.4). The plugin keeps the
/// <c>config.yml</c> keys as its startup fallback.
/// </summary>
public class MenuItemSettingsDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary><c>all</c> or <c>bedrock</c> (Floodgate players only).</summary>
    [JsonPropertyName("giveTo")]
    public string GiveTo { get; set; } = "all";

    /// <summary>Put it back at every join when it is missing.</summary>
    [JsonPropertyName("giveOnJoin")]
    public bool GiveOnJoin { get; set; } = true;

    /// <summary>Give it back at respawn.</summary>
    [JsonPropertyName("keepOnDeath")]
    public bool KeepOnDeath { get; set; } = true;

    /// <summary>A Bukkit material name such as <c>NETHER_STAR</c>.</summary>
    [JsonPropertyName("material")]
    public string Material { get; set; } = "NETHER_STAR";

    /// <summary>Display name, "&amp;"-coded.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Lore lines, "&amp;"-coded.</summary>
    [JsonPropertyName("lore")]
    public List<string> Lore { get; set; } = new();

    /// <summary>0-8 hotbar (8 = right-most), 9-35 main inventory.</summary>
    [JsonPropertyName("slot")]
    public int Slot { get; set; } = 8;

    /// <summary>True: dropping takes it away until the next join or respawn.</summary>
    [JsonPropertyName("droppable")]
    public bool Droppable { get; set; }

    /// <summary>True: it may be moved within the player's own inventory (inventory clicks then do nothing).</summary>
    [JsonPropertyName("movable")]
    public bool Movable { get; set; }

    /// <summary>The default click table: click type (<see cref="Services.MenuItemSettings.ClickTypes"/>) → action. A
    /// click that is not listed does nothing.</summary>
    [JsonPropertyName("clicks")]
    public Dictionary<string, MenuItemActionDto> Clicks { get; set; } = new();

    /// <summary>Situation (<see cref="Services.MenuItemSettings.Situations"/>) → partial click table. A click the
    /// override does not list uses <see cref="Clicks"/>.</summary>
    [JsonPropertyName("overrides")]
    public Dictionary<string, Dictionary<string, MenuItemActionDto>> Overrides { get; set; } = new();
}

/// <summary>What one click does (P26): <c>OpenMenu</c> a menu key, <c>RunCommand</c> a player command, or <c>None</c>.</summary>
public class MenuItemActionDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "None";

    /// <summary>For <c>OpenMenu</c>: the menu template key, e.g. <c>main</c>, <c>siege.information</c>, <c>siege.shop</c>.</summary>
    [JsonPropertyName("menuKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MenuKey { get; set; }

    /// <summary>For <c>RunCommand</c>: the command the player runs, without the slash, e.g. <c>siege shop</c>.</summary>
    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }
}

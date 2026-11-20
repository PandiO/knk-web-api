using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services;

/// <summary>
/// The menu item section of the game settings (siege shop DESIGN § 4.3, § 5.7, § 6.1.8; P03, P26;
/// PandiO/knk-workspace#870): defaults, reading and validation. Stored as JSON in
/// <see cref="GameSettings.MenuItemSettingsJson"/>; a missing column value means <see cref="Defaults"/>.
/// </summary>
public static class MenuItemSettings
{
    public const string LeftClick = "LEFT_CLICK";
    public const string RightClick = "RIGHT_CLICK";
    public const string ShiftLeftClick = "SHIFT_LEFT_CLICK";
    public const string ShiftRightClick = "SHIFT_RIGHT_CLICK";
    public const string InventoryClick = "INVENTORY_CLICK";

    /// <summary>The click types a table can set (§ 4.3.1).</summary>
    public static readonly string[] ClickTypes = { LeftClick, RightClick, ShiftLeftClick, ShiftRightClick, InventoryClick };

    public const string SituationSiegeHub = "siege.hub";
    public const string SituationSiegeMatch = "siege.match";
    public const string SituationSiegeSurvivalMatch = "siege.survival.match";

    /// <summary>The situations the plugin registers (§ 4.3.3, P26), most specific first (§ 5.7.1). Later features add theirs here.</summary>
    public static readonly string[] Situations = { SituationSiegeSurvivalMatch, SituationSiegeMatch, SituationSiegeHub };

    public const string ActionOpenMenu = "OpenMenu";
    public const string ActionRunCommand = "RunCommand";
    public const string ActionNone = "None";

    public static readonly string[] ActionTypes = { ActionOpenMenu, ActionRunCommand, ActionNone };

    public static readonly string[] GiveToValues = { "all", "bedrock" };

    public const int MaxSlot = 35;
    public const int MaxNameLength = 256;
    public const int MaxLoreLines = 20;
    public const int MaxLoreLineLength = 256;
    public const int MaxMenuKeyLength = 100;
    public const int MaxCommandLength = 256;

    private static readonly Regex MaterialPattern = new("^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex MenuKeyPattern = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.Compiled);

    /// <summary>
    /// Today's <c>menus.menu-item.*</c> defaults of the plugin's <c>config.yml</c> plus the siege overrides of § 4.3.4.
    /// The shift clicks open the main menu too, because today a shift-click is a plain click to the plugin.
    /// </summary>
    public static MenuItemSettingsDto Defaults() => new()
    {
        Enabled = true,
        GiveTo = "all",
        GiveOnJoin = true,
        KeepOnDeath = true,
        Material = "NETHER_STAR",
        Name = "&6&lMenu &7(click to open)",
        Lore = new List<string> { "&7Opens the Knights & Kings menu (&e/menu&7)." },
        Slot = 8,
        Droppable = false,
        Movable = false,
        Clicks = new Dictionary<string, MenuItemActionDto>
        {
            [LeftClick] = OpenMenu("main"),
            [RightClick] = OpenMenu("main"),
            [ShiftLeftClick] = OpenMenu("main"),
            [ShiftRightClick] = OpenMenu("main"),
            [InventoryClick] = OpenMenu("main"),
        },
        Overrides = new Dictionary<string, Dictionary<string, MenuItemActionDto>>
        {
            [SituationSiegeHub] = new()
            {
                [LeftClick] = OpenMenu("siege.information"),
                [RightClick] = OpenMenu("siege.information"),
            },
            [SituationSiegeMatch] = MatchOverride(),
            [SituationSiegeSurvivalMatch] = MatchOverride(),
        },
    };

    private static Dictionary<string, MenuItemActionDto> MatchOverride() => new()
    {
        [LeftClick] = OpenMenu("siege.information"),
        [RightClick] = OpenMenu("siege.information"),
        [ShiftRightClick] = OpenMenu("siege.shop"),
    };

    private static MenuItemActionDto OpenMenu(string key) => new() { Type = ActionOpenMenu, MenuKey = key };

    /// <summary>The stored section, or <see cref="Defaults"/> when none is stored (or it can't be read).</summary>
    public static MenuItemSettingsDto Read(GameSettings? settings)
    {
        var dto = JsonColumn.Deserialize<MenuItemSettingsDto>(settings?.MenuItemSettingsJson);
        if (dto == null)
        {
            return Defaults();
        }
        dto.Lore ??= new List<string>();
        dto.Clicks ??= new Dictionary<string, MenuItemActionDto>();
        dto.Overrides ??= new Dictionary<string, Dictionary<string, MenuItemActionDto>>();
        return dto;
    }

    /// <summary>
    /// Validates (throws <see cref="ArgumentException"/> naming the field) and returns a clean copy: click types,
    /// situations and action types in their canonical spelling, <c>giveTo</c> lower case, the material upper case,
    /// a leading slash dropped from commands, <c>None</c> actions without key or command, empty overrides dropped.
    /// </summary>
    public static MenuItemSettingsDto Normalize(MenuItemSettingsDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }
        const string p = "menuItem.";

        var giveTo = GiveToValues.FirstOrDefault(v => v.Equals(dto.GiveTo?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{p}giveTo must be one of {string.Join(", ", GiveToValues)}.");

        var material = (dto.Material ?? string.Empty).Trim().ToUpperInvariant();
        if (material.StartsWith("MINECRAFT:", StringComparison.Ordinal))
        {
            material = material["MINECRAFT:".Length..];
        }
        if (!MaterialPattern.IsMatch(material))
        {
            throw new ArgumentException($"{p}material must be a material name such as NETHER_STAR.");
        }

        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"{p}name must be at most {MaxNameLength} characters.");
        }

        var lore = (dto.Lore ?? new List<string>()).Select(l => (l ?? string.Empty).TrimEnd()).ToList();
        if (lore.Count > MaxLoreLines)
        {
            throw new ArgumentException($"{p}lore has at most {MaxLoreLines} lines.");
        }
        if (lore.Any(l => l.Length > MaxLoreLineLength || l.Contains('\n') || l.Contains('\r')))
        {
            throw new ArgumentException($"{p}lore lines must be single lines of at most {MaxLoreLineLength} characters.");
        }

        if (dto.Slot < 0 || dto.Slot > MaxSlot)
        {
            throw new ArgumentException($"{p}slot must be 0 to {MaxSlot} (0-8 hotbar, 9-35 main inventory).");
        }

        var clicks = NormalizeTable(dto.Clicks, $"{p}clicks");

        var overrides = new Dictionary<string, Dictionary<string, MenuItemActionDto>>();
        foreach (var (rawSituation, table) in dto.Overrides ?? new Dictionary<string, Dictionary<string, MenuItemActionDto>>())
        {
            var situation = Situations.FirstOrDefault(s => s.Equals(rawSituation?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"{p}overrides: '{rawSituation}' is not a known situation ({string.Join(", ", Situations)}).");
            if (overrides.ContainsKey(situation))
            {
                throw new ArgumentException($"{p}overrides: situation '{situation}' is listed twice.");
            }
            var cleaned = NormalizeTable(table, $"{p}overrides[{situation}]");
            if (cleaned.Count > 0)
            {
                overrides[situation] = cleaned;
            }
        }

        return new MenuItemSettingsDto
        {
            Enabled = dto.Enabled,
            GiveTo = giveTo,
            GiveOnJoin = dto.GiveOnJoin,
            KeepOnDeath = dto.KeepOnDeath,
            Material = material,
            Name = name,
            Lore = lore,
            Slot = dto.Slot,
            Droppable = dto.Droppable,
            Movable = dto.Movable,
            Clicks = clicks,
            Overrides = overrides,
        };
    }

    private static Dictionary<string, MenuItemActionDto> NormalizeTable(Dictionary<string, MenuItemActionDto>? table, string field)
    {
        var cleaned = new Dictionary<string, MenuItemActionDto>();
        foreach (var (rawClick, action) in table ?? new Dictionary<string, MenuItemActionDto>())
        {
            var click = ClickTypes.FirstOrDefault(c => c.Equals(rawClick?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"{field}: '{rawClick}' is not a click type ({string.Join(", ", ClickTypes)}).");
            if (cleaned.ContainsKey(click))
            {
                throw new ArgumentException($"{field}: click '{click}' is listed twice.");
            }
            if (action == null)
            {
                continue;
            }
            cleaned[click] = NormalizeAction(action, $"{field}[{click}]");
        }
        return cleaned;
    }

    private static MenuItemActionDto NormalizeAction(MenuItemActionDto action, string field)
    {
        var type = ActionTypes.FirstOrDefault(t => t.Equals(action.Type?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{field}.type must be one of {string.Join(", ", ActionTypes)}.");
        switch (type)
        {
            case ActionOpenMenu:
                var key = action.MenuKey?.Trim() ?? string.Empty;
                if (key.Length == 0)
                {
                    throw new ArgumentException($"{field}: Open menu needs a menu key.");
                }
                if (key.Length > MaxMenuKeyLength || !MenuKeyPattern.IsMatch(key))
                {
                    throw new ArgumentException($"{field}: '{key}' is not a menu key (lower case letters, digits, '.', '_' and '-', such as siege.shop).");
                }
                return new MenuItemActionDto { Type = type, MenuKey = key };
            case ActionRunCommand:
                var command = (action.Command ?? string.Empty).Trim().TrimStart('/').Trim();
                if (command.Length == 0)
                {
                    throw new ArgumentException($"{field}: Run command needs a command.");
                }
                if (command.Length > MaxCommandLength || command.Contains('\n') || command.Contains('\r'))
                {
                    throw new ArgumentException($"{field}: the command must be one line of at most {MaxCommandLength} characters.");
                }
                return new MenuItemActionDto { Type = type, Command = command };
            default:
                return new MenuItemActionDto { Type = ActionNone };
        }
    }
}

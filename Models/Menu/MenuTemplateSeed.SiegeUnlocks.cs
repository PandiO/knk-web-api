using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// Siege shop L4 (knk-workspace docs/specs/siege-shop/DESIGN.md § 4.2, § 7.2.5, P08; PandiO/knk-workspace#869): the
/// <c>siege.unlocks</c> menu (every siege weapon type with its coin price, minimum title and the viewer's state; an
/// unlockable one is bought after a confirmation) and its <b>Siege unlocks</b> button in <c>siege.overview</c>, the menu
/// the main menu's Siege Minigame tile (the minigames tile) opens: main menu → Siege Minigame → Siege unlocks.
/// Create-only like every MenuTemplateSeed entry. The plugin side is knk-paper's <c>SiegeUnlocksMenuFeature</c> over
/// knk-core's <c>core.siege.unlocks</c> views (root <c>siegeUnlocks</c>, row source <c>siege.unlocks</c>, action
/// <c>siege.unlocks.buy</c>, condition <c>siege.unlocks.purchase-pending</c>); the seed ↔ plugin contract test is
/// knk-api-client's <c>SiegeMenuSeedContractTest</c>. The button needs only engine ids and hides itself
/// (<c>menu-available</c>) until a plugin with the feature validated the menu, so there is no deploy order.
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string SiegeUnlocksMenuKey = "siege.unlocks";

    /// <summary>The Siege unlocks button's slot in <c>siege.overview</c>'s header (0; 4 is the info item, 8 Back).</summary>
    public const int SiegeUnlocksButtonSlot = 0;

    private static IEnumerable<MenuTemplate> SiegeUnlocksTemplates()
    {
        yield return SiegeUnlocksTemplate();
    }

    private static MenuItemTemplate SiegeUnlocksButton() =>
        OpenTile(SiegeUnlocksButtonSlot, 2, SiegeUnlocksMenuKey,
            Bind("Material", "ANVIL", VariableRefreshPolicy.Static),
            Bind("Name", "&6Siege unlocks", VariableRefreshPolicy.Static),
            Lore(0, "&7Unlock siege weapon types with coins"),
            Lore(1, "&7to buy them in siege matches."));

    /// <summary>
    /// Databases seeded before L4 get the Siege unlocks button in <c>siege.overview</c> once: added to its Header section
    /// when no item there opens <c>siege.unlocks</c> and the slot is free. Idempotent; returns 1 when it added it.
    /// </summary>
    public static async Task<int> AddSiegeUnlocksButtonAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var overview = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions)
            .SingleOrDefaultAsync(t => t.Key == SiegeOverviewMenuKey, cancellationToken);
        var header = overview?.Sections.FirstOrDefault(s => s.Name == "Header");
        if (header == null) return 0;
        var opensUnlocks = overview!.Sections.SelectMany(s => s.Items).Any(i => i.Actions.Any(a =>
            a.ActionTypeId == "menu.open" && a.ParamsJson != null && a.ParamsJson.Contains("\"" + SiegeUnlocksMenuKey + "\"")));
        if (opensUnlocks) return 0;
        if (header.Items.Any(i => i.SlotOverride == SiegeUnlocksButtonSlot)) return 0;
        header.Items.Add(SiegeUnlocksButton());
        await context.SaveChangesAsync(cancellationToken);
        logger?.LogInformation("Siege unlocks button added to {Menu}", SiegeOverviewMenuKey);
        return 1;
    }

    /// <summary>Every weapon type with price, minimum title and state; Confirm/Cancel while a purchase waits.</summary>
    private static MenuTemplate SiegeUnlocksTemplate()
    {
        var purchase = new ActionBinding
        {
            ActionTypeId = "menu.confirm.request",
            ParamsJson = "{\"actionTypeId\":\"siege.unlocks.buy\",\"actionParamsJson\":\"{\\\"templateId\\\":\\\"$row.getTemplateId$\\\"}\","
                + "\"prompt\":\"$row.getPurchasePrompt$\"}",
            SortOrder = 0,
        };
        // Any other row: straight to the action, which says why not (unlocked already, title, coins, in a match).
        var explain = new ActionBinding
        {
            ActionTypeId = "siege.unlocks.buy",
            ParamsJson = "{\"templateId\":\"$row.getTemplateId$\"}",
            SortOrder = 1,
        };
        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
                Bind("Lore", "$row.getLines$", VariableRefreshPolicy.OnDirty),
            },
        };
        row.Actions.Add(purchase);
        row.Actions.Add(explain);
        AddActionCondition(row, purchase,
            RenderCondition("value-equals", "{\"value\":\"$row.getCanUnlock$\",\"expected\":\"true\"}"));
        AddActionCondition(row, explain,
            RenderCondition("value-equals", "{\"value\":\"$row.getCanUnlock$\",\"expected\":\"false\"}"));

        return new MenuTemplate
        {
            Key = SiegeUnlocksMenuKey,
            Name = "&8Siege unlocks",
            Description = "Every siege weapon type with its coin price, minimum title and the viewer's state; unlock one "
                + "outside a match. Siege shop L4 (#869), DESIGN § 4.2.",
            Height = 6,
            Growth = MenuGrowthMode.Dynamic,
            MinHeight = 3,
            AutoRefreshTicks = 20,
            Sections =
            {
                new MenuSectionTemplate
                {
                    Name = "Header",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 0,
                    DisplaySlot = 0,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        DemoItem(4, 0,
                            Bind("Material", "ANVIL", VariableRefreshPolicy.Static),
                            Bind("Name", "&6Siege unlocks", VariableRefreshPolicy.Static),
                            Lore(0, "&7Unlock a siege weapon type once"),
                            Lore(1, "&7to buy it in siege matches."),
                            Lore(2, ""),
                            Bind("Lore", "$siegeUnlocks.getCoinsLine$", VariableRefreshPolicy.Ttl, 3, 20),
                            Bind("Lore", "$siegeUnlocks.getTitleLine$", VariableRefreshPolicy.Ttl, 4, 20),
                            Bind("Lore", "$siegeUnlocks.getMatchLine$", VariableRefreshPolicy.Ttl, 5, 20)),
                        ConfirmButton(2, "siege.unlocks.purchase-pending", "&7Unlock the weapon you picked (see chat)"),
                        CancelButton(6, "siege.unlocks.purchase-pending"),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Weapons",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "siege.unlocks",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        row,
                    },
                },
            },
        };
    }
}

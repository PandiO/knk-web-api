using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// Siege shop L3 (knk-workspace docs/specs/siege-shop/DESIGN.md § 4.1, § 7.2.3, P24; PandiO/knk-workspace#868): the
/// shop menus <c>siege.shop</c> (the categories of the viewer's match and their balance) and
/// <c>siege.shop.category</c> (one category's offers, <c>ctx.categoryId</c>), and the <b>Shop</b> button in
/// <c>siege.information</c>. Create-only like every MenuTemplateSeed entry. The plugin side is knk-paper's
/// <c>SiegeShopMenuFeature</c> over knk-core's <c>core.siege.shop</c> views (root <c>siegeShop</c>, row sources
/// <c>siege.shop-categories</c>/<c>siege.shop-entries</c>, actions <c>siege.shop.buy</c>/<c>siege.shop.open</c>, condition
/// <c>siege.shop-available</c>); the seed ↔ plugin contract test is knk-api-client's <c>SiegeMenuSeedContractTest</c>.
/// The plugin must be deployed first: a plugin without these ids blocks <c>siege.information</c> at its validation.
/// The engine's menu titles are fixed text, so the balance is the header item's lore, not part of the title.
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string SiegeShopMenuKey = "siege.shop";
    public const string SiegeShopCategoryMenuKey = "siege.shop.category";

    private static IEnumerable<MenuTemplate> SiegeShopTemplates()
    {
        yield return SiegeShopTemplate();
        yield return SiegeShopCategoryTemplate();
    }

    /// <summary>
    /// The Shop button of <c>siege.information</c> (slot 0 of its header): shown only while the viewer can use a shop
    /// (<c>siege.shop.enabled</c> on and a member of a running match), so with the flag off the menu looks as before.
    /// </summary>
    private static MenuItemTemplate SiegeShopButton()
    {
        var shop = DemoItem(0, 4,
            Bind("Material", "EMERALD", VariableRefreshPolicy.Static),
            Bind("Name", "&aShop", VariableRefreshPolicy.Static),
            Lore(0, "&7Buy siege weapons and consumables"),
            Lore(1, "&7with your siege points."),
            Lore(2, ""),
            Bind("Lore", "$siegeShop.getBalanceLine$", VariableRefreshPolicy.Ttl, 3, 20));
        shop.Actions.Add(new ActionBinding { ActionTypeId = "siege.shop.open", ParamsJson = "{}", SortOrder = 0 });
        shop.Conditions.Add(RenderCondition("siege.shop-available", "{}"));
        return shop;
    }

    /// <summary>
    /// Databases seeded before the shop get its button in <c>siege.information</c> once: added to the Header section
    /// when no item there runs <c>siege.shop.open</c> and slot 0 is free. Idempotent; returns 1 when it added it.
    /// </summary>
    public static async Task<int> AddSiegeShopButtonAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var info = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions)
            .SingleOrDefaultAsync(t => t.Key == SiegeInformationMenuKey, cancellationToken);
        var header = info?.Sections.FirstOrDefault(s => s.Name == "Header");
        if (header == null) return 0;
        var items = info!.Sections.SelectMany(s => s.Items).ToList();
        if (items.Any(i => i.Actions.Any(a => a.ActionTypeId == "siege.shop.open"))) return 0;
        if (header.Items.Any(i => i.SlotOverride == 0)) return 0;
        header.Items.Add(SiegeShopButton());
        await context.SaveChangesAsync(cancellationToken);
        logger?.LogInformation("Siege shop button added to {Menu}", SiegeInformationMenuKey);
        return 1;
    }

    private static MenuItemTemplate SiegeShopBalance(int slot, string name)
    {
        return DemoItem(slot, 0,
            Bind("Material", "GOLD_INGOT", VariableRefreshPolicy.Static),
            Bind("Name", name, VariableRefreshPolicy.OnDirty),
            Bind("Lore", "$siegeShop.getBalanceLine$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Lore", "$siegeShop.getEmptyLine$", VariableRefreshPolicy.OnDirty, 1),
            Lore(2, ""),
            Bind("Lore", "$siegeShop.getFooterLines$", VariableRefreshPolicy.Static, 3));
    }

    /// <summary>The categories of the viewer's match (empty ones are not listed); a click opens one.</summary>
    private static MenuTemplate SiegeShopTemplate()
    {
        var category = new MenuItemTemplate
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
            Actions =
            {
                new ActionBinding
                {
                    ActionTypeId = "menu.open",
                    ParamsJson = "{\"key\":\"" + SiegeShopCategoryMenuKey + "\",\"ctx.categoryId\":\"$row.getCategoryId$\"}",
                    SortOrder = 0,
                },
            },
        };

        return new MenuTemplate
        {
            Key = SiegeShopMenuKey,
            Name = "&8Siege shop",
            Description = "The siege shop of the viewer's match: its categories and balance. Siege shop L3 (#868), DESIGN § 4.1.",
            Height = 3,
            Growth = MenuGrowthMode.Dynamic,
            MinHeight = 2,
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
                    Items = { SiegeShopBalance(4, "&6Siege shop"), BackButton(8, 1) },
                },
                new MenuSectionTemplate
                {
                    Name = "Categories",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 2,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "siege.shop-categories",
                    ContentSourceParamsJson = "{}",
                    Items = { category },
                },
            },
        };
    }

    /// <summary>One category's offers (<c>ctx.categoryId</c>): current price, bought n/cap, cooldown, why not; a click buys.</summary>
    private static MenuTemplate SiegeShopCategoryTemplate()
    {
        var entry = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("Amount", "$row.getAmount$", VariableRefreshPolicy.OnDirty),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
                Bind("Lore", "$row.getLines$", VariableRefreshPolicy.Ttl, 0, 20),
            },
            Actions =
            {
                new ActionBinding
                {
                    ActionTypeId = "siege.shop.buy",
                    ParamsJson = "{\"entryId\":\"$row.getEntryId$\"}",
                    SortOrder = 0,
                },
            },
        };

        return new MenuTemplate
        {
            Key = SiegeShopCategoryMenuKey,
            Name = "&8Siege shop",
            Description = "One category of the siege shop (ctx.categoryId); a click buys. Siege shop L3 (#868), DESIGN § 4.1.",
            Height = 6,
            Growth = MenuGrowthMode.Dynamic,
            MinHeight = 2,
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
                    Items = { SiegeShopBalance(4, "$siegeShop.getCategoryName$"), BackButton(8, 1) },
                },
                new MenuSectionTemplate
                {
                    Name = "Offers",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 5,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "siege.shop-entries",
                    ContentSourceParamsJson = "{\"categoryId\":\"$ctx.categoryId$\"}",
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        entry,
                    },
                },
            },
        };
    }
}

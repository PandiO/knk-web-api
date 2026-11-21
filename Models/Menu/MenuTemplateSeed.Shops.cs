using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Shop menus (production P10b, PandiO/knk-workspace#687; shops DESIGN § 8), built from the legacy V1 shop menus as the
/// developer decided on 2026-11-21 (knk-workspace docs/specs/legacy/inventory-menu-screens.md § 3.7). Create-only like
/// every MenuTemplateSeed entry.
/// <list type="number">
///   <item><c>shops.shop</c> (V1 "Products of X", § 3.7.1): header 0 Financial (coins), 3 Sell chest (only when the
///     shop buys from players), 4 the shop's information, 8 Back; below it the paged catalogue of
///     <c>GET api/Shops/{id}/offer</c> (row source <c>shops.offer</c>, <c>ctx.shopId</c>); a click opens the item.</item>
///   <item><c>shops.item</c> (V1 item info, § 3.7.3): 0 Financial, 3 Amount (cycles 1, 8, 16, 32, 64), 4 the item,
///     5 the price with stock and total (a click opens the confirmation), 6 the description, 8 Back.</item>
///   <item><c>shops.confirm</c> (V1 sale confirmation, § 3.2.2): 4 the confirmation (item, amount, the quoted price),
///     8 Back, 12 Confirm (to the inventory), 14 Cancel; added: 13 the item and 10 Confirm to the reward inbox.</item>
/// </list>
/// The sell menu (V1 "Sell items to X", § 3.7.2) needs deposit slots the engine doesn't have, so the plugin builds it
/// itself (action <c>shops.sell</c>). The plugin side is knk-paper's <c>ShopMenuFeature</c> (root <c>shop</c>, row
/// sources <c>shops.offer</c>/<c>shops.item</c>, actions <c>shops.amount</c>/<c>shops.purchase</c>/<c>shops.sell</c>,
/// conditions <c>shops.buys</c>/<c>shops.buyable</c>); deploy the plugin first, or these menus fail its validation.
/// Without the plugin's <c>production.enabled</c> they are blocked.
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string ShopMenuKey = "shops.shop";
    public const string ShopItemMenuKey = "shops.item";
    public const string ShopConfirmMenuKey = "shops.confirm";

    private const string ShopCtx = "\"ctx.shopId\":\"$ctx.shopId$\"";

    private static IEnumerable<MenuTemplate> ShopTemplates()
    {
        yield return ShopTemplate();
        yield return ShopItemTemplate();
        yield return ShopConfirmTemplate();
    }

    /// <summary>The legacy slot 0 "Financial": the viewer's coins.</summary>
    private static MenuItemTemplate ShopFinancial() =>
        DemoItem(0, 0,
            Bind("Material", "GOLD_INGOT", VariableRefreshPolicy.Static),
            Bind("Name", "&6Financial", VariableRefreshPolicy.Static),
            Bind("Lore", "$shop.getCoinsLine$", VariableRefreshPolicy.Ttl, 0, 20));

    /// <summary>One item of the shop, shown as the item itself (rows of <c>shops.offer</c> and <c>shops.item</c>).</summary>
    private static MenuItemTemplate ShopItemRow(params ActionBinding[] actions)
    {
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
                Bind("Lore", "$row.getLines$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20),
            },
        };
        row.Actions.AddRange(actions);
        return row;
    }

    private static ConditionBinding ShopClickCondition(string conditionTypeId) => new()
    {
        ConditionTypeId = conditionTypeId,
        ParamsJson = "{}",
        SortOrder = 0,
        Phase = MenuConditionPhase.Click,
    };

    /// <summary>The shop menu: info header and the paged catalogue (<c>ctx.shopId</c>).</summary>
    private static MenuTemplate ShopTemplate()
    {
        var sell = DemoItem(3, 1,
            Bind("Material", "CHEST", VariableRefreshPolicy.Static),
            Bind("Name", "&aSell items to this shop", VariableRefreshPolicy.Static),
            Lore(0, "&7See what the shop pays for your items"));
        sell.Actions.Add(new ActionBinding { ActionTypeId = "shops.sell", ParamsJson = "{}", SortOrder = 0 });
        sell.Conditions.Add(RenderCondition("shops.buys", "{}"));

        var info = DemoItem(4, 2,
            Bind("Material", "ANVIL", VariableRefreshPolicy.Static),
            Bind("Name", "$shop.getInfoName$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Lore", "$shop.getShopLines$", VariableRefreshPolicy.Ttl, 0, 20),
            Lore(1, ""),
            Bind("Lore", "$shop.getEmptyLine$", VariableRefreshPolicy.Ttl, 2, 20));

        var open = new ActionBinding
        {
            ActionTypeId = "menu.open",
            ParamsJson = "{\"key\":\"" + ShopItemMenuKey + "\"," + ShopCtx + ",\"ctx.item\":\"$row.getItem$\"}",
            SortOrder = 0,
        };

        return new MenuTemplate
        {
            Key = ShopMenuKey,
            Name = "&8Shop",
            Description = "A shop's catalogue (ctx.shopId): legacy V1 \"Products of X\". Production P10b (#687), shops DESIGN § 8.",
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
                    Items = { ShopFinancial(), sell, info, BackButton(8, 3) },
                },
                new MenuSectionTemplate
                {
                    Name = "Catalogue",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "shops.offer",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        ShopItemRow(open),
                    },
                },
            },
        };
    }

    /// <summary>The shop item information menu (<c>ctx.shopId</c>, <c>ctx.item</c>): legacy V1 item info.</summary>
    private static MenuTemplate ShopItemTemplate()
    {
        var amount = DemoItem(3, 1,
            Bind("Material", "CHEST", VariableRefreshPolicy.Static),
            Bind("Name", "$shop.getAmountName$", VariableRefreshPolicy.OnDirty),
            Bind("Lore", "$shop.getAmountLines$", VariableRefreshPolicy.OnDirty));
        amount.Actions.Add(new ActionBinding { ActionTypeId = "shops.amount", ParamsJson = "{}", SortOrder = 0 });

        var price = DemoItem(5, 2,
            Bind("Material", "NAME_TAG", VariableRefreshPolicy.Static),
            Bind("Name", "$shop.getPriceName$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Lore", "$shop.getPriceLines$", VariableRefreshPolicy.Ttl, 0, 20));
        price.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.open",
            ParamsJson = "{\"key\":\"" + ShopConfirmMenuKey + "\"," + ShopCtx
                + ",\"ctx.item\":\"$ctx.item$\",\"ctx.amount\":\"$shop.getAmount$\"}",
            SortOrder = 0,
        });
        price.Conditions.Add(ShopClickCondition("shops.buyable"));

        var description = DemoItem(6, 3,
            Bind("Material", "MAP", VariableRefreshPolicy.Static),
            Bind("Name", "&eDescription", VariableRefreshPolicy.Static),
            Bind("Lore", "$shop.getDescriptionLines$", VariableRefreshPolicy.Ttl, 0, 20));

        return new MenuTemplate
        {
            Key = ShopItemMenuKey,
            Name = "&8Shop item",
            Description = "One item of a shop (ctx.shopId, ctx.item): amount, price, description, buy. Legacy V1 item info. P10b (#687).",
            Height = 1,
            Growth = MenuGrowthMode.Static,
            AutoRefreshTicks = 20,
            Sections =
            {
                // The item sits in slot 4, so the buttons are two sections around it: slots 0-3 and 5-8.
                new MenuSectionTemplate
                {
                    Name = "Header",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 0,
                    DisplaySlot = 0,
                    Width = 4,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { ShopFinancial(), amount },
                },
                new MenuSectionTemplate
                {
                    Name = "Buttons",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 2,
                    DisplaySlot = 5,
                    Width = 4,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { price, description, BackButton(8, 4) },
                },
                new MenuSectionTemplate
                {
                    Name = "Item",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 4,
                    Width = 1,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    ContentSourceId = "shops.item",
                    ContentSourceParamsJson = "{}",
                    Items = { ShopItemRow() },
                },
            },
        };
    }

    /// <summary>The purchase confirmation (<c>ctx.shopId</c>, <c>ctx.item</c>, <c>ctx.amount</c>): legacy V1 confirm.</summary>
    private static MenuTemplate ShopConfirmTemplate()
    {
        var book = DemoItem(4, 0,
            Bind("Material", "BOOK", VariableRefreshPolicy.Static),
            Bind("Name", "&eConfirmation", VariableRefreshPolicy.Static),
            Bind("Lore", "$shop.getConfirmLines$", VariableRefreshPolicy.Ttl, 0, 20));

        var inbox = DemoItem(10, 1,
            Bind("Material", "ENDER_CHEST", VariableRefreshPolicy.Static),
            Bind("Name", "&bConfirm: to your reward inbox", VariableRefreshPolicy.Static),
            Lore(0, "&7Buy it and claim it later with /inbox"),
            Bind("Lore", "$shop.getTotalLine$", VariableRefreshPolicy.Ttl, 1, 20));
        inbox.Actions.Add(new ActionBinding { ActionTypeId = "shops.purchase", ParamsJson = "{\"deliverTo\":\"Inbox\"}", SortOrder = 0 });
        inbox.Conditions.Add(ShopClickCondition("shops.buyable"));

        var confirm = DemoItem(12, 2,
            Bind("Material", "GREEN_TERRACOTTA", VariableRefreshPolicy.Static),
            Bind("Name", "&aConfirm", VariableRefreshPolicy.Static),
            Lore(0, "&7Buy it into your inventory"),
            Bind("Lore", "$shop.getTotalLine$", VariableRefreshPolicy.Ttl, 1, 20));
        confirm.Actions.Add(new ActionBinding { ActionTypeId = "shops.purchase", ParamsJson = "{\"deliverTo\":\"Inventory\"}", SortOrder = 0 });
        confirm.Conditions.Add(ShopClickCondition("shops.buyable"));

        var cancel = DemoItem(14, 3,
            Bind("Material", "RED_TERRACOTTA", VariableRefreshPolicy.Static),
            Bind("Name", "&cCancel", VariableRefreshPolicy.Static),
            Lore(0, "&7Back without buying"));
        cancel.Actions.Add(new ActionBinding { ActionTypeId = "menu.back", ParamsJson = "{}", SortOrder = 0 });

        return new MenuTemplate
        {
            Key = ShopConfirmMenuKey,
            Name = "&8Confirm purchase",
            Description = "The purchase confirmation (ctx.shopId, ctx.item, ctx.amount): the quoted price, confirm or cancel. Legacy V1 confirm. P10b (#687).",
            Height = 2,
            Growth = MenuGrowthMode.Static,
            AutoRefreshTicks = 20,
            Sections =
            {
                // The item sits in slot 13, so the second row is two sections around it: slots 9-12 and 14-17.
                new MenuSectionTemplate
                {
                    Name = "Header",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 0,
                    DisplaySlot = 0,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { book, BackButton(8, 4) },
                },
                new MenuSectionTemplate
                {
                    Name = "Confirm",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 2,
                    DisplaySlot = 9,
                    Width = 4,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { inbox, confirm },
                },
                new MenuSectionTemplate
                {
                    Name = "Cancel",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 3,
                    DisplaySlot = 14,
                    Width = 4,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { cancel },
                },
                new MenuSectionTemplate
                {
                    Name = "Item",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 13,
                    Width = 1,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    ContentSourceId = "shops.item",
                    ContentSourceParamsJson = "{}",
                    Items = { ShopItemRow() },
                },
            },
        };
    }
}

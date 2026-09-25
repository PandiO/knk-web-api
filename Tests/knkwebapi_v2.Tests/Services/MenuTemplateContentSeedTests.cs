using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// InventoryMenu content port (docs/specs/inventory-menu/CONTENT_PORT_PLAN.md CP1-CP8):
/// the content seeds are create-only and every one of them must be a template the CRUD API
/// itself would accept - same pattern as <see cref="MenuTemplateServicePhase9Tests"/>.
/// </summary>
public class MenuTemplateContentSeedTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();

    private static async Task<(KnKDbContext Context, List<MenuTemplate> Seeded)> SeedTwiceAsync()
    {
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"MenuContentSeed_{Guid.NewGuid()}")
            .Options;
        var context = new KnKDbContext(options);

        await MenuTemplateSeed.SeedCanonicalAsync(context);
        // Create-only: a second run must not duplicate anything.
        await MenuTemplateSeed.SeedCanonicalAsync(context);

        var seeded = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.VariableBindings)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions).ThenInclude(a => a.Conditions)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Conditions)
            .Include(t => t.Sections).ThenInclude(s => s.VariableBindings)
            .ToListAsync();
        return (context, seeded);
    }

    private static MenuTemplate Single(List<MenuTemplate> seeded, string key) => seeded.Single(t => t.Key == key);

    private static MenuItemTemplate ItemAt(MenuTemplate template, int slot) =>
        template.Sections.SelectMany(s => s.Items).Single(i => i.SlotOverride == slot);

    private static string? Binding(MenuItemTemplate item, string property) =>
        item.VariableBindings.FirstOrDefault(b => b.TargetProperty == property)?.Expression;

    /// <summary>Every content-port template key, in seed order. Add each new template here.</summary>
    private static readonly string[] ContentKeyList =
    {
        MenuTemplateSeed.HubMenuKey,
        MenuTemplateSeed.KitsOverviewMenuKey,
        MenuTemplateSeed.ProfileMenuKey,
        MenuTemplateSeed.ItemsCatalogMenuKey,
        MenuTemplateSeed.PremiumTiersMenuKey,
        MenuTemplateSeed.UserManagerMenuKey,
        MenuTemplateSeed.UserManagerEditMenuKey,
        MenuTemplateSeed.UserManagerTitlesMenuKey,
        MenuTemplateSeed.UserManagerGroupsMenuKey,
    };

    public static TheoryData<string> ContentKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in ContentKeyList) data.Add(key);
        return data;
    }

    [Theory]
    [MemberData(nameof(ContentKeys))]
    public async Task Seed_CreatesEachContentTemplateOnce_AndItPassesServiceValidation(string key)
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;

        Assert.Single(seeded, t => t.Key == key);

        // The seed bypasses the service, so round-trip the template through the real mapping
        // profile and the service's create-path validation - proves the API would accept it.
        var repo = new Mock<IMenuTemplateRepository>();
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>())).Returns(Task.CompletedTask);
        var service = new MenuTemplateService(repo.Object, new Mock<IMinecraftMaterialRefRepository>().Object, _mapper);

        var dto = _mapper.Map<MenuTemplateDto>(Single(seeded, key));
        var created = await service.CreateAsync(dto);

        Assert.Equal(key, created.Key);
    }

    [Fact]
    public async Task Hub_ShipsEveryTileBehindMenuAvailable_AndTheManagerTileBehindItsPermission()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var hub = Single(seeded, MenuTemplateSeed.HubMenuKey);

        Assert.Equal(3, hub.Height);
        var expectedTargets = new Dictionary<int, string>
        {
            [4] = MenuTemplateSeed.ProfileMenuKey,
            [10] = MenuTemplateSeed.KitsOverviewMenuKey,
            [12] = MenuTemplateSeed.SiegeOverviewMenuKey,
            [14] = MenuTemplateSeed.ItemsCatalogMenuKey,
            [16] = MenuTemplateSeed.PremiumTiersMenuKey,
            [22] = MenuTemplateSeed.UserManagerMenuKey,
        };
        foreach (var (slot, target) in expectedTargets)
        {
            var tile = ItemAt(hub, slot);
            var open = Assert.Single(tile.Actions);
            Assert.Equal("menu.open", open.ActionTypeId);
            Assert.Equal("{\"key\":\"" + target + "\"}", open.ParamsJson);
            var available = Assert.Single(tile.Conditions);
            Assert.Equal("menu-available", available.ConditionTypeId);
            Assert.Equal(MenuConditionPhase.Render, available.Phase);
            Assert.Equal("{\"key\":\"" + target + "\"}", available.ParamsJson);
        }

        var manager = ItemAt(hub, 22);
        Assert.Equal(MenuTemplateSeed.UserManagePermission, manager.VisibilityPermission);
        Assert.Equal(MenuTemplateSeed.UserManagePermission, manager.ActionPermission);
        Assert.All(expectedTargets.Keys.Where(s => s != 22), s => Assert.Null(ItemAt(hub, s).VisibilityPermission));

        var head = ItemAt(hub, 4);
        Assert.Equal("$player.getName$", Binding(head, "SkullOwner"));

        var back = ItemAt(hub, 8);
        Assert.Equal("menu.back", Assert.Single(back.Actions).ActionTypeId);
        Assert.Equal("&c$menu.getBackLabel$", Binding(back, "Name"));
    }

    [Fact]
    public async Task KitsOverview_RowTemplatePicksClaimOrConfirmedPurchasePerRow()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var kits = Single(seeded, MenuTemplateSeed.KitsOverviewMenuKey);

        Assert.Equal(6, kits.Height);
        Assert.Equal(20, kits.AutoRefreshTicks);
        var grid = kits.Sections.Single(s => s.Name == "Kits");
        Assert.Equal("kits.available", grid.ContentSourceId);
        Assert.Equal((9, 9, 4), (grid.DisplaySlot, grid.Width, grid.Height));

        var row = Assert.Single(grid.Items, i => i.IsRowTemplate);
        Assert.Equal(2, row.Actions.Count);
        var claim = row.Actions.Single(a => a.ActionTypeId == "kits.claim");
        Assert.Equal("{\"kitId\":\"$row.getKitId$\"}", claim.ParamsJson);
        Assert.Contains("\"expected\":\"false\"", Assert.Single(claim.Conditions).ParamsJson);
        var purchase = row.Actions.Single(a => a.ActionTypeId == "menu.confirm.request");
        using (var parsed = JsonDocument.Parse(purchase.ParamsJson))
        {
            Assert.Equal("kits.purchase", parsed.RootElement.GetProperty("actionTypeId").GetString());
            using var inner = JsonDocument.Parse(parsed.RootElement.GetProperty("actionParamsJson").GetString()!);
            Assert.Equal("$row.getKitId$", inner.RootElement.GetProperty("kitId").GetString());
        }
        Assert.Contains("\"expected\":\"true\"", Assert.Single(purchase.Conditions).ParamsJson);
        Assert.All(row.Conditions, c => Assert.Equal(MenuConditionPhase.Render, c.Phase));
        Assert.Equal(VariableRefreshPolicy.Ttl,
            row.VariableBindings.Single(b => b.Expression == "$row.getCooldownText$").RefreshPolicy);

        Assert.Equal("menu.page.prev", Assert.Single(ItemAt(kits, 45).Actions).ActionTypeId);
        Assert.Equal("menu.page.next", Assert.Single(ItemAt(kits, 53).Actions).ActionTypeId);
        foreach (var slot in new[] { 48, 50 })
            Assert.Equal("kits.purchase-pending", Assert.Single(ItemAt(kits, slot).Conditions).ConditionTypeId);
    }

    [Fact]
    public async Task Profile_ReadsTheProfileRootAndListsTitleBrackets()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var profile = Single(seeded, MenuTemplateSeed.ProfileMenuKey);

        Assert.Equal(6, profile.Height);
        Assert.Equal("$player.getName$", Binding(ItemAt(profile, 0), "SkullOwner"));
        Assert.Contains(ItemAt(profile, 1).VariableBindings, b => b.Expression == "$profile.getPrestigeLine$");
        Assert.Equal("$profile.getProgressLines$", Binding(ItemAt(profile, 2), "Lore"));
        Assert.Equal("menu.back", Assert.Single(ItemAt(profile, 8).Actions).ActionTypeId);
        var titles = profile.Sections.Single(s => s.Name == "Titles");
        Assert.Equal("titles.brackets", titles.ContentSourceId);
        var row = Assert.Single(titles.Items, i => i.IsRowTemplate);
        Assert.Equal("$row.getDisplayMode$", Binding(row, "DisplayMode"));
        Assert.Empty(row.Actions);
    }

    [Fact]
    public async Task ItemsCatalog_IsASearchablePagedCatalogWithoutFilters()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var catalog = Single(seeded, MenuTemplateSeed.ItemsCatalogMenuKey);

        var grid = catalog.Sections.Single(s => s.Name == "Items");
        Assert.Equal("catalog.itemblueprints", grid.ContentSourceId);
        Assert.True(grid.Searchable);
        Assert.DoesNotContain(grid.Items, i => i.IsRowTemplate);
        var actions = grid.Items.SelectMany(i => i.Actions).Select(a => a.ActionTypeId).ToList();
        Assert.Equal(new[] { "menu.page.prev", "menu.page.next", "menu.search.prompt" }, actions);
        Assert.DoesNotContain(actions, a => a.StartsWith("menu.filter"));
        Assert.Contains(ItemAt(catalog, 4).VariableBindings, b => b.Expression == "$itemsCatalog.getTotalLine$");
    }

    [Fact]
    public async Task PremiumTiers_IsAReadOnlyRowOfTiersUnderTheViewersTier()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var premium = Single(seeded, MenuTemplateSeed.PremiumTiersMenuKey);

        Assert.Equal(3, premium.Height);
        Assert.Equal("$premium.getTierLine$", Binding(ItemAt(premium, 4), "Lore"));
        var tiers = premium.Sections.Single(s => s.Name == "Tiers");
        Assert.Equal("premium.tiers", tiers.ContentSourceId);
        Assert.Empty(Assert.Single(tiers.Items, i => i.IsRowTemplate).Actions);
    }

    [Fact]
    public async Task UserManager_EverySectionNeedsTheManageNode_AndRowsOpenTheEditorWithStepDefaults()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var managerKeys = new[]
        {
            MenuTemplateSeed.UserManagerMenuKey, MenuTemplateSeed.UserManagerEditMenuKey,
            MenuTemplateSeed.UserManagerTitlesMenuKey, MenuTemplateSeed.UserManagerGroupsMenuKey,
        };
        Assert.All(managerKeys.SelectMany(k => Single(seeded, k).Sections),
            s => Assert.Equal(MenuTemplateSeed.UserManagePermission, s.VisibilityPermission));

        var row = Single(seeded, MenuTemplateSeed.UserManagerMenuKey).Sections.Single(s => s.Name == "Players")
            .Items.Single(i => i.IsRowTemplate);
        using var open = JsonDocument.Parse(Assert.Single(row.Actions).ParamsJson);
        Assert.Equal(MenuTemplateSeed.UserManagerEditMenuKey, open.RootElement.GetProperty("key").GetString());
        Assert.Equal("$row.getUserId$", open.RootElement.GetProperty("ctx.userId").GetString());
        Assert.Equal("100", open.RootElement.GetProperty("state.pm.coinStep").GetString());
        Assert.Equal("10", open.RootElement.GetProperty("state.pm.gemStep").GetString());
        Assert.Equal("100", open.RootElement.GetProperty("state.pm.xpStep").GetString());
    }

    [Fact]
    public async Task UserManagerEdit_SteppersAdjustByTheSessionStepAndEveryEditIsGated()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var edit = Single(seeded, MenuTemplateSeed.UserManagerEditMenuKey);

        var minus = ItemAt(edit, 10);
        Assert.Equal("knk.admin.user.coins", minus.ActionPermission);
        Assert.Contains("\"delta\":\"-$state.pm.coinStep$\"", Assert.Single(minus.Actions).ParamsJson);
        Assert.Contains("\"delta\":\"$state.pm.coinStep$\"", Assert.Single(ItemAt(edit, 12).Actions).ParamsJson);
        var value = ItemAt(edit, 11);
        Assert.Equal("menu.state.cycle", Assert.Single(value.Actions).ActionTypeId);
        Assert.Contains("1,10,100,1000,10000", value.Actions[0].ParamsJson);
        Assert.Equal("knk.admin.user.xp", ItemAt(edit, 28).ActionPermission);

        // Every mutating item carries the outranks Click condition.
        foreach (var slot in new[] { 10, 12, 19, 21, 28, 30, 16, 23, 24, 32, 33 })
            Assert.Contains(ItemAt(edit, slot).Conditions, c => c.ConditionTypeId == "users.outranks-target" && c.Phase == MenuConditionPhase.Click);

        foreach (var (slot, action) in new[] { (32, "users.kick"), (33, "users.ban") })
        {
            var request = Assert.Single(ItemAt(edit, slot).Actions);
            Assert.Equal("menu.confirm.request", request.ActionTypeId);
            using var parsed = JsonDocument.Parse(request.ParamsJson);
            Assert.Equal(action, parsed.RootElement.GetProperty("actionTypeId").GetString());
        }
        var freeze = ItemAt(edit, 24);
        Assert.Equal(2, freeze.Actions.Count);
        Assert.All(freeze.Actions, a => Assert.Contains(a.Conditions, c => c.ConditionTypeId == "permission-node"));
        Assert.Equal("users.pending", Assert.Single(ItemAt(edit, 48).Conditions).ConditionTypeId);
        Assert.Equal("users.target", edit.Sections.Single(s => s.Name == "Target").ContentSourceId);
    }

    [Fact]
    public async Task UserManagerSubMenus_ConfirmTitleChangesAndGroupRemovals()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;

        var titleRow = Single(seeded, MenuTemplateSeed.UserManagerTitlesMenuKey).Sections.Single(s => s.Name == "Titles")
            .Items.Single(i => i.IsRowTemplate);
        using (var parsed = JsonDocument.Parse(Assert.Single(titleRow.Actions).ParamsJson))
            Assert.Equal("users.set-title", parsed.RootElement.GetProperty("actionTypeId").GetString());
        Assert.Equal("$row.getPickerDisplayMode$", Binding(titleRow, "DisplayMode"));

        var groupRow = Single(seeded, MenuTemplateSeed.UserManagerGroupsMenuKey).Sections.Single(s => s.Name == "Groups")
            .Items.Single(i => i.IsRowTemplate);
        Assert.Equal("users.group", groupRow.Actions.Single(a => a.SortOrder == 0).ActionTypeId);
        Assert.Equal("menu.confirm.request", groupRow.Actions.Single(a => a.SortOrder == 1).ActionTypeId);
        Assert.Equal("knk.admin.user.group", groupRow.ActionPermission);
    }

    /// <summary>
    /// Writes every content template exactly as <c>GET /api/MenuTemplates/by-key/{key}</c> would
    /// return it (mapping profile + the API's JSON options) - the fixture knk-plugin's
    /// <c>ContentSeedContractTest</c> validates against the registered menu features. Always
    /// checks the export round-trips; writes the file only when
    /// <c>KNK_MENU_CONTENT_SEED_EXPORT</c> names a path (regenerate after changing a content seed:
    /// <c>KNK_MENU_CONTENT_SEED_EXPORT=../knk-plugin/knk-paper/src/test/resources/menu/content-seeds.json
    /// dotnet test ... --filter ContentSeeds_ExportAsApiJson</c>).
    /// </summary>
    [Fact]
    public async Task ContentSeeds_ExportAsApiJson()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;

        var keys = ContentKeyList.ToList();
        var dtos = keys.Select(key => _mapper.Map<MenuTemplateDto>(Single(seeded, key))).ToList();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(dtos, options);

        var roundTripped = JsonSerializer.Deserialize<List<MenuTemplateDto>>(json, options)!;
        Assert.Equal(keys, roundTripped.Select(t => t.Key).ToList());

        var exportPath = Environment.GetEnvironmentVariable("KNK_MENU_CONTENT_SEED_EXPORT");
        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exportPath))!);
            await File.WriteAllTextAsync(exportPath, json + "\n");
        }
    }
}

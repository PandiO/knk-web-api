using AutoMapper;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#870 (siege shop DESIGN § 4.3, § 6.1.8, P03, P26, acceptance A29): the menu item section of the
/// game settings: its seeded defaults, saving the look, clicks and overrides, the validation, and the realtime event
/// that tells the game server to reload.
/// </summary>
public class MenuItemSettingsTests
{
    private readonly Mock<IGameSettingsRepository> _repo = new();
    private readonly Mock<IPermissionGroupRepository> _groupRepo = new();
    private readonly GameSettingsService _service;
    private GameSettings? _stored;

    public MenuItemSettingsTests()
    {
        _repo.Setup(r => r.GetSingletonAsync()).ReturnsAsync(() => _stored);
        _repo.Setup(r => r.UpsertAsync(It.IsAny<GameSettings>())).ReturnsAsync((GameSettings s) => _stored = s);
        _groupRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<PermissionGroup>());
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<GameSettingsMappingProfile>()).CreateMapper();
        _service = new GameSettingsService(_repo.Object, mapper, _groupRepo.Object);
    }

    private static GameSettingsUpdateDto Update(MenuItemSettingsDto? menuItem) => new() { JoinSpawnMode = "WorldSpawn", MenuItem = menuItem };

    private static MenuItemActionDto Open(string key) => new() { Type = "OpenMenu", MenuKey = key };

    private static void AssertOpens(Dictionary<string, MenuItemActionDto> table, string click, string key)
    {
        Assert.True(table.TryGetValue(click, out var action), $"{click} is not set");
        Assert.Equal("OpenMenu", action!.Type);
        Assert.Equal(key, action.MenuKey);
    }

    [Fact]
    public async Task FreshDatabase_ReadsTodaysConfigDefaults_AndTheSiegeOverrides_A29()
    {
        var item = (await _service.GetAsync()).MenuItem;

        // Today's menus.menu-item.* keys of the plugin's config.yml.
        Assert.True(item.Enabled);
        Assert.Equal("all", item.GiveTo);
        Assert.True(item.GiveOnJoin);
        Assert.True(item.KeepOnDeath);
        Assert.Equal("NETHER_STAR", item.Material);
        Assert.Equal("&6&lMenu &7(click to open)", item.Name);
        Assert.Equal(new[] { "&7Opens the Knights & Kings menu (&e/menu&7)." }, item.Lore);
        Assert.Equal(8, item.Slot);
        Assert.False(item.Droppable);
        Assert.False(item.Movable);
        Assert.Equal(MenuItemSettings.ClickTypes.Length, item.Clicks.Count);
        foreach (var click in MenuItemSettings.ClickTypes)
        {
            AssertOpens(item.Clicks, click, "main");
        }

        // § 4.3.4.
        Assert.Equal(3, item.Overrides.Count);
        foreach (var situation in new[] { "siege.match", "siege.survival.match" })
        {
            var table = item.Overrides[situation];
            Assert.Equal(3, table.Count);
            AssertOpens(table, "LEFT_CLICK", "siege.information");
            AssertOpens(table, "RIGHT_CLICK", "siege.information");
            AssertOpens(table, "SHIFT_RIGHT_CLICK", "siege.shop");
        }
        var hub = item.Overrides["siege.hub"];
        Assert.Equal(2, hub.Count);
        AssertOpens(hub, "LEFT_CLICK", "siege.information");
        AssertOpens(hub, "RIGHT_CLICK", "siege.information");
    }

    [Fact]
    public async Task ExistingRowWithoutTheColumn_OrUnreadableJson_ReadsTheDefaults()
    {
        _stored = new GameSettings { MenuItemSettingsJson = null };
        Assert.Equal("NETHER_STAR", (await _service.GetAsync()).MenuItem.Material);

        _stored = new GameSettings { MenuItemSettingsJson = "{not json" };
        var item = (await _service.GetAsync()).MenuItem;
        Assert.Equal(3, item.Overrides.Count);
    }

    [Fact]
    public async Task Save_StoresLookClicksAndOverrides_AndReadsThemBack_A29()
    {
        var edit = MenuItemSettings.Defaults();
        edit.Name = "&bKnK menu";
        edit.Lore = new List<string> { "&7Line one", "&7Line two" };
        edit.Material = "compass";
        edit.Slot = 4;
        edit.GiveTo = "Bedrock";
        edit.Movable = true;
        edit.Clicks["shift_left_click"] = new MenuItemActionDto { Type = "runcommand", Command = "/siege points" };
        edit.Clicks.Remove("SHIFT_LEFT_CLICK");
        edit.Overrides["siege.match"]["INVENTORY_CLICK"] = new MenuItemActionDto { Type = "None", MenuKey = "ignored" };

        var item = (await _service.UpdateAsync(Update(edit))).MenuItem;

        Assert.Equal("&bKnK menu", item.Name);
        Assert.Equal(new[] { "&7Line one", "&7Line two" }, item.Lore);
        Assert.Equal("COMPASS", item.Material);
        Assert.Equal(4, item.Slot);
        Assert.Equal("bedrock", item.GiveTo);
        Assert.True(item.Movable);
        var shiftLeft = item.Clicks["SHIFT_LEFT_CLICK"];
        Assert.Equal("RunCommand", shiftLeft.Type);
        Assert.Equal("siege points", shiftLeft.Command);
        Assert.Null(shiftLeft.MenuKey);
        var none = item.Overrides["siege.match"]["INVENTORY_CLICK"];
        Assert.Equal("None", none.Type);
        Assert.Null(none.MenuKey);
        AssertOpens(item.Overrides["siege.match"], "SHIFT_RIGHT_CLICK", "siege.shop");

        // Stored as JSON on the settings row and read back the same.
        Assert.Contains("\"SHIFT_LEFT_CLICK\"", _stored!.MenuItemSettingsJson);
        var reread = (await _service.GetAsync()).MenuItem;
        Assert.Equal("siege points", reread.Clicks["SHIFT_LEFT_CLICK"].Command);
    }

    [Fact]
    public async Task Save_WithoutTheSection_KeepsTheStoredOne()
    {
        var edit = MenuItemSettings.Defaults();
        edit.Name = "&cKept";
        await _service.UpdateAsync(Update(edit));

        var item = (await _service.UpdateAsync(Update(null))).MenuItem;

        Assert.Equal("&cKept", item.Name);
    }

    [Fact]
    public async Task Save_DropsEmptyOverrides_AndAClickTableMayBeEmpty()
    {
        var edit = MenuItemSettings.Defaults();
        edit.Overrides["siege.hub"] = new Dictionary<string, MenuItemActionDto>();
        edit.Clicks = new Dictionary<string, MenuItemActionDto>();

        var item = (await _service.UpdateAsync(Update(edit))).MenuItem;

        Assert.False(item.Overrides.ContainsKey("siege.hub"));
        Assert.Equal(2, item.Overrides.Count);
        Assert.Empty(item.Clicks);
    }

    public static IEnumerable<object[]> BadSections()
    {
        MenuItemSettingsDto With(Action<MenuItemSettingsDto> change)
        {
            var dto = MenuItemSettings.Defaults();
            change(dto);
            return dto;
        }

        yield return new object[] { With(d => d.Clicks["MIDDLE_CLICK"] = Open("main")), "not a click type" };
        yield return new object[] { With(d => d.Overrides["arena.duel"] = new() { ["LEFT_CLICK"] = Open("main") }), "not a known situation" };
        yield return new object[] { With(d => d.Overrides["siege.match"]["LEFT_CLICK"] = new MenuItemActionDto { Type = "Teleport" }), "type must be one of" };
        yield return new object[] { With(d => d.Clicks["LEFT_CLICK"] = new MenuItemActionDto { Type = "OpenMenu" }), "needs a menu key" };
        yield return new object[] { With(d => d.Clicks["LEFT_CLICK"] = Open("Siege Shop")), "is not a menu key" };
        yield return new object[] { With(d => d.Clicks["LEFT_CLICK"] = new MenuItemActionDto { Type = "RunCommand", Command = " / " }), "needs a command" };
        yield return new object[] { With(d => d.Clicks["LEFT_CLICK"] = new MenuItemActionDto { Type = "RunCommand", Command = "say a\nop me" }), "one line" };
        yield return new object[] { With(d => d.Slot = 36), "slot must be 0 to 35" };
        yield return new object[] { With(d => d.Slot = -1), "slot must be 0 to 35" };
        yield return new object[] { With(d => d.GiveTo = "java"), "giveTo must be one of" };
        yield return new object[] { With(d => d.Material = "nether star"), "material must be" };
        yield return new object[] { With(d => d.Material = ""), "material must be" };
        yield return new object[] { With(d => d.Name = new string('a', 257)), "name must be at most" };
        yield return new object[] { With(d => d.Lore = Enumerable.Repeat("x", 21).ToList()), "lore has at most" };
        yield return new object[] { With(d => d.Clicks = new() { ["left_click"] = Open("main"), ["LEFT_CLICK"] = Open("main") }), "listed twice" };
    }

    [Theory]
    [MemberData(nameof(BadSections))]
    public async Task Save_WithABadValue_IsRefusedWithTheReason_AndNothingChanges(MenuItemSettingsDto bad, string reason)
    {
        var good = MenuItemSettings.Defaults();
        good.Name = "&aBefore";
        await _service.UpdateAsync(Update(good));
        var before = _stored!.MenuItemSettingsJson;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(Update(bad)));

        Assert.Contains(reason, ex.Message);
        Assert.StartsWith("menuItem.", ex.Message);
        Assert.Equal(before, _stored!.MenuItemSettingsJson);
    }

    [Fact]
    public void SavingTheSection_TellsTheGameServerToReload_A29()
    {
        // The plugin reloads the game settings on GameSettingsChanged, so a saved card reaches online players without
        // a restart (§ 4.3.5); the 30-second runtime-world report alone still sends nothing.
        var change = new RealtimeEntityChange(typeof(GameSettings), RealtimeEntityState.Modified,
            new HashSet<string>(StringComparer.Ordinal) { nameof(GameSettings.MenuItemSettingsJson), nameof(GameSettings.UpdatedAt) },
            new Dictionary<string, object?>(StringComparer.Ordinal));

        var events = RealtimeEntityChangeMap.Map(new[] { change });

        var single = Assert.Single(events);
        Assert.Equal(ConfigEventTypes.GameSettingsChanged, single.Type);
        Assert.Equal(new ConfigChangedPayload(ConfigScopes.Game), single.Payload);
    }
}

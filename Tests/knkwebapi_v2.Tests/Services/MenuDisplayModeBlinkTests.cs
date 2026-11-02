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
/// PandiO/knk-workspace#49: MenuDisplayMode has Blink (the plugin's BLINK, KNG-125), so staff can
/// set a fixed DisplayMode of Blink through the CRUD API instead of only through a DisplayMode
/// binding. The column stores the name, and the existing values keep their numbers.
/// </summary>
public class MenuDisplayModeBlinkTests
{
    private readonly Mock<IMenuTemplateRepository> _repo = new();
    private readonly Mock<IMinecraftMaterialRefRepository> _materialRepo = new();
    private readonly MenuTemplateService _service;
    private MenuTemplate? _added;

    public MenuDisplayModeBlinkTests()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();
        _repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        _repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>()))
            .Callback<MenuTemplate>(t => _added = t)
            .Returns(Task.CompletedTask);
        _service = new MenuTemplateService(_repo.Object, _materialRepo.Object, mapper);
    }

    private static MenuTemplateDto TemplateWithItem(string displayMode) => new()
    {
        Key = "test.blink",
        Name = "Blink test",
        Height = 1,
        Sections =
        {
            new MenuSectionTemplateDto
            {
                Name = "Buttons",
                Kind = "StaticButtons",
                Width = 9,
                Height = 1,
                Items = { new MenuItemTemplateDto { DisplayMode = displayMode } },
            },
        },
    };

    [Theory]
    [InlineData("Blink")]
    [InlineData("blink")]
    [InlineData("BLINK")]
    public async Task CreateAsync_BlinkDisplayMode_IsStoredAndReturnedAsBlink(string input)
    {
        var result = await _service.CreateAsync(TemplateWithItem(input));

        Assert.Equal(MenuDisplayMode.Blink, _added!.Sections[0].Items[0].DisplayMode);
        // The plugin parses this name (MenuEnumParsing, case and underscores ignored) into BLINK.
        Assert.Equal("Blink", result.Sections[0].Items[0].DisplayMode);
    }

    [Fact]
    public async Task CreateAsync_UnknownDisplayMode_StillThrows()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(TemplateWithItem("Flash")));
        Assert.Contains("MenuDisplayMode", ex.Message);
    }

    [Fact]
    public void MenuDisplayMode_ExistingValuesKeepTheirNumbers_AndBlinkIsAppended()
    {
        Assert.Equal(
            new[] { "Normal", "Disabled", "Highlight", "Hidden", "Blink" },
            Enum.GetNames<MenuDisplayMode>());
        Assert.Equal(0, (int)MenuDisplayMode.Normal);
        Assert.Equal(3, (int)MenuDisplayMode.Hidden);
        Assert.Equal(4, (int)MenuDisplayMode.Blink);
    }

    [Fact]
    public void DisplayModeColumn_StoresTheNameInAVarchar20_SoBlinkNeedsNoMigration()
    {
        using var ctx = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase("display-mode-blink").Options);
        var property = ctx.Model.FindEntityType(typeof(MenuItemTemplate))!.FindProperty(nameof(MenuItemTemplate.DisplayMode))!;

        Assert.Equal(typeof(string), property.GetProviderClrType());
        Assert.Equal(20, property.GetMaxLength());
        Assert.All(Enum.GetNames<MenuDisplayMode>(), name => Assert.True(name.Length <= 20));
    }
}

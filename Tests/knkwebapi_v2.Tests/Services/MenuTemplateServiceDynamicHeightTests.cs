using AutoMapper;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Menu follow-up 2026-09-26: MenuTemplate.MinHeight / BackgroundMaterial and
/// MenuSectionTemplate.MinHeight - validated and round-tripped through the DTOs.
/// </summary>
public class MenuTemplateServiceDynamicHeightTests
{
    private readonly Mock<IMenuTemplateRepository> _repo = new();
    private readonly Mock<IMinecraftMaterialRefRepository> _materialRepo = new();
    private readonly MenuTemplateService _service;
    private MenuTemplate? _added;

    public MenuTemplateServiceDynamicHeightTests()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();
        _repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        _repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>()))
            .Callback<MenuTemplate>(t => _added = t)
            .Returns(Task.CompletedTask);
        _service = new MenuTemplateService(_repo.Object, _materialRepo.Object, mapper);
    }

    private static MenuTemplateDto Template(int? minHeight, int? sectionMinHeight, string? background = null) => new()
    {
        Key = "test.dynamic",
        Name = "Dynamic test",
        Height = 4,
        MinHeight = minHeight,
        BackgroundMaterial = background,
        Sections =
        {
            new MenuSectionTemplateDto { Name = "Grid", Kind = "ContentGrid", Width = 9, Height = 2, MinHeight = sectionMinHeight },
        },
    };

    [Fact]
    public async Task CreateAsync_PersistsAndMapsMinHeightsAndBackground()
    {
        var result = await _service.CreateAsync(Template(2, 0, "  BLACK_STAINED_GLASS_PANE "));

        Assert.Equal(2, _added!.MinHeight);
        Assert.Equal(0, _added.Sections[0].MinHeight);
        Assert.Equal("BLACK_STAINED_GLASS_PANE", _added.BackgroundMaterial);
        Assert.Equal(2, result.MinHeight);
        Assert.Equal(0, result.Sections[0].MinHeight);
        Assert.Equal("BLACK_STAINED_GLASS_PANE", result.BackgroundMaterial);
    }

    [Fact]
    public async Task CreateAsync_UnsetValuesStayNull()
    {
        await _service.CreateAsync(Template(null, null, " "));

        Assert.Null(_added!.MinHeight);
        Assert.Null(_added.Sections[0].MinHeight);
        Assert.Null(_added.BackgroundMaterial);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(5, null)]
    [InlineData(null, -1)]
    [InlineData(null, 3)]
    public async Task CreateAsync_MinHeightOutsideItsHeight_Throws(int? minHeight, int? sectionMinHeight)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Template(minHeight, sectionMinHeight)));
        Assert.Contains("MinHeight", ex.Message);
    }
}

using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Territory;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Territory;

/// <summary>
/// The production section of the game settings (PandiO/knk-workspace#326, G16, territory DESIGN § 1):
/// <c>Production:Enabled</c> defaults off; the territory ranges default to base 100, factor 2, capital × 1.5.
/// </summary>
public class ProductionSettingsTests
{
    private readonly Mock<IGameSettingsRepository> _repo = new();
    private readonly GameSettingsService _service;
    private GameSettings? _stored;

    public ProductionSettingsTests()
    {
        _repo.Setup(r => r.GetSingletonAsync()).ReturnsAsync(() => _stored);
        _repo.Setup(r => r.UpsertAsync(It.IsAny<GameSettings>())).ReturnsAsync((GameSettings s) => _stored = s);
        var groups = new Mock<IPermissionGroupRepository>();
        groups.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<PermissionGroup>());
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<GameSettingsMappingProfile>()).CreateMapper();
        _service = new GameSettingsService(_repo.Object, mapper, groups.Object);
    }

    [Fact]
    public async Task Defaults_FlagOff_AndTheDesignRanges()
    {
        var read = await _service.GetAsync();

        Assert.False(read.Production.Enabled);
        Assert.Equal((100, 2.0, 1.5), (read.Production.Territory.BaseRange, read.Production.Territory.PowerFactor,
            read.Production.Territory.CapitalMultiplier));
    }

    [Fact]
    public async Task Update_StoresTheSection_AndNullKeepsIt()
    {
        await _service.UpdateAsync(new GameSettingsUpdateDto
        {
            Production = new ProductionSettingsDto
            {
                Enabled = true, Territory = new TerritorySettingsDto { BaseRange = 64, PowerFactor = 1, CapitalMultiplier = 2 },
            },
        });
        var read = await _service.UpdateAsync(new GameSettingsUpdateDto { Production = null });

        Assert.True(read.Production.Enabled);
        Assert.Equal(64, read.Production.Territory.BaseRange);
        Assert.Equal(2, ProductionSettings.Read(_stored).Territory.CapitalMultiplier);
    }

    [Fact]
    public async Task Update_OutOfBounds_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(new GameSettingsUpdateDto
        {
            Production = new ProductionSettingsDto { Territory = new TerritorySettingsDto { CapitalMultiplier = 0.5 } },
        }));
        Assert.Null(_stored?.ProductionSettingsJson);
    }
}

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
/// Siege Phase 1 (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md Phase 1): Clan validation -
/// one default clan per town, banner/town existence, chat colour names.
/// </summary>
public class ClanServiceTests
{
    private readonly Mock<IClanRepository> _repo = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<ClanMappingProfile>()).CreateMapper();
    private readonly ClanService _service;
    private Clan? _added;

    public ClanServiceTests()
    {
        _repo.Setup(r => r.BannerDesignExistsAsync(1)).ReturnsAsync(true);
        _repo.Setup(r => r.TownExistsAsync(7)).ReturnsAsync(true);
        _repo.Setup(r => r.AddAsync(It.IsAny<Clan>()))
            .Callback<Clan>(c => { c.Id = 42; _added = c; })
            .Returns(Task.CompletedTask);
        _service = new ClanService(_repo.Object, _mapper);
    }

    private static ClanUpsertDto Dto(int? townId = null, string? chatColor = null) => new()
    {
        Name = "Cinix Garrison",
        IsNpc = true,
        ChatColor = chatColor,
        BannerDesignId = 1,
        DefaultForTownId = townId
    };

    [Fact]
    public async Task Create_DefaultsChatColourToWhite_AndNormalizesCase()
    {
        await _service.CreateAsync(Dto());
        Assert.Equal("WHITE", _added!.ChatColor);

        await _service.CreateAsync(Dto(chatColor: "dark_red"));
        Assert.Equal("DARK_RED", _added!.ChatColor);
    }

    [Theory]
    [InlineData("BOLD")]
    [InlineData("PINK")]
    [InlineData("#ff0000")]
    public async Task Create_RejectsNonColourChatColor(string chatColor)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Dto(chatColor: chatColor)));
    }

    [Fact]
    public async Task Create_RejectsSecondDefaultClanForSameTown()
    {
        _repo.Setup(r => r.GetDefaultForTownAsync(7)).ReturnsAsync(new Clan { Id = 3, Name = "Old default", DefaultForTownId = 7 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Dto(townId: 7)));
        Assert.Contains("Old default", ex.Message);
        _repo.Verify(r => r.AddAsync(It.IsAny<Clan>()), Times.Never);
    }

    [Fact]
    public async Task Update_KeepingItsOwnDefaultTown_IsAllowed()
    {
        var existing = new Clan { Id = 3, Name = "Garrison", BannerDesignId = 1, DefaultForTownId = 7 };
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(existing);
        _repo.Setup(r => r.GetDefaultForTownAsync(7)).ReturnsAsync(existing);

        await _service.UpdateAsync(3, Dto(townId: 7));

        _repo.Verify(r => r.UpdateAsync(existing), Times.Once);
        Assert.Equal("Cinix Garrison", existing.Name);
    }

    [Fact]
    public async Task Create_TreatsZeroTownAsNoTown()
    {
        await _service.CreateAsync(Dto(townId: 0));

        Assert.Null(_added!.DefaultForTownId);
        _repo.Verify(r => r.GetDefaultForTownAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Create_RejectsUnknownTown()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Dto(townId: 99)));
    }

    [Fact]
    public async Task Create_RequiresExistingBanner()
    {
        var dto = Dto();
        dto.BannerDesignId = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));

        dto.BannerDesignId = 5;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Fact]
    public async Task Read_IncludesBannerGraphAndTownName()
    {
        var clan = new Clan
        {
            Id = 3,
            Name = "Garrison",
            ChatColor = "GOLD",
            BannerDesignId = 1,
            BannerDesign = new BannerDesign
            {
                Id = 1,
                Name = "Crown",
                Layers = { new BannerLayer { Id = 2, SortOrder = 1, PatternKey = "minecraft:border" }, new BannerLayer { Id = 1, SortOrder = 0, PatternKey = "minecraft:cross" } }
            },
            DefaultForTownId = 7,
            DefaultForTown = new Town { Id = 7, Name = "Cinix" }
        };
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(clan);

        var dto = await _service.GetByIdAsync(3);

        Assert.Equal("Cinix", dto!.DefaultForTownName);
        Assert.Equal(new[] { "minecraft:cross", "minecraft:border" }, dto.BannerDesign!.Layers.Select(l => l.PatternKey).ToArray());
    }
}

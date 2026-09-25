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
/// InventoryMenu Phase 9 (E1-E9, docs/specs/inventory-menu/IMPLEMENTATION_PLAN.md
/// "Phase 9"): the web-api half is schema + DTO + validation - AutoRefreshTicks (E4),
/// IsRowTemplate (E3), ConditionBinding.Phase (E5), empty lore expressions (E8) -
/// plus the create-only example.domain seeds.
/// </summary>
public class MenuTemplateServicePhase9Tests
{
    private readonly Mock<IMenuTemplateRepository> _repo = new();
    private readonly Mock<IMinecraftMaterialRefRepository> _materialRepo = new();
    private readonly IMapper _mapper;
    private readonly MenuTemplateService _service;
    private MenuTemplate? _added;

    public MenuTemplateServicePhase9Tests()
    {
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();
        _repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        _repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>()))
            .Callback<MenuTemplate>(t => _added = t)
            .Returns(Task.CompletedTask);
        _service = new MenuTemplateService(_repo.Object, _materialRepo.Object, _mapper);
    }

    private static MenuTemplateDto Template(params MenuSectionTemplateDto[] sections) => new()
    {
        Key = "test.phase9",
        Name = "Phase 9 test",
        Height = 3,
        Sections = sections.ToList(),
    };

    private static MenuSectionTemplateDto Section(string name, string? contentSourceId, params MenuItemTemplateDto[] items) => new()
    {
        Name = name,
        Kind = "ContentGrid",
        Width = 9,
        Height = 1,
        ContentSourceId = contentSourceId,
        Items = items.ToList(),
    };

    private static MenuItemTemplateDto RowTemplate(int? slotOverride = null) => new()
    {
        IsRowTemplate = true,
        SlotOverride = slotOverride,
        VariableBindings = { new VariableBindingDto { TargetProperty = "Name", Expression = "$row.getName$" } },
    };

    [Fact]
    public async Task CreateAsync_RowTemplateWithContentSource_IsPersisted()
    {
        var dto = Template(Section("Rows", "example.rows", RowTemplate()));

        var result = await _service.CreateAsync(dto);

        Assert.True(_added!.Sections[0].Items[0].IsRowTemplate);
        Assert.True(result.Sections[0].Items[0].IsRowTemplate);
    }

    [Fact]
    public async Task CreateAsync_RowTemplateWithoutContentSource_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Template(Section("Rows", null, RowTemplate()))));
        Assert.Contains("ContentSourceId", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_TwoRowTemplatesInOneSection_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(Template(Section("Rows", "example.rows", RowTemplate(), RowTemplate()))));
        Assert.Contains("at most one", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_RowTemplateWithSlotOverride_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(Template(Section("Rows", "example.rows", RowTemplate(slotOverride: 3)))));
        Assert.Contains("SlotOverride", ex.Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(20, 20)]
    public async Task CreateAsync_AutoRefreshTicks_NonPositiveMeansOff(int? input, int? expected)
    {
        var dto = Template(Section("Rows", null));
        dto.AutoRefreshTicks = input;

        var result = await _service.CreateAsync(dto);

        Assert.Equal(expected, _added!.AutoRefreshTicks);
        Assert.Equal(expected, result.AutoRefreshTicks);
    }

    [Fact]
    public async Task CreateAsync_ConditionPhase_DefaultsToClickAndParsesRender()
    {
        var item = new MenuItemTemplateDto
        {
            Conditions =
            {
                new ConditionBindingDto { ConditionTypeId = "always" },
                new ConditionBindingDto { ConditionTypeId = "value-equals", Phase = "render", SortOrder = 1 },
            },
        };

        var result = await _service.CreateAsync(Template(Section("Static", null, item)));

        var conditions = _added!.Sections[0].Items[0].Conditions;
        Assert.Equal(MenuConditionPhase.Click, conditions[0].Phase);
        Assert.Equal(MenuConditionPhase.Render, conditions[1].Phase);
        Assert.Equal("Render", result.Sections[0].Items[0].Conditions[1].Phase);
    }

    [Fact]
    public async Task CreateAsync_InvalidConditionPhase_Throws()
    {
        var item = new MenuItemTemplateDto
        {
            Conditions = { new ConditionBindingDto { ConditionTypeId = "always", Phase = "Hover" } },
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Template(Section("Static", null, item))));
    }

    [Fact]
    public async Task CreateAsync_EmptyLoreExpression_IsAllowedAsBlankLine()
    {
        var item = new MenuItemTemplateDto
        {
            VariableBindings = { new VariableBindingDto { TargetProperty = "Lore", Expression = "" } },
        };

        await _service.CreateAsync(Template(Section("Static", null, item)));

        Assert.Equal("", _added!.Sections[0].Items[0].VariableBindings[0].Expression);
    }

    [Fact]
    public async Task Seed_CreatesDomainDemoTemplates_ThatPassServiceValidation()
    {
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"MenuSeedPhase9_{Guid.NewGuid()}")
            .Options;
        await using var context = new KnKDbContext(options);

        await MenuTemplateSeed.SeedCanonicalAsync(context);
        // Create-only: a second run must not duplicate anything.
        await MenuTemplateSeed.SeedCanonicalAsync(context);

        var seeded = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.VariableBindings)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions).ThenInclude(a => a.Conditions)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Conditions)
            .Where(t => t.Key.StartsWith("example.domain"))
            .ToListAsync();

        Assert.Equal(2, seeded.Count);
        var domain = seeded.Single(t => t.Key == "example.domain");
        Assert.Equal(20, domain.AutoRefreshTicks);
        var rows = domain.Sections.Single(s => s.Name == "Rows");
        Assert.Equal("example.rows", rows.ContentSourceId);
        Assert.Single(rows.Items, i => i.IsRowTemplate);
        Assert.Contains(rows.Items.Single(i => i.IsRowTemplate).Conditions, c => c.Phase == MenuConditionPhase.Render);

        // Round-trip every seeded demo template through the real mapping profile and
        // the service's create-path validation - the seed bypasses the service, so
        // this is what proves it is a template the API itself would accept.
        foreach (var template in seeded)
        {
            var dto = _mapper.Map<MenuTemplateDto>(template);
            await _service.CreateAsync(dto);
        }

        var actionLevel = domain.Sections.Single(s => s.Name == "Conditions").Items
            .Single(i => i.Actions.Count == 2);
        Assert.All(actionLevel.Actions, a => Assert.Single(a.Conditions, c => c.Phase == MenuConditionPhase.Render));
    }
}

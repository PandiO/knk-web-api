using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Moq;
using System.Text.Json;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>Linear KNG-6: Grade.DropChance and Grade.EnchantLevelCapDivisor through GradeService.</summary>
public class GradeServiceTests
{
    private readonly Mock<IGradeRepository> _repo = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<GradeMappingProfile>()).CreateMapper();

    private GradeService Service() => new(_repo.Object, _mapper);

    [Fact]
    public async Task GetById_ReturnsTheNewFields()
    {
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(new Grade { Id = 3, Name = "Rare", Stars = 3, DropChance = 40m, EnchantLevelCapDivisor = 3 });

        var dto = await Service().GetByIdAsync(3);

        dto!.DropChance.Should().Be(40m);
        dto.EnchantLevelCapDivisor.Should().Be(3);
    }

    [Fact]
    public void ListAndNavDtos_CarryTheNewFields()
    {
        var grade = new Grade { Id = 10, Name = "Divine", Stars = 10, DropChance = 0.05m, EnchantLevelCapDivisor = null };

        var list = _mapper.Map<GradeListDto>(grade);
        var nav = _mapper.Map<GradeNavDto>(grade);

        list.DropChance.Should().Be(0.05m);
        list.EnchantLevelCapDivisor.Should().BeNull();
        nav.DropChance.Should().Be(0.05m);
        nav.EnchantLevelCapDivisor.Should().BeNull();
    }

    [Fact]
    public async Task Create_StoresTheNewFields()
    {
        Grade? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Grade>())).Callback<Grade>(g => added = g).Returns(Task.CompletedTask);

        var dto = await Service().CreateAsync(new GradeCreateDto { Name = "Epic", Stars = 4, DropChance = 25m, EnchantLevelCapDivisor = 2 });

        added!.DropChance.Should().Be(25m);
        added.EnchantLevelCapDivisor.Should().Be(2);
        dto.EnchantLevelCapDivisor.Should().Be(2);
    }

    [Fact]
    public async Task Update_WritesTheNewFields_IncludingClearingTheDivisor()
    {
        var existing = new Grade { Id = 1, Name = "Common", Stars = 1, DropChance = 70m, EnchantLevelCapDivisor = 5 };
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);

        await Service().UpdateAsync(1, new GradeUpdateDto { Id = 1, Name = "Common", Stars = 1, DropChance = 65.5m, EnchantLevelCapDivisor = null });

        existing.DropChance.Should().Be(65.5m);
        existing.EnchantLevelCapDivisor.Should().BeNull(); // uncapped
        _repo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAndUpdate_RejectADivisorBelowOne(int divisor)
    {
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Grade { Id = 1, Name = "Common", Stars = 1 });

        await FluentActions.Awaiting(() => Service().CreateAsync(new GradeCreateDto { Name = "X", Stars = 1, EnchantLevelCapDivisor = divisor }))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Service().UpdateAsync(1, new GradeUpdateDto { Id = 1, Name = "X", Stars = 1, EnchantLevelCapDivisor = divisor }))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public async Task Create_RejectsADropChanceOutsidePercentRange(double dropChance)
    {
        await FluentActions.Awaiting(() => Service().CreateAsync(new GradeCreateDto { Name = "X", Stars = 1, DropChance = (decimal)dropChance }))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Update_FromAFormWithoutTheNewFields_KeepsThem()
    {
        // What the web app's generic form sends when the Grade form configuration predates KNG-6.
        var dto = JsonSerializer.Deserialize<GradeUpdateDto>("{\"id\":1,\"name\":\"Basic\",\"stars\":1}")!;
        var existing = new Grade { Id = 1, Name = "Common", Stars = 1, DropChance = 70m, EnchantLevelCapDivisor = 5 };
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);

        await Service().UpdateAsync(1, dto);

        existing.Name.Should().Be("Basic");
        existing.DropChance.Should().Be(70m);
        existing.EnchantLevelCapDivisor.Should().Be(5);
    }

    [Fact]
    public async Task Update_WithExplicitNulls_ClearsThem()
    {
        var dto = JsonSerializer.Deserialize<GradeUpdateDto>(
            "{\"id\":1,\"name\":\"Common\",\"stars\":1,\"dropChance\":null,\"enchantLevelCapDivisor\":null}")!;
        var existing = new Grade { Id = 1, Name = "Common", Stars = 1, DropChance = 70m, EnchantLevelCapDivisor = 5 };
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);

        await Service().UpdateAsync(1, dto);

        existing.DropChance.Should().BeNull();
        existing.EnchantLevelCapDivisor.Should().BeNull();
    }
}

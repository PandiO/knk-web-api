using Xunit;
using Moq;
using knkwebapi_v2.Services;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for TitleService (docs/specs/user-features/IMPLEMENTATION_PLAN.md §4). Covers the
/// "jump straight to target bracket" resolution rule — promotion and demotion are the exact same
/// code path, since a title is purely a function of the current XP total.
/// </summary>
public class TitleServiceTests
{
    private readonly Mock<ITitleBracketRepository> _mockRepo;
    private readonly TitleService _service;

    public TitleServiceTests()
    {
        _mockRepo = new Mock<ITitleBracketRepository>();
        _service = new TitleService(_mockRepo.Object);
    }

    private static List<TitleBracket> DefaultBrackets() => new()
    {
        new TitleBracket { Id = 1, Name = "Novice", MinExperience = 0 },
        new TitleBracket { Id = 2, Name = "Apprentice", MinExperience = 5 },
        new TitleBracket { Id = 3, Name = "Journeyman", MinExperience = 10 },
        new TitleBracket { Id = 4, Name = "Veteran", MinExperience = 12 },
        new TitleBracket { Id = 5, Name = "Master", MinExperience = 15 }
    };

    [Fact]
    public async Task ResolveAsync_NoBracketsSeeded_ReturnsEmptyResolution()
    {
        _mockRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(new List<TitleBracket>());

        var result = await _service.ResolveAsync(100);

        Assert.Null(result.TitleBracketId);
        Assert.Null(result.TitleName);
        Assert.Equal(0, result.PrestigeExperience);
    }

    [Theory]
    [InlineData(0, 1, "Novice")]
    [InlineData(4, 1, "Novice")]
    [InlineData(5, 2, "Apprentice")]
    [InlineData(9, 2, "Apprentice")]
    [InlineData(10, 3, "Journeyman")]
    [InlineData(11, 3, "Journeyman")]
    [InlineData(12, 4, "Veteran")]
    [InlineData(14, 4, "Veteran")]
    public async Task ResolveAsync_BelowTopBracket_ResolvesCorrectBracketWithNoPrestige(int xp, int expectedId, string expectedName)
    {
        _mockRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(DefaultBrackets());

        var result = await _service.ResolveAsync(xp);

        Assert.Equal(expectedId, result.TitleBracketId);
        Assert.Equal(expectedName, result.TitleName);
        Assert.Equal(0, result.PrestigeExperience);
    }

    [Fact]
    public async Task ResolveAsync_ExactlyAtTopBracketThreshold_ResolvesTopBracketWithZeroPrestige()
    {
        _mockRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(DefaultBrackets());

        var result = await _service.ResolveAsync(15);

        Assert.Equal(5, result.TitleBracketId);
        Assert.Equal("Master", result.TitleName);
        Assert.Equal(0, result.PrestigeExperience);
    }

    [Fact]
    public async Task ResolveAsync_PastTopBracket_ResolvesTopBracketWithPrestigeOverage()
    {
        _mockRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(DefaultBrackets());

        var result = await _service.ResolveAsync(37);

        Assert.Equal(5, result.TitleBracketId);
        Assert.Equal("Master", result.TitleName);
        Assert.Equal(22, result.PrestigeExperience);
    }

    [Fact]
    public async Task ResolveAsync_JumpsDirectlyAcrossMultipleBrackets_NoIncrementalCatchUp()
    {
        // Confirms promotion/demotion both jump straight to the target bracket in one call —
        // no per-level stepping the way v1's TitleChangeEvents drained a multi-level change one
        // tick at a time (docs/specs/legacy/user-system.md).
        _mockRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(DefaultBrackets());

        var promoted = await _service.ResolveAsync(13); // 0 -> jumps straight to Veteran (id 4)
        Assert.Equal(4, promoted.TitleBracketId);

        var demoted = await _service.ResolveAsync(2); // jumps straight back down to Novice (id 1)
        Assert.Equal(1, demoted.TitleBracketId);
    }

    [Fact]
    public async Task ResolveAsync_XpBelowLowestBracket_FallsBackToLowestBracket()
    {
        // Guards against a misconfigured seed (no bracket at MinExperience 0) rather than
        // returning no title at all for a brand-new user.
        var brackets = new List<TitleBracket>
        {
            new() { Id = 10, Name = "StartsAtFive", MinExperience = 5 },
            new() { Id = 11, Name = "Ten", MinExperience = 10 }
        };
        _mockRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(brackets);

        var result = await _service.ResolveAsync(0);

        Assert.Equal(10, result.TitleBracketId);
        Assert.Equal("StartsAtFive", result.TitleName);
        Assert.Equal(0, result.PrestigeExperience);
    }
}

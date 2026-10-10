using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Tests.Services.Statistics;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Public player profile (IMPLEMENTATION_PLAN.md §3.2; link 5 acceptance criterion 2): anonymous,
/// case-insensitive, 404 for unknown or inactive accounts, only the listed always-public fields —
/// no online flag, email or UUID.
/// </summary>
public class PlayersControllerTests : IDisposable
{
    private readonly StatisticsTestDb _db = new();

    public PlayersControllerTests()
    {
        var alice = _db.Context.Users.Single(u => u.Id == 1);
        alice.ExperiencePoints = 120;
        alice.Coins = 7;
        alice.Gems = 2;
        alice.IsOnline = true;
        alice.Email = "alice@example.com";
        _db.Context.Users.Single(u => u.Id == 3).IsActive = false;
        _db.Context.TitleBrackets.Add(new TitleBracket { Id = 10, MaleName = "Squire", FemaleName = "Squire", MinExperience = 100 });
        _db.Context.PlayerStatTotals.AddRange(
            new PlayerStatTotal { UserId = 1, MetricKey = "active_playtime", ContextKey = "", Value = 3600.9m },
            new PlayerStatTotal { UserId = 1, MetricKey = "afk_time", ContextKey = "", Value = 60 },
            new PlayerStatTotal { UserId = 1, MetricKey = "pvp_kills", ContextKey = "open_world", Value = 9 });
        _db.Context.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private PlayersController Controller() => new(_db.Query());

    [Theory]
    [InlineData("alice")]
    [InlineData("ALICE")]
    [InlineData(" Alice ")]
    public async Task GetByName_ReturnsTheAlwaysPublicProfile_IgnoringCase(string name)
    {
        var result = await Controller().GetByName(name, default);

        var profile = Assert.IsType<PublicPlayerProfileDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal((1, "alice", "Squire", 120, 7, 2, 3600L, 60L),
            (profile.UserId, profile.Username, profile.TitleName, profile.Experience, profile.Coins, profile.Gems,
                profile.ActivePlaytimeSeconds, profile.AfkSeconds));
        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), profile.FirstJoinedAt);
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("carol")] // inactive (merged) account
    [InlineData("")]
    public async Task GetByName_Is404_ForUnknownOrInactivePlayers(string name)
    {
        Assert.IsType<NotFoundObjectResult>((await Controller().GetByName(name, default)).Result);
    }

    [Fact]
    public void Profile_ExposesOnlyTheListedFields()
    {
        var names = typeof(PublicPlayerProfileDto).GetProperties().Select(p => p.Name).OrderBy(n => n);

        Assert.Equal(new[] { "ActivePlaytimeSeconds", "AfkSeconds", "Coins", "Experience", "FirstJoinedAt", "Gems", "TitleName", "UserId", "Username" },
            names);
    }
}

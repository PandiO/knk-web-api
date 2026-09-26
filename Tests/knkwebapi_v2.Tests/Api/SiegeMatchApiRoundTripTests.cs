using System.Text.Json;
using System.Text.Json.Serialization;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Tests.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Siege Phase 6: the match lifecycle through the real controller with the JSON bodies the plugin
/// sends (camelCase keys, PascalCase enum names), deserialized with the app's JSON settings, and the
/// status-code mapping (201 / 200 / 204 / 400 / 404 / 409).
/// </summary>
public class SiegeMatchApiRoundTripTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() }
    };

    private KnKDbContext _context = null!;
    private SiegeMatchesController _controller = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeMatchRoundTrip");
        await SiegeTestData.SeedValidScenarioAsync(_context);
        _context.SiegeLobbies.Add(new SiegeLobby { Id = 1, Name = "Cinix", Key = "cinix", IsEnabled = true });
        _context.Users.Add(new User { Id = 1, Username = "defender", Coins = 0 });
        _context.Users.Add(new User { Id = 2, Username = "attacker", Coins = 0 });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _controller = new SiegeMatchesController(new SiegeMatchService(
            new SiegeMatchRepository(_context),
            new TitleService(new TitleBracketRepository(_context))));
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private static T Body<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;

    private static JsonElement AsJson(object? value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, Json)).RootElement;

    [Fact]
    public async Task PluginShapedLifecycle_CreateStartLeftComplete_ThenRepeatComplete()
    {
        var created = Assert.IsType<CreatedResult>(await _controller.Create(Body<SiegeMatchCreateDto>(
            """{ "siegeLobbyId": 1, "siegeScenarioId": 100 }""")));
        var id = AsJson(created.Value).GetProperty("id").GetInt32();
        Assert.Equal($"/api/siege-matches/{id}", created.Location);
        Assert.Equal("Created", AsJson(created.Value).GetProperty("status").GetString());

        var started = Assert.IsType<OkObjectResult>(await _controller.Start(id, Body<SiegeMatchStartDto>(
            """{ "participants": [ { "userId": 1, "siegeTeamId": 201 }, { "userId": 2, "siegeTeamId": 202 } ] }""")));
        Assert.Equal("InProgress", AsJson(started.Value).GetProperty("status").GetString());

        Assert.IsType<NoContentResult>(await _controller.ParticipantLeft(id, 1,
            Body<SiegeMatchParticipantLeftDto>("""{ "leftAt": "2026-09-26T21:00:00Z" }""")));
        _context.ChangeTracker.Clear();

        const string complete = """
        {
          "endReason": "InstantVictory",
          "winningAllianceGroup": 2,
          "participants": [ { "userId": 2, "siegeTeamId": 202, "kills": 3, "deaths": 1, "highestKillStreak": 2, "captures": 1 } ],
          "objectives": [
            { "siegeObjectiveId": 502, "finalHolderTeamId": 201, "capturedByUserId": null, "capturedAt": null },
            { "siegeObjectiveId": 501, "finalHolderTeamId": 202, "capturedByUserId": 2, "capturedAt": "2026-09-26T21:05:00Z" }
          ]
        }
        """;
        var first = AsJson(Assert.IsType<OkObjectResult>(await _controller.Complete(id, Body<SiegeMatchCompleteDto>(complete))).Value);
        Assert.False(first.GetProperty("alreadyCompleted").GetBoolean());
        Assert.Equal("Completed", first.GetProperty("status").GetString());
        var attacker = first.GetProperty("rewards").EnumerateArray().Single(r => r.GetProperty("userId").GetInt32() == 2);
        Assert.Equal(100 + 50 + 50, attacker.GetProperty("coins").GetInt32());
        Assert.Equal(10 + 5 + 5, attacker.GetProperty("experience").GetInt32());
        Assert.Equal(1, attacker.GetProperty("gems").GetInt32());
        var defender = first.GetProperty("rewards").EnumerateArray().Single(r => r.GetProperty("userId").GetInt32() == 1);
        Assert.False(defender.GetProperty("presentAtEnd").GetBoolean());

        _context.ChangeTracker.Clear();
        var second = AsJson(Assert.IsType<OkObjectResult>(await _controller.Complete(id, Body<SiegeMatchCompleteDto>(complete))).Value);
        Assert.True(second.GetProperty("alreadyCompleted").GetBoolean());
        _context.ChangeTracker.Clear();
        Assert.Equal(200, _context.Users.Single(u => u.Id == 2).Coins);

        // The same match is visible in the history for the attacker.
        var history = Assert.IsType<OkObjectResult>((await _controller.Query(2, null, null)).Result);
        var row = AsJson(history.Value).EnumerateArray().Single();
        Assert.Equal(200, row.GetProperty("participant").GetProperty("coinsAwarded").GetInt32());
    }

    [Fact]
    public async Task StatusCodes_NotFound_BadRequest_Conflict()
    {
        Assert.IsType<NotFoundObjectResult>(await _controller.Complete(999, new SiegeMatchCompleteDto()));
        Assert.IsType<BadRequestObjectResult>(await _controller.Create(new SiegeMatchCreateDto { SiegeLobbyId = 9, SiegeScenarioId = 100 }));

        var created = Assert.IsType<CreatedResult>(await _controller.Create(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 }));
        var id = ((SiegeMatchDto)created.Value!).Id;
        Assert.IsType<BadRequestObjectResult>(await _controller.Complete(id, Body<SiegeMatchCompleteDto>("""{ "endReason": "ServerRestart" }""")));

        var aborted = Assert.IsType<OkObjectResult>(await _controller.Abort(id, Body<SiegeMatchAbortDto>("""{ "endReason": "NotEnoughPlayers" }""")));
        Assert.Equal("NotEnoughPlayers", AsJson(aborted.Value).GetProperty("endReason").GetString());
        _context.ChangeTracker.Clear();
        Assert.IsType<ConflictObjectResult>(await _controller.Complete(id, Body<SiegeMatchCompleteDto>("""{ "endReason": "TimeExpired" }""")));

        var recovered = Assert.IsType<OkObjectResult>(await _controller.AbortUnfinished(null));
        Assert.Empty(AsJson(recovered.Value).GetProperty("abortedMatchIds").EnumerateArray());
    }
}

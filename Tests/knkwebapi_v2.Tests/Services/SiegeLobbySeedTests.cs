using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 9: the disabled example lobby seed is create-only by key and never enabled by the seed.
/// </summary>
public class SiegeLobbySeedTests
{
    private static KnKDbContext NewContext() => new(new DbContextOptionsBuilder<KnKDbContext>()
        .UseInMemoryDatabase($"SiegeLobbySeed_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Seed_CreatesOneDisabledExampleLobby_Idempotently()
    {
        await using var context = NewContext();

        await SiegeLobbySeed.SeedCanonicalAsync(context);
        await SiegeLobbySeed.SeedCanonicalAsync(context);

        var lobby = Assert.Single(await context.SiegeLobbies.Include(l => l.Rotation).ToListAsync());
        Assert.Equal(SiegeLobbySeed.ExampleKey, lobby.Key);
        Assert.Equal(lobby.Key.ToLowerInvariant(), lobby.Key);
        Assert.False(lobby.IsEnabled);
        Assert.Empty(lobby.Rotation);
        Assert.InRange(lobby.VoteCandidateCount, 1, 3);
    }

    [Fact]
    public async Task Seed_LeavesAnExistingExampleLobbyUntouched()
    {
        await using var context = NewContext();
        context.SiegeLobbies.Add(new SiegeLobby { Name = "Cinix", Key = SiegeLobbySeed.ExampleKey, IsEnabled = true, CooldownSeconds = 60 });
        await context.SaveChangesAsync();

        await SiegeLobbySeed.SeedCanonicalAsync(context);

        var lobby = Assert.Single(await context.SiegeLobbies.ToListAsync());
        Assert.Equal("Cinix", lobby.Name);
        Assert.True(lobby.IsEnabled);
        Assert.Equal(60, lobby.CooldownSeconds);
    }
}

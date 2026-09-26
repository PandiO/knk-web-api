using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// Siege Phase 9 (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md): one <b>disabled</b> example
/// lobby, so a fresh database shows admins what a lobby looks like (docs/guides/authoring-a-siege-scenario.md).
/// Create-only by key like every other seed: an existing row (edited, enabled or deleted-and-recreated)
/// is never touched. It has no rotation - add a ready scenario and enable it to use it. Disabled
/// lobbies never reach the plugin's runtime-config.
/// </summary>
public static class SiegeLobbySeed
{
    public const string ExampleKey = "example";

    public static async Task SeedCanonicalAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        if (await context.SiegeLobbies.AnyAsync(l => l.Key == ExampleKey, cancellationToken))
        {
            return;
        }

        await context.SiegeLobbies.AddAsync(new SiegeLobby
        {
            Name = "Example siege (disabled)",
            Key = ExampleKey,
            IsEnabled = false,
            Mode = SiegeLobbyMode.Continuous,
            MatchmakingSeconds = 300,
            CooldownSeconds = 900,
            VoteCandidateCount = 2,
            AllowRandomVote = true
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        logger?.LogInformation("SiegeLobby seed: created the disabled example lobby '{Key}'", ExampleKey);
    }
}

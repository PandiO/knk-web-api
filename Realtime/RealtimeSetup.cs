using knkwebapi_v2.Attributes;
using knkwebapi_v2.Realtime.Hubs;
using knkwebapi_v2.Realtime.Presence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// Wiring of the realtime channel (PandiO/knk-workspace#178, DESIGN §4–§5): both hubs live in the API process, no
/// backplane (Q1). Program.cs calls <see cref="AddKnkRealtime"/>, <see cref="MapKnkRealtimeHubs"/> and
/// <see cref="ReadHubAccessToken"/>; the hub tests use the same three.
/// </summary>
public static class RealtimeSetup
{
    public const string GameHubPath = "/api/hubs/game";
    public const string WebHubPath = "/api/hubs/web";

    /// <summary>Authorization policy of the game hub: the plugin key, as on <c>[RequirePluginService]</c> routes.</summary>
    public const string GameServerPolicy = "KnkRealtimeGameServer";

    public static IServiceCollection AddKnkRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RealtimeOptions>(configuration.GetSection(RealtimeOptions.SectionName));
        var options = configuration.GetSection(RealtimeOptions.SectionName).Get<RealtimeOptions>() ?? new RealtimeOptions();

        services.AddSignalR(o =>
        {
            o.KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(1, options.KeepAliveSeconds));
            o.ClientTimeoutInterval = TimeSpan.FromSeconds(Math.Max(2, options.ClientTimeoutSeconds));
            // Never send exception text to a client.
            o.EnableDetailedErrors = false;
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RealtimeConnections>();
        services.AddSingleton<RealtimeMetrics>();
        services.AddSingleton<RealtimePublisher>();
        services.AddSingleton<IRealtimePublisher>(sp => sp.GetRequiredService<RealtimePublisher>());
        services.AddSingleton<IRealtimeTransport, HubContextRealtimeTransport>();
        services.AddHostedService<RealtimeDispatchService>();
        services.AddSingleton<PresenceRegistry>();
        services.AddScoped<IPresenceStore, PresenceStore>();
        services.AddHostedService<PresenceSweepService>();

        services.AddSingleton<IAuthorizationHandler, GameServerKeyHandler>();
        services.AddAuthorization(o =>
            o.AddPolicy(GameServerPolicy, policy => policy.AddRequirements(new GameServerKeyRequirement())));
        return services;
    }

    public static void MapKnkRealtimeHubs(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<GameServerHub>(GameHubPath).RequireAuthorization(GameServerPolicy);
        // The default policy: an authenticated JWT user. The connection closes when the token expires; the web client
        // reconnects with a fresh one.
        endpoints.MapHub<WebHub>(WebHubPath, o => o.CloseOnAuthenticationExpiration = true).RequireAuthorization();
    }

    /// <summary>
    /// For <c>JwtBearerEvents.OnMessageReceived</c>: browsers can't set headers on a WebSocket, so the web hub (and only
    /// the web hub) takes the access token from the <c>access_token</c> query string when no Authorization header is
    /// present. Returns null everywhere else.
    /// </summary>
    public static string? ReadHubAccessToken(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments(WebHubPath, StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrEmpty(request.Headers.Authorization.ToString())) return null;
        var token = request.Query["access_token"].ToString();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}

/// <summary>Requirement of <see cref="RealtimeSetup.GameServerPolicy"/>.</summary>
public sealed class GameServerKeyRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Passes when the request carries the plugin key (constant-time compare, <see cref="PluginServiceAuth.KeyStatus"/>),
/// or under the explicit Development opt-out <c>Security:AllowUnauthenticatedPluginCalls</c>.
/// </summary>
public sealed class GameServerKeyHandler : AuthorizationHandler<GameServerKeyRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, GameServerKeyRequirement requirement)
    {
        var http = context.Resource switch
        {
            HttpContext h => h,
            HubInvocationContext hub => hub.Context.GetHttpContext(),
            _ => null,
        };
        if (http != null && PluginServiceAuth.KeyStatus(http) is PluginServiceAuth.PluginKeyStatus.Valid
                or PluginServiceAuth.PluginKeyStatus.DevelopmentBypass)
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

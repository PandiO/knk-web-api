using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using knkwebapi_v2.DependencyInjection; // added for DI extensions
using System;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRazorPages();

// Closed-alpha WP1 (D6): default-deny. Every action needs the plugin key or a logged-in user
// unless it has [AllowAnonymous]; every write also needs an explicit access rule.
builder.Services.AddControllers(options => options.Filters.Add<knkwebapi_v2.Attributes.DefaultCallerRequiredFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        // Add converter for enums to be serialized/deserialized as strings
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Prevent automatic 400 so we can handle invalid enums (e.g., "Draft") gracefully in controllers.
        options.SuppressModelStateInvalidFilter = true;
    });

// Bind Kestrel to URLs from config, or to loopback only (closed-alpha WP7.6): reaching the API
// from the network is an explicit choice (Urls / ASPNETCORE_URLS, e.g. http://0.0.0.0:5000 in a container).
var urlConfig = builder.Configuration["ASPNETCORE_URLS"] ?? builder.Configuration["Urls"];
if (!string.IsNullOrWhiteSpace(urlConfig))
{
    builder.WebHost.UseUrls(urlConfig);
}
else
{
    builder.WebHost.UseUrls("http://127.0.0.1:5000");
}

// Closed-alpha WP7.1 (D9): no secrets in git. Outside Development a missing or committed JWT
// secret or a missing connection string stops the API here.
var startupWarnings = knkwebapi_v2.Configuration.StartupSecurity.ApplySecretChecks(builder);

string? connectionString = builder.Configuration.GetConnectionString("MySqlDbConnection");
builder.Services.AddDbContext<KnKDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.Configure<knkwebapi_v2.Configuration.SecuritySettings>(builder.Configuration.GetSection("Security"));

// Register app services/repositories in one place
builder.Services.AddApplicationServices(builder.Configuration);

// JWT bearer authentication configuration
var jwtSection = builder.Configuration.GetSection("Security:Jwt");
var jwtIssuer = jwtSection["Issuer"] ?? "knk-api";
var jwtAudience = jwtSection["Audience"] ?? "knk-app";
var jwtSecret = jwtSection["Secret"] ?? throw new InvalidOperationException("Security:Jwt:Secret is required in configuration");
var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = jwtKey,
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(60)
    };
    // Closed-alpha WP4: a refresh JWT from before the change is not a bearer token, and every
    // access token must carry the user's current TokenVersion ("tv") and an active user.
    // IUserSessionStateCache keeps this to one DB read per user per 60 s.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var sessions = context.HttpContext.RequestServices.GetRequiredService<knkwebapi_v2.Services.IUserSessionStateCache>();
            var problem = context.Principal == null
                ? "no principal"
                : await knkwebapi_v2.Services.AccessTokenSessionCheck.FindProblemAsync(context.Principal, sessions, context.HttpContext.RequestAborted);
            if (problem != null)
            {
                context.Fail($"Access token refused: {problem}.");
            }
        }
    };
});

// Health checks: add liveness/readiness
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// Options binding for telemetry and client activity
builder.Services.Configure<knkwebapi_v2.Configuration.TelemetryOptions>(builder.Configuration.GetSection(knkwebapi_v2.Configuration.TelemetryOptions.SectionName));
builder.Services.Configure<knkwebapi_v2.Configuration.ClientActivityOptions>(builder.Configuration.GetSection(knkwebapi_v2.Configuration.ClientActivityOptions.SectionName));

// Register in-memory client activity store
builder.Services.AddSingleton<knkwebapi_v2.Services.Interfaces.IClientActivityStore>(sp =>
{
    var opts = builder.Configuration.GetSection(knkwebapi_v2.Configuration.ClientActivityOptions.SectionName).Get<knkwebapi_v2.Configuration.ClientActivityOptions>()
               ?? new knkwebapi_v2.Configuration.ClientActivityOptions();
    return new knkwebapi_v2.Services.InMemoryClientActivityStore(opts.MaxClients, opts.CleanupInterval);
});

// OpenTelemetry setup per configuration
var telemetryOptions = builder.Configuration.GetSection(knkwebapi_v2.Configuration.TelemetryOptions.SectionName)
    .Get<knkwebapi_v2.Configuration.TelemetryOptions>() ?? new knkwebapi_v2.Configuration.TelemetryOptions();

if (telemetryOptions.Enabled)
{
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation();
            metrics.AddMeter(knkwebapi_v2.Services.Lootbox.LootboxMetrics.MeterName);
            // Currency ledger counters and lock-wait histogram (currency-payments Phase 5).
            metrics.AddMeter(knkwebapi_v2.Services.CurrencyMetrics.MeterName);
            if (string.Equals(telemetryOptions.Exporter, "otlp", StringComparison.OrdinalIgnoreCase)
                && telemetryOptions.Otlp.EnableMetrics)
            {
                metrics.AddOtlpExporter(o => { o.Endpoint = new Uri(telemetryOptions.Otlp.Endpoint); });
            }
        })
        .WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation();
            if (string.Equals(telemetryOptions.Exporter, "otlp", StringComparison.OrdinalIgnoreCase)
                && telemetryOptions.Otlp.EnableTracing)
            {
                tracing.AddOtlpExporter(o => { o.Endpoint = new Uri(telemetryOptions.Otlp.Endpoint); });
            }
        });
}

// Closed-alpha WP6.3: per-IP limits on the auth and lookup endpoints ([EnableRateLimiting]).
knkwebapi_v2.Configuration.RateLimitingSetup.AddKnkRateLimiting(builder.Services, builder.Configuration);

// Closed-alpha WP7.2: CORS origins from Cors:AllowedOrigins (the localhost dev list lives in
// appsettings.Development.json). A same-origin deploy (web app and /api on one host) needs none.
var allowedOrigins = knkwebapi_v2.Configuration.StartupSecurity.AllowedOrigins(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy =>
        {
            if (allowedOrigins.Length == 0)
            {
                policy.SetIsOriginAllowed(_ => false);
                return;
            }
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials() // cookies (the refresh cookie) and the Authorization header
                .WithExposedHeaders("Retry-After"); // the web app reads it on a 429
        });
});

// Closed-alpha WP7.3: behind a reverse proxy / Cloudflare Tunnel the client IP and scheme come
// from forwarded headers (needed for the per-IP rate limits and Secure cookies).
var forwardedHeadersEnabled = knkwebapi_v2.Configuration.StartupSecurity.ConfigureForwardedHeaders(builder.Services, builder.Configuration);

builder.Services.AddAuthorization();

var app = builder.Build();

foreach (var warning in startupWarnings)
{
    app.Logger.LogWarning("{Warning}", warning);
}
if (app.Environment.IsDevelopment())
{
    app.Logger.LogInformation("Dev secrets live in user-secrets, e.g. dotnet user-secrets set \"ConnectionStrings:MySqlDbConnection\" \"…\" (see CLAUDE.md).");
}

// KNG-22: the plugin authenticates with this shared key (X-API-Key). Without it every
// plugin-only and currency/admin write refuses the game server, unless a developer opted out.
if (string.IsNullOrEmpty(app.Configuration[knkwebapi_v2.Attributes.PluginServiceAuth.ApiKeyConfigKey]))
{
    var bypass = app.Environment.IsDevelopment()
        && bool.TryParse(app.Configuration[knkwebapi_v2.Attributes.PluginServiceAuth.AllowUnauthenticatedConfigKey], out var allow) && allow;
    app.Logger.LogWarning(bypass
        ? "Security:PluginApiKey is not set and Security:AllowUnauthenticatedPluginCalls is on: EVERY anonymous caller is treated as the game server. Development only."
        : "Security:PluginApiKey is not set: game-server (knk-plugin) calls to protected endpoints will be refused with 401. Set the same value as the plugin's api.auth.api-key.");
}

// Configure the HTTP request pipeline.
if (forwardedHeadersEnabled)
{
    // First, so everything after sees the real client IP and scheme.
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Closed-alpha WP7.4: outside Development an unhandled exception is a ProblemDetails body with
    // no exception text (the middleware logs the exception).
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title = "An unexpected error occurred.",
            status = StatusCodes.Status500InternalServerError,
            traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
        }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }));
}

app.UseCors();
app.MapRazorPages();
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Client activity tracking middleware
app.UseMiddleware<knkwebapi_v2.Middleware.ClientActivityMiddleware>();

// Only redirect to HTTPS if an HTTPS listener is configured
var configuredUrls = (urlConfig ?? string.Empty)
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
bool hasHttpsListener = configuredUrls.Any(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
if (hasHttpsListener)
{
    app.UseHttpsRedirection();
}

// TODO: Prometheus exporter endpoint can be added with OpenTelemetry Prometheus AspNetCore package

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<KnKDbContext>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var seedLogger = loggerFactory.CreateLogger("AbilityDefinitionCanonicalSeed");
    await knkwebapi_v2.Models.AbilityDefinition.SeedCanonicalAsync(dbContext, seedLogger);

    var itemBlueprintCatalogSeedLogger = loggerFactory.CreateLogger("ItemBlueprintExampleCatalogSeed");
    await knkwebapi_v2.Models.ItemBlueprintExampleCatalogSeed.SeedCanonicalAsync(dbContext, itemBlueprintCatalogSeedLogger);

    var menuSeedLogger = loggerFactory.CreateLogger("MenuTemplateSeed");
    await knkwebapi_v2.Models.MenuTemplateSeed.SeedCanonicalAsync(dbContext, menuSeedLogger);

    var kitSeedLogger = loggerFactory.CreateLogger("KitSeed");
    var materialCatalog = scope.ServiceProvider.GetRequiredService<knkwebapi_v2.Services.Interfaces.IMinecraftMaterialCatalogService>();
    await knkwebapi_v2.Models.KitSeed.SeedCanonicalAsync(dbContext, materialCatalog, kitSeedLogger);

    // After KitSeed, so on a fresh DB the kit's own "Iron Sword"/"Arrow" rows win the shared names.
    var v1BlueprintSeedLogger = loggerFactory.CreateLogger("ItemBlueprintV1Seed");
    var enchantmentCatalog = scope.ServiceProvider.GetRequiredService<knkwebapi_v2.Services.Interfaces.IMinecraftEnchantmentCatalogService>();
    await knkwebapi_v2.Models.ItemBlueprintV1Seed.SeedCanonicalAsync(dbContext, materialCatalog, enchantmentCatalog, v1BlueprintSeedLogger);

    // After the ability (custom) and V1 (vanilla) seeds: permanent enchantment books reuse their EnchantmentDefinitions (KNG-5).
    var enchantBookSeedLogger = loggerFactory.CreateLogger("EnchantBookSeed");
    await knkwebapi_v2.Models.EnchantBookSeed.SeedCanonicalAsync(dbContext, materialCatalog, enchantBookSeedLogger);

    // After every item seed: one disabled lootbox type per category, the specials (incl. the Flaming Samurai), enchant
    // rolls and the lootbox settings (docs/specs/lootboxes/DESIGN.md §3.5).
    var lootboxSeedLogger = loggerFactory.CreateLogger("LootboxSeed");
    await knkwebapi_v2.Models.LootboxSeed.SeedCanonicalAsync(dbContext, materialCatalog, enchantmentCatalog, lootboxSeedLogger);

    // Siege Phase 9: one disabled example lobby (create-only by key).
    var siegeLobbySeedLogger = loggerFactory.CreateLogger("SiegeLobbySeed");
    await knkwebapi_v2.Models.SiegeLobbySeed.SeedCanonicalAsync(dbContext, siegeLobbySeedLogger);
}

app.Run();


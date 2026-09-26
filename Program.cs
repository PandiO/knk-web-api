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

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRazorPages();

builder.Services.AddControllers()
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

// Bind Kestrel to URLs from config or default to LAN-accessible HTTP
var urlConfig = builder.Configuration["ASPNETCORE_URLS"] ?? builder.Configuration["Urls"];
if (!string.IsNullOrWhiteSpace(urlConfig))
{
    builder.WebHost.UseUrls(urlConfig);
}
else
{
    builder.WebHost.UseUrls("http://0.0.0.0:5000");
}

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

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy =>
        {
            policy
                .WithOrigins(
                    "http://localhost:3000",    // Frontend dev server (HTTP)
                    "https://localhost:3000",   // Frontend dev server (HTTPS)
                    "http://localhost:5294",    // API HTTP (before HTTPS redirect)
                    "https://localhost:7104"    // API HTTPS (after redirect)
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials(); // Enable credentials (cookies, authorization headers)
        });
});

// Placeholder admin authorization policy.
// TODO: Bind this to your real authentication/authorization setup.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy =>
    {
        // Placeholder: allow all in Development, require role claim otherwise.
        policy.RequireAssertion(ctx =>
            ctx.User?.IsInRole("Admin") == true || ctx.User?.Claims.Any(c => c.Type == "role" && c.Value == "Admin") == true);
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapRazorPages();
app.UseRouting();

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

    // Siege Phase 9: one disabled example lobby (create-only by key).
    var siegeLobbySeedLogger = loggerFactory.CreateLogger("SiegeLobbySeed");
    await knkwebapi_v2.Models.SiegeLobbySeed.SeedCanonicalAsync(dbContext, siegeLobbySeedLogger);
}

app.Run();


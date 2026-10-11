using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>Closed-alpha hardening WP7: secrets, CORS and the plugin region calls.</summary>
public class DeploymentSettingsTests
{
    private static WebApplicationBuilder Builder(string environment, Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private const string Connection = "Server=localhost;Database=x;User=u;Password=p";
    private static readonly string GoodSecret = new('k', 48);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-0123456789")]
    public void JwtSecret_EmptyOrShort_IsAProblem(string? secret)
    {
        Assert.NotNull(StartupSecurity.JwtSecretProblem(secret));
    }

    [Fact]
    public void JwtSecret_Fresh_IsFine()
    {
        Assert.Null(StartupSecurity.JwtSecretProblem(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))));
        Assert.Equal(2, StartupSecurity.CommittedJwtSecretSha256.Count);
    }

    [Fact]
    public void Production_RefusesToStart_WithoutAJwtSecret()
    {
        var builder = Builder(Environments.Production, new() { ["ConnectionStrings:MySqlDbConnection"] = Connection });
        var ex = Assert.Throws<InvalidOperationException>(() => StartupSecurity.ApplySecretChecks(builder));
        Assert.Contains("Security:Jwt:Secret", ex.Message);
    }

    [Fact]
    public void Production_RefusesToStart_WithoutAConnectionString()
    {
        var builder = Builder(Environments.Production, new() { ["Security:Jwt:Secret"] = GoodSecret });
        Assert.Throws<InvalidOperationException>(() => StartupSecurity.ApplySecretChecks(builder));
    }

    [Fact]
    public void Production_StartsWithAGoodSecret()
    {
        var builder = Builder(Environments.Production, new() { ["ConnectionStrings:MySqlDbConnection"] = Connection, ["Security:Jwt:Secret"] = GoodSecret });
        Assert.Empty(StartupSecurity.ApplySecretChecks(builder));
    }

    [Fact]
    public void Development_GeneratesAJwtSecret_WithAWarning()
    {
        var builder = Builder(Environments.Development, new() { ["ConnectionStrings:MySqlDbConnection"] = Connection });
        var warnings = StartupSecurity.ApplySecretChecks(builder);
        Assert.Single(warnings);
        Assert.Null(StartupSecurity.JwtSecretProblem(builder.Configuration["Security:Jwt:Secret"]));
    }

    [Fact]
    public void CorsOrigins_ComeFromConfig()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
            ["Cors:AllowedOrigins:1"] = "https://app.example",
        }).Build();
        Assert.Equal(new[] { "http://localhost:3000", "https://app.example" }, StartupSecurity.AllowedOrigins(config));
        Assert.Empty(StartupSecurity.AllowedOrigins(new ConfigurationBuilder().Build()));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("true") });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task RegionCalls_CarryThePluginKey()
    {
        var handler = new RecordingHandler();
        var service = new RegionService(new Factory(handler), NullLogger<RegionService>.Instance, "http://plugin:8081", "the-key");

        await service.RenameRegionAsync("a", "b");

        Assert.Equal("the-key", handler.Last!.Headers.GetValues("X-API-Key").Single());
    }
}

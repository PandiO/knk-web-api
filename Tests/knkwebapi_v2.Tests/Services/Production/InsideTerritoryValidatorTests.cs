using System.Net;
using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Services.ValidationMethods;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// The InsideTerritory validation type (P-2, P-3; production DESIGN § 3.4) and the plugin lookup contract it calls
/// (TerritoryLookupClient, answered by P2c #677): region or location field, the town from the dependency (a record, an id
/// or form JSON), the game server's answer, "could not run" when the server can't answer, and a pass without a lookup
/// while production is off (A21).
/// </summary>
public class InsideTerritoryValidatorTests
{
    private readonly Mock<ITerritoryLookupClient> _lookup = new();
    private readonly Mock<IProductionSettingsReader> _settings = new();

    public InsideTerritoryValidatorTests() =>
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(new ProductionSettingsDto { Enabled = true });

    private InsideTerritoryValidator Validator() => new(_lookup.Object, _settings.Object);

    private static readonly Town Phoenix = new() { Id = 10, Name = "Phoenix", WorldName = "world", WgRegionId = "domain_10" };

    [Fact]
    public async Task Region_InsideTheTownsTerritory_IsValid()
    {
        _lookup.Setup(l => l.IsRegionInsideAsync(TerritoryHolderKind.Town, 10, "domain_5", "world")).ReturnsAsync(true);

        var result = await Validator().ValidateAsync("domain_5", Phoenix, null, null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Region_Outside_IsInvalid_AndNamesTheTown()
    {
        _lookup.Setup(l => l.IsRegionInsideAsync(TerritoryHolderKind.Town, 10, "domain_5", "world")).ReturnsAsync(false);

        var result = await Validator().ValidateAsync("domain_5", Phoenix, "{\"holderKind\":\"Town\"}", null);

        Assert.False(result.IsValid);
        Assert.Contains("Phoenix", result.Message);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task Location_UsesTheBlockColumn_AndTheLocationsWorld()
    {
        _lookup.Setup(l => l.IsLocationInsideAsync(TerritoryHolderKind.Town, 10, 120.5, -40, "world_nether")).ReturnsAsync(true);

        var result = await Validator().ValidateAsync(new Location { X = 120.5, Y = 64, Z = -40, World = "world_nether" }, Phoenix, null, null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Dependency_AsFormJson_OrABareId_IsRead()
    {
        _lookup.Setup(l => l.IsRegionInsideAsync(TerritoryHolderKind.Town, 10, "r", null)).ReturnsAsync(true);
        var json = JsonDocument.Parse("{\"id\": 10, \"name\": \"Phoenix\"}").RootElement;

        var fromJson = await Validator().ValidateAsync("r", json, null, null);
        Assert.True(fromJson.IsValid, fromJson.Message);
        var fromId = await Validator().ValidateAsync("r", 10, null, null);
        Assert.True(fromId.IsValid, fromId.Message);
    }

    [Fact]
    public async Task Kingdom_HolderKind_IsPassedOn_AndAnUnknownKindIsAConfigError()
    {
        _lookup.Setup(l => l.IsRegionInsideAsync(TerritoryHolderKind.Kingdom, 10, "r", "world")).ReturnsAsync(true);

        Assert.True((await Validator().ValidateAsync("r", Phoenix, "{\"holderKind\":\"Kingdom\"}", null)).IsValid);
        Assert.False((await Validator().ValidateAsync("r", Phoenix, "{\"holderKind\":\"Clan\"}", null)).IsValid);
    }

    [Fact]
    public async Task GameServerUnavailable_IsACouldNotRunResult()
    {
        _lookup.Setup(l => l.IsRegionInsideAsync(It.IsAny<TerritoryHolderKind>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new TerritoryLookupUnavailableException("the game server is not reachable"));

        var result = await Validator().ValidateAsync("domain_5", Phoenix, null, null);

        Assert.False(result.IsValid);
        Assert.Equal("TerritoryLookupUnavailable", result.Metadata!["failureReason"]);
    }

    [Fact]
    public async Task ProductionOff_Passes_WithoutAskingTheGameServer()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(new ProductionSettingsDto { Enabled = false });

        var result = await Validator().ValidateAsync("domain_5", Phoenix, null, null);

        Assert.True(result.IsValid);
        _lookup.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingFieldOrTown_IsInvalid()
    {
        Assert.False((await Validator().ValidateAsync(null, Phoenix, null, null)).IsValid);
        Assert.False((await Validator().ValidateAsync("r", null, null, null)).IsValid);
        Assert.False((await Validator().ValidateAsync(new Town(), Phoenix, null, null)).IsValid); // no region, no X/Z
    }

    // ---- The plugin contract (TerritoryLookupClient) ----

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (TerritoryLookupClient, Handler) Client(HttpStatusCode status, string body)
    {
        var handler = new Handler(status, body);
        return (new TerritoryLookupClient(new Factory(handler), NullLogger<TerritoryLookupClient>.Instance, "http://mc:8081/", "k3y"), handler);
    }

    [Fact]
    public async Task Client_AsksForTheRegion_WithTheHolderWorldAndKey()
    {
        var (client, handler) = Client(HttpStatusCode.OK, "true");

        Assert.True(await client.IsRegionInsideAsync(TerritoryHolderKind.Town, 12, "domain 7", "world"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://mc:8081/api/territory/contains-region/domain%207?holderKind=Town&holderId=12&world=world",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal("k3y", request.Headers.GetValues("X-API-Key").Single());
    }

    [Fact]
    public async Task Client_AsksForALocation_WithInvariantNumbers()
    {
        var (client, handler) = Client(HttpStatusCode.OK, "{\"inside\": false}");

        Assert.False(await client.IsLocationInsideAsync(TerritoryHolderKind.Kingdom, 3, 10.5, -2, null));

        Assert.Equal("http://mc:8081/api/territory/contains-location?holderKind=Kingdom&holderId=3&x=10.5&z=-2",
            handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "")]            // a plugin without the endpoint
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]  // territory off on the game server
    [InlineData(HttpStatusCode.OK, "maybe")]             // not understood
    public async Task Client_WithoutAClearAnswer_IsUnavailable(HttpStatusCode status, string body)
    {
        var (client, _) = Client(status, body);

        await Assert.ThrowsAsync<TerritoryLookupUnavailableException>(() =>
            client.IsRegionInsideAsync(TerritoryHolderKind.Town, 1, "r", null));
    }
}

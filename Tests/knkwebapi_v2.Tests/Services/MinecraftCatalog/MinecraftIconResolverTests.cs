using System.Net;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>KNG-200 plan §7: icons from Prismarine's mapping, version fallback, Inventivetalent guesses.</summary>
public class MinecraftIconResolverTests
{
    private const string Prismarine = "https://raw.githubusercontent.com/PrismarineJS/minecraft-assets/master/data";
    private const string Inventivetalent = "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets";

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Get { get; } = new();
        public HashSet<string> Head { get; } = new();
        public bool Offline { get; set; }
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add($"{request.Method} {url}");
            if (Offline) throw new HttpRequestException("offline");
            if (request.Method == HttpMethod.Get && Get.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }
            if (request.Method == HttpMethod.Head && Head.Contains(url))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static MinecraftIconResolver Resolver(FakeHandler handler) =>
        new(new Factory(handler), new MinecraftIconOptions(), NullLogger<MinecraftIconResolver>.Instance);

    private static void Publish(FakeHandler handler, string version)
    {
        handler.Get[$"{Prismarine}/{version}/items_textures.json"] = """
            [
              { "name": "stone", "model": "stone", "texture": "minecraft:block/stone" },
              { "name": "diamond_sword", "model": "diamond_sword", "texture": "minecraft:items/diamond_sword" },
              { "name": "apple", "model": "apple", "texture": "minecraft:item/apple" },
              { "name": "air", "model": "air", "texture": "minecraft:missingno" }
            ]
            """;
        handler.Get[$"{Prismarine}/{version}/blocks_textures.json"] = """
            [ { "name": "wall_torch", "blockState": "wall_torch", "model": "wall_torch", "texture": "minecraft:block/torch" } ]
            """;
    }

    [Theory]
    [InlineData("minecraft:block/stone", "blocks/stone")]
    [InlineData("minecraft:item/apple", "items/apple")]
    [InlineData("minecraft:items/diamond_sword", "items/diamond_sword")]
    [InlineData("minecraft:blocks/water_still", "blocks/water_still")]
    [InlineData("minecraft:missingno", null)]
    [InlineData(null, null)]
    public void PrismarineTexturePath_FixesTheBlockFolder(string? texture, string? expected)
    {
        Assert.Equal(expected, MinecraftIconResolver.PrismarineTexturePath(texture));
    }

    [Fact]
    public void CandidateVersions_ScannedFirst_ThenLowerOnesNewestFirst()
    {
        Assert.Equal(new[] { "26.3", "26.1", "1.21.11", "1.21.10" },
            MinecraftIconResolver.CandidateVersions("26.3", new[] { "1.21.10", "26.1", "1.21.11", "26.4" }));
    }

    [Fact]
    public async Task UsesTheNearestLowerPrismarineVersion_AndItsMapping()
    {
        var handler = new FakeHandler();
        Publish(handler, "26.1"); // 26.3 is not published there

        var results = await Resolver(handler).ResolveAsync("26.3", new[]
        {
            new MinecraftIconRequest("minecraft:stone", true, true),
            new MinecraftIconRequest("minecraft:apple", true, false),
            new MinecraftIconRequest("minecraft:wall_torch", false, true),
        }, CancellationToken.None);

        Assert.Equal($"{Prismarine}/26.1/blocks/stone.png", results["minecraft:stone"].Url);
        Assert.Equal($"{Prismarine}/26.1/items/apple.png", results["minecraft:apple"].Url);
        Assert.Equal($"{Prismarine}/26.1/blocks/torch.png", results["minecraft:wall_torch"].Url);
        Assert.All(results.Values, r => Assert.Equal("prismarine@26.1", r.Source));
    }

    [Fact]
    public async Task KeysPrismarineLacks_AreGuessedOnInventivetalent_WithHeadChecks()
    {
        var handler = new FakeHandler();
        Publish(handler, "1.21.10");
        handler.Head.Add($"{Inventivetalent}/1.21.10/assets/minecraft/textures/block/copper_golem_statue.png");

        var results = await Resolver(handler).ResolveAsync("1.21.10", new[]
        {
            new MinecraftIconRequest("minecraft:copper_golem_statue", true, true),
            new MinecraftIconRequest("minecraft:nothing_here", true, false),
            new MinecraftIconRequest("knk:custom", true, false),
        }, CancellationToken.None);

        var statue = results["minecraft:copper_golem_statue"];
        Assert.Equal(MinecraftIconOutcome.Resolved, statue.Outcome);
        Assert.Equal($"{Inventivetalent}/1.21.10/assets/minecraft/textures/block/copper_golem_statue.png", statue.Url);
        Assert.Equal(MinecraftIconOutcome.Missing, results["minecraft:nothing_here"].Outcome);
        Assert.Equal(MinecraftIconOutcome.Missing, results["knk:custom"].Outcome);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("custom"));
    }

    [Fact]
    public async Task Offline_IsUnavailable_NotMissing()
    {
        var handler = new FakeHandler { Offline = true };

        var results = await Resolver(handler).ResolveAsync("1.21.10",
            new[] { new MinecraftIconRequest("minecraft:stone", true, true) }, CancellationToken.None);

        Assert.Equal(MinecraftIconOutcome.Unavailable, results["minecraft:stone"].Outcome);
    }

    [Fact]
    public async Task Disabled_ResolvesNothing()
    {
        var handler = new FakeHandler();
        var resolver = new MinecraftIconResolver(new Factory(handler), new MinecraftIconOptions { Enabled = false }, NullLogger<MinecraftIconResolver>.Instance);

        Assert.Empty(await resolver.ResolveAsync("1.21.10", new[] { new MinecraftIconRequest("minecraft:stone", true, true) }, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }
}

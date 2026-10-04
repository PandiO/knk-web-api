using System.Net;
using knkwebapi_v2.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// The rename call to the plugin carries the domain type and parent region only when they are known, so the plugin can
/// set the renamed region up as its category (parent, priority, flags); a plain rename stays exactly as before.
/// </summary>
public class RegionServiceRenameTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("true") });
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static (RegionService Service, RecordingHandler Handler) Create()
    {
        var handler = new RecordingHandler();
        var service = new RegionService(new SingleClientFactory(handler), NullLogger<RegionService>.Instance, "http://plugin:8081");
        return (service, handler);
    }

    [Fact]
    public async Task PlainRename_SendsOnlyTheTwoRegionIds()
    {
        var (service, handler) = Create();

        var result = await service.RenameRegionAsync("tempregion_worldtask_1", "domain_7");

        Assert.True(result);
        var query = handler.LastRequest!.Query;
        Assert.Contains("oldRegionId=tempregion_worldtask_1", query);
        Assert.Contains("newRegionId=domain_7", query);
        Assert.DoesNotContain("domainType", query);
        Assert.DoesNotContain("parentRegionId", query);
    }

    [Fact]
    public async Task RenameWithTypeAndParent_SendsBoth()
    {
        var (service, handler) = Create();

        await service.RenameRegionAsync("tempregion_worldtask_2", "domain_8", "District", "town_1");

        var query = handler.LastRequest!.Query;
        Assert.Contains("domainType=District", query);
        Assert.Contains("parentRegionId=town_1", query);
    }

    [Fact]
    public async Task RenameWithTypeButNoParent_SendsTheTypeOnly()
    {
        var (service, handler) = Create();

        await service.RenameRegionAsync("tempregion_worldtask_3", "domain_9", "Town", null);

        var query = handler.LastRequest!.Query;
        Assert.Contains("domainType=Town", query);
        Assert.DoesNotContain("parentRegionId", query);
    }

    [Fact]
    public async Task ParentWithoutType_IsNotSent()
    {
        var (service, handler) = Create();

        await service.RenameRegionAsync("tempregion_worldtask_4", "domain_10", null, "town_1");

        Assert.DoesNotContain("parentRegionId", handler.LastRequest!.Query);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.DependencyInjection;
using knkwebapi_v2.Services.CloudflareAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Tests.Services.CloudflareAccess;

/// <summary>
/// A stand-in for api.cloudflare.com: one Zero Trust list held in memory, served through the
/// real "cloudflare" HttpClient (AddCloudflareAccessSync) with this handler as its primary handler.
/// Records every request with its Authorization header and body.
/// </summary>
internal sealed class FakeCloudflare : HttpMessageHandler
{
    public const string AccountId = "acc123";
    public const string ListId = "list-456";
    public const string Token = "cf-test-token-not-a-secret";

    public List<string> Values { get; } = new();
    public List<(HttpMethod Method, string PathAndQuery, string? Auth, string? Body)> Requests { get; } = new();

    /// <summary>Set to make every request fail with this status and error envelope.</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Delay before answering (honours cancellation).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public int PerPageSeen { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), body));
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, ct);
        }
        if (FailWith.HasValue)
        {
            return Json(FailWith.Value, new { success = false, errors = new[] { new { code = 10000, message = "Authentication error" } }, messages = Array.Empty<object>(), result = (object?)null });
        }

        var path = request.RequestUri!.AbsolutePath;
        var listPath = $"/client/v4/accounts/{AccountId}/gateway/lists/{ListId}";
        if (request.Method == HttpMethod.Get && path == listPath + "/items")
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var page = int.Parse(query["page"] ?? "1");
            var perPage = int.Parse(query["per_page"] ?? "20");
            PerPageSeen = perPage;
            var items = Values.Skip((page - 1) * perPage).Take(perPage)
                .Select(v => new { value = v, description = "x", created_at = "2026-10-21T12:00:00Z" }).ToArray();
            var totalPages = Math.Max(1, (int)Math.Ceiling(Values.Count / (double)perPage));
            return Json(HttpStatusCode.OK, new
            {
                success = true, errors = Array.Empty<object>(), messages = Array.Empty<object>(), result = items,
                result_info = new { page, per_page = perPage, count = items.Length, total_count = Values.Count, total_pages = totalPages }
            });
        }
        if (request.Method == HttpMethod.Patch && path == listPath)
        {
            var node = JsonNode.Parse(body!)!;
            foreach (var item in node["append"]?.AsArray() ?? new JsonArray())
            {
                Values.Add(item!["value"]!.GetValue<string>());
            }
            foreach (var value in node["remove"]?.AsArray() ?? new JsonArray())
            {
                Values.Remove(value!.GetValue<string>());
            }
            return Json(HttpStatusCode.OK, new { success = true, errors = Array.Empty<object>(), messages = Array.Empty<object>(), result = new { id = ListId, count = Values.Count } });
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>not found</html>") };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };

    public static CloudflareAccessSettings Settings(bool enabled = true) => new()
    {
        SyncEnabled = enabled,
        AccountId = AccountId,
        ListId = ListId,
        ApiToken = Token,
        ProxyUrl = "http://cfproxy:8888"
    };

    /// <summary>The real DI registration with this handler under the cloudflare client.</summary>
    public ServiceProvider Provider(CloudflareAccessSettings settings, Action<IServiceCollection>? more = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<CloudflareAccessSettings>>(Options.Create(settings));
        services.AddCloudflareAccessSync();
        services.AddHttpClient(CloudflareAccessSettings.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => this);
        more?.Invoke(services);
        return services.BuildServiceProvider();
    }
}

/// <summary>A fixed desired set for CloudflareAccessSync tests.</summary>
internal sealed class FixedDesiredSet : ICloudflareAccessDesiredSet
{
    public HashSet<string> Emails { get; set; } = new();

    public Task<HashSet<string>> GetAsync(DateTime now, CancellationToken ct = default) => Task.FromResult(new HashSet<string>(Emails));
}

/// <summary>Records what registration asked of the sync.</summary>
internal sealed class RecordingAccessSync : ICloudflareAccessSync
{
    public bool Enabled { get; set; } = true;
    public TimeSpan InlineAddTimeout => TimeSpan.FromSeconds(5);
    public List<string?> Added { get; } = new();
    public int SyncRequests { get; private set; }
    public bool Throw { get; set; }

    public void RequestSync() => SyncRequests++;

    public Task<bool> TryAddNowAsync(string? email, TimeSpan timeout)
    {
        Added.Add(email);
        return Task.FromResult(!Throw);
    }

    public knkwebapi_v2.Dtos.CloudflareAccessSyncStatusDto GetStatus() => new() { Enabled = Enabled };
}

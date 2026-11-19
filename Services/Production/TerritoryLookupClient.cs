using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>
    /// The territory lookup for the <c>InsideTerritory</c> placement rule (P-2, P-3; production DESIGN § 3.4, P2
    /// PandiO/knk-workspace#91). Territory is computed and chunk-cached by the game server (territory DESIGN § 1), so
    /// the API asks the plugin's HTTP server, as <c>RegionContainment</c> does for regions. Contract (answered by the
    /// plugin link P2c, #677):
    /// <list type="number">
    /// <item><c>GET /api/territory/contains-region/{regionId}?holderKind=Town&amp;holderId=12[&amp;world=W]</c>: <c>true</c>
    /// when every block column of the region (its union with its sub-regions, KNG-257) is owned by that holder;
    /// contested columns count as not owned.</item>
    /// <item><c>GET /api/territory/contains-location?holderKind=Town&amp;holderId=12&amp;x=..&amp;z=..[&amp;world=W]</c>:
    /// <c>true</c> when that block column is owned by the holder.</item>
    /// <item>Body: <c>true</c> or <c>false</c> (or JSON <c>{"inside": bool}</c>). 404 (an older plugin), 503 (territory off
    /// on the game server) or no answer: the lookup is unavailable and the rule reports that it could not run.</item>
    /// </list>
    /// Calls carry the shared plugin key (<c>X-API-Key</c>) like the region calls.
    /// </summary>
    public interface ITerritoryLookupClient
    {
        Task<bool> IsRegionInsideAsync(TerritoryHolderKind holderKind, int holderId, string regionId, string? world);
        Task<bool> IsLocationInsideAsync(TerritoryHolderKind holderKind, int holderId, double x, double z, string? world);
    }

    /// <summary>The game server could not answer a territory lookup (offline, an older plugin, territory off there).</summary>
    public sealed class TerritoryLookupUnavailableException : Exception
    {
        public TerritoryLookupUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public sealed class TerritoryLookupClient : ITerritoryLookupClient
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TerritoryLookupClient> _logger;
        private readonly string _baseUrl;
        private readonly string? _apiKey;

        public TerritoryLookupClient(IHttpClientFactory httpClientFactory, ILogger<TerritoryLookupClient> logger, string baseUrl, string? apiKey)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
        }

        public Task<bool> IsRegionInsideAsync(TerritoryHolderKind holderKind, int holderId, string regionId, string? world) =>
            AskAsync($"{_baseUrl}/api/territory/contains-region/{Uri.EscapeDataString(regionId)}?{Holder(holderKind, holderId)}{World(world)}");

        public Task<bool> IsLocationInsideAsync(TerritoryHolderKind holderKind, int holderId, double x, double z, string? world) =>
            AskAsync($"{_baseUrl}/api/territory/contains-location?{Holder(holderKind, holderId)}"
                + $"&x={x.ToString(CultureInfo.InvariantCulture)}&z={z.ToString(CultureInfo.InvariantCulture)}{World(world)}");

        private static string Holder(TerritoryHolderKind kind, int id) => $"holderKind={kind}&holderId={id}";

        private static string World(string? world) =>
            string.IsNullOrWhiteSpace(world) ? string.Empty : $"&world={Uri.EscapeDataString(world.Trim())}";

        private async Task<bool> AskAsync(string url)
        {
            HttpResponseMessage response;
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = Timeout;
                if (!string.IsNullOrEmpty(_apiKey))
                    client.DefaultRequestHeaders.Add(knkwebapi_v2.Attributes.PluginServiceAuth.ApiKeyHeader, _apiKey);
                response = await client.GetAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new TerritoryLookupUnavailableException("the game server is not reachable", ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Territory lookup {Url} answered HTTP {Status}", url, (int)response.StatusCode);
                    throw new TerritoryLookupUnavailableException(
                        $"the game server answered HTTP {(int)response.StatusCode} (territory lookup missing or off there)");
                }
                var body = (await response.Content.ReadAsStringAsync()).Trim();
                if (bool.TryParse(body, out var inside)) return inside;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("inside", out var prop)
                        && prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        return prop.GetBoolean();
                }
                catch (JsonException) { }
                throw new TerritoryLookupUnavailableException("the game server's answer was not understood");
            }
        }
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using knkwebapi_v2.Configuration;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.CloudflareAccess
{
    /// <summary>The API-owned allow-list behind the Access policy (DESIGN.md § 5.8).</summary>
    public interface ICloudflareAccessList
    {
        /// <summary>Every value in the list, as Cloudflare stores it.</summary>
        Task<IReadOnlyList<string>> GetEmailsAsync(CancellationToken ct = default);

        Task AddAsync(IReadOnlyCollection<string> emails, CancellationToken ct = default);

        /// <summary>Removes these values (as <see cref="GetEmailsAsync"/> returned them).</summary>
        Task RemoveAsync(IReadOnlyCollection<string> values, CancellationToken ct = default);
    }

    /// <summary>A Cloudflare API call that failed. The message never carries the token.</summary>
    public sealed class CloudflareApiException : Exception
    {
        public CloudflareApiException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// A Zero Trust list of type "User email addresses" (verified 2026-10-21, DESIGN.md § 5.8):
    /// <c>GET accounts/{account}/gateway/lists/{list}/items</c> pages through it and
    /// <c>PATCH accounts/{account}/gateway/lists/{list}</c> appends and removes. The Access policy
    /// includes the list with an <c>email_list</c> rule, which the developer sets once by hand.
    /// </summary>
    public sealed class ZeroTrustEmailList : ICloudflareAccessList
    {
        public const int PageSize = 1000;

        /// <summary>Values per PATCH, well below the 1,000-entry list limit.</summary>
        public const int PatchBatchSize = 200;

        public const string ItemDescription = "knk-web-api";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IHttpClientFactory _clients;
        private readonly CloudflareAccessSettings _settings;

        public ZeroTrustEmailList(IHttpClientFactory clients, IOptions<CloudflareAccessSettings> settings)
        {
            _clients = clients;
            _settings = settings.Value;
        }

        private string ListPath =>
            $"accounts/{Uri.EscapeDataString(_settings.AccountId ?? string.Empty)}/gateway/lists/{Uri.EscapeDataString(_settings.ListId ?? string.Empty)}";

        public async Task<IReadOnlyList<string>> GetEmailsAsync(CancellationToken ct = default)
        {
            var client = _clients.CreateClient(CloudflareAccessSettings.HttpClientName);
            var values = new List<string>();
            for (var page = 1; ; page++)
            {
                using var response = await client.GetAsync($"{ListPath}/items?page={page}&per_page={PageSize}", ct);
                var envelope = await ReadAsync<List<ListItem>>(response, "read the list", ct);
                var items = envelope.Result ?? new List<ListItem>();
                values.AddRange(items.Where(i => !string.IsNullOrWhiteSpace(i.Value)).Select(i => i.Value!));
                var totalPages = envelope.ResultInfo?.TotalPages;
                if (items.Count == 0 || (totalPages.HasValue ? page >= totalPages.Value : items.Count < PageSize))
                {
                    return values;
                }
            }
        }

        public async Task AddAsync(IReadOnlyCollection<string> emails, CancellationToken ct = default)
        {
            foreach (var batch in emails.Chunk(PatchBatchSize))
            {
                await PatchAsync(new PatchBody { Append = batch.Select(e => new ListItem { Value = e, Description = ItemDescription }).ToList() }, "add to the list", ct);
            }
        }

        public async Task RemoveAsync(IReadOnlyCollection<string> values, CancellationToken ct = default)
        {
            foreach (var batch in values.Chunk(PatchBatchSize))
            {
                await PatchAsync(new PatchBody { Remove = batch.ToList() }, "remove from the list", ct);
            }
        }

        private async Task PatchAsync(PatchBody body, string what, CancellationToken ct)
        {
            var client = _clients.CreateClient(CloudflareAccessSettings.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Patch, ListPath) { Content = JsonContent.Create(body, options: Json) };
            using var response = await client.SendAsync(request, ct);
            await ReadAsync<JsonElement>(response, what, ct);
        }

        private static async Task<Envelope<T>> ReadAsync<T>(HttpResponseMessage response, string what, CancellationToken ct)
        {
            Envelope<T>? envelope = null;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, ct);
            }
            catch (JsonException)
            {
                // Not Cloudflare's JSON (a proxy error page, say): reported by status below.
            }
            if (response.IsSuccessStatusCode && envelope is { Success: true })
            {
                return envelope;
            }
            var errors = envelope?.Errors is { Count: > 0 }
                ? string.Join("; ", envelope.Errors.Select(e => $"{e.Code}: {e.Message}"))
                : "no error details";
            throw new CloudflareApiException($"Cloudflare refused to {what}: HTTP {(int)response.StatusCode} ({errors}).");
        }

        private sealed class Envelope<T>
        {
            public bool Success { get; set; }
            public List<ApiError>? Errors { get; set; }
            public T? Result { get; set; }

            [JsonPropertyName("result_info")]
            public ResultInfo? ResultInfo { get; set; }
        }

        private sealed class ApiError
        {
            public int Code { get; set; }
            public string? Message { get; set; }
        }

        private sealed class ResultInfo
        {
            [JsonPropertyName("total_pages")]
            public int? TotalPages { get; set; }
        }

        private sealed class ListItem
        {
            [JsonPropertyName("value")]
            public string? Value { get; set; }

            [JsonPropertyName("description")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Description { get; set; }
        }

        private sealed class PatchBody
        {
            [JsonPropertyName("append")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<ListItem>? Append { get; set; }

            [JsonPropertyName("remove")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<string>? Remove { get; set; }
        }
    }
}

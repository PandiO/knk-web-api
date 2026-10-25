using System.Globalization;
using System.Net;
using System.Text.Json;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>Icon sources for the Minecraft catalogue (config section <c>MinecraftCatalog:Icons</c>, KNG-200 plan §7).</summary>
public sealed class MinecraftIconOptions
{
    public const string SectionName = "MinecraftCatalog:Icons";

    public bool Enabled { get; set; } = true;

    /// <summary>PrismarineJS minecraft-assets: <c>{base}/{version}/items_textures.json</c> etc.</summary>
    public string PrismarineBaseUrl { get; set; } = "https://raw.githubusercontent.com/PrismarineJS/minecraft-assets/master/data";

    /// <summary>Versions to try below the scanned one, newest first after sorting; a 404 moves on to the next.</summary>
    public List<string> PrismarineVersions { get; set; } = new()
    {
        "26.1", "1.21.11", "1.21.10", "1.21.9", "1.21.8", "1.21.7", "1.21.6", "1.21.5", "1.21.4",
    };

    /// <summary>InventivetalentDev minecraft-assets: one branch per version with the raw textures.</summary>
    public string InventivetalentBaseUrl { get; set; } = "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets";

    /// <summary>Guess <c>textures/item|block/{name}.png</c> on the scanned version's branch for keys Prismarine lacks.</summary>
    public bool InventivetalentFallback { get; set; } = true;

    public int MaxConcurrentChecks { get; set; } = 8;

    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>Outcome of resolving one key.</summary>
public enum MinecraftIconOutcome
{
    /// <summary>An icon URL was found.</summary>
    Resolved,
    /// <summary>The sources answered, and none has this key.</summary>
    Missing,
    /// <summary>A source could not be reached: keep whatever the row had.</summary>
    Unavailable,
}

public sealed record MinecraftIconResult(MinecraftIconOutcome Outcome, string? Url, string? Source);

public sealed record MinecraftIconRequest(string Key, bool IsItem, bool IsBlock);

public interface IMinecraftIconResolver
{
    /// <summary>Resolves icons for the normalised keys of one scan. Never throws for network problems.</summary>
    Task<IReadOnlyDictionary<string, MinecraftIconResult>> ResolveAsync(
        string? minecraftVersion, IReadOnlyCollection<MinecraftIconRequest> entries, CancellationToken ct);
}

/// <summary>
/// Resolves icons from public, versioned asset repositories (KNG-200 plan §7). PrismarineJS first,
/// through its texture mapping (<c>minecraft:block/x</c> → <c>blocks/x.png</c>, the path the old static
/// catalogue got wrong), at the scanned version or the nearest lower one it has. Keys it lacks are
/// guessed on InventivetalentDev's branch for the scanned version and checked with HEAD requests.
/// Runs before the ingest transaction; no HTTP happens inside it.
/// </summary>
public sealed class MinecraftIconResolver : IMinecraftIconResolver
{
    private const string MinecraftNamespace = "minecraft:";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MinecraftIconOptions _options;
    private readonly ILogger<MinecraftIconResolver> _logger;

    public MinecraftIconResolver(IHttpClientFactory httpClientFactory, MinecraftIconOptions options, ILogger<MinecraftIconResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, MinecraftIconResult>> ResolveAsync(
        string? minecraftVersion, IReadOnlyCollection<MinecraftIconRequest> entries, CancellationToken ct)
    {
        var results = new Dictionary<string, MinecraftIconResult>(StringComparer.Ordinal);
        if (!_options.Enabled || entries.Count == 0) return results;

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));

        var prismarine = await LoadPrismarineAsync(client, minecraftVersion, ct);
        var unresolved = new List<MinecraftIconRequest>();
        foreach (var entry in entries)
        {
            if (!entry.Key.StartsWith(MinecraftNamespace, StringComparison.Ordinal))
            {
                results[entry.Key] = new MinecraftIconResult(MinecraftIconOutcome.Missing, null, null);
                continue;
            }
            var url = prismarine?.Find(entry);
            if (url != null)
            {
                results[entry.Key] = new MinecraftIconResult(MinecraftIconOutcome.Resolved, url, $"prismarine@{prismarine!.Version}");
            }
            else
            {
                unresolved.Add(entry);
            }
        }

        var fallbackVersion = string.IsNullOrWhiteSpace(minecraftVersion) ? null : minecraftVersion.Trim();
        if (unresolved.Count > 0 && _options.InventivetalentFallback && fallbackVersion != null)
        {
            using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentChecks));
            var guesses = await Task.WhenAll(unresolved.Select(async entry =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return (entry.Key, Result: await GuessInventivetalentAsync(client, fallbackVersion, entry, ct));
                }
                finally
                {
                    gate.Release();
                }
            }));
            foreach (var (key, result) in guesses) results[key] = result;
        }
        else
        {
            var outcome = prismarine == null ? MinecraftIconOutcome.Unavailable : MinecraftIconOutcome.Missing;
            foreach (var entry in unresolved) results[entry.Key] = new MinecraftIconResult(outcome, null, null);
        }

        _logger.LogInformation("Minecraft icons for {Version}: {Resolved} resolved, {Missing} missing, {Unavailable} unavailable (prismarine {Prismarine})",
            minecraftVersion, results.Values.Count(r => r.Outcome == MinecraftIconOutcome.Resolved),
            results.Values.Count(r => r.Outcome == MinecraftIconOutcome.Missing),
            results.Values.Count(r => r.Outcome == MinecraftIconOutcome.Unavailable),
            prismarine?.Version ?? "none");
        return results;
    }

    /// <summary>The scanned version first, then every configured version below it, newest first.</summary>
    public static IReadOnlyList<string> CandidateVersions(string? scanned, IEnumerable<string> known)
    {
        var candidates = new List<string>();
        var scannedParts = ParseVersion(scanned);
        if (scannedParts != null) candidates.Add(scanned!.Trim());
        candidates.AddRange(known
            .Select(v => (Version: v.Trim(), Parts: ParseVersion(v)))
            .Where(v => v.Parts != null && (scannedParts == null || CompareVersions(v.Parts, scannedParts) < 0))
            .OrderByDescending(v => v.Parts!, Comparer<int[]>.Create(CompareVersions))
            .Select(v => v.Version));
        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>A Prismarine texture id as a path below the version folder, or null (<c>minecraft:block/x</c> → <c>blocks/x</c>).</summary>
    public static string? PrismarineTexturePath(string? texture)
    {
        if (string.IsNullOrWhiteSpace(texture)) return null;
        var path = texture.StartsWith(MinecraftNamespace, StringComparison.Ordinal) ? texture[MinecraftNamespace.Length..] : texture;
        if (path.Length == 0 || path == "missingno") return null;
        var slash = path.IndexOf('/');
        if (slash < 0) return null;
        var folder = path[..slash] switch
        {
            "block" => "blocks",
            "item" => "items",
            var other => other,
        };
        return folder + path[slash..];
    }

    private async Task<PrismarineTextures?> LoadPrismarineAsync(HttpClient client, string? version, CancellationToken ct)
    {
        var baseUrl = _options.PrismarineBaseUrl.TrimEnd('/');
        foreach (var candidate in CandidateVersions(version, _options.PrismarineVersions))
        {
            try
            {
                var items = await GetJsonAsync(client, $"{baseUrl}/{candidate}/items_textures.json", ct);
                if (items == null) continue; // 404: this version isn't there
                var blocks = await GetJsonAsync(client, $"{baseUrl}/{candidate}/blocks_textures.json", ct);
                return new PrismarineTextures(candidate, $"{baseUrl}/{candidate}", ReadTextures(items), ReadTextures(blocks));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "Prismarine icon mapping for {Version} could not be loaded", candidate);
                return null;
            }
        }
        return null;
    }

    private static async Task<JsonDocument?> GetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static Dictionary<string, string> ReadTextures(JsonDocument? document)
    {
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document == null) return textures;
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) return textures;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var name = element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var texture = element.TryGetProperty("texture", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var path = PrismarineTexturePath(texture);
                if (name != null && path != null) textures.TryAdd(name, path);
            }
        }
        return textures;
    }

    private async Task<MinecraftIconResult> GuessInventivetalentAsync(HttpClient client, string version, MinecraftIconRequest entry, CancellationToken ct)
    {
        var name = entry.Key[MinecraftNamespace.Length..];
        var baseUrl = $"{_options.InventivetalentBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(version)}/assets/minecraft/textures";
        var folders = entry.IsItem ? new[] { "item", "block" } : new[] { "block", "item" };
        try
        {
            foreach (var folder in folders)
            {
                var url = $"{baseUrl}/{folder}/{name}.png";
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) return new MinecraftIconResult(MinecraftIconOutcome.Resolved, url, $"inventivetalent@{version}");
                if (response.StatusCode != HttpStatusCode.NotFound) return new MinecraftIconResult(MinecraftIconOutcome.Unavailable, null, null);
            }
            return new MinecraftIconResult(MinecraftIconOutcome.Missing, null, $"inventivetalent@{version}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new MinecraftIconResult(MinecraftIconOutcome.Unavailable, null, null);
        }
    }

    private static int[]? ParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Trim().Split('.');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return null;
        }
        return numbers;
    }

    private static int CompareVersions(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private sealed record PrismarineTextures(string Version, string BaseUrl, Dictionary<string, string> Items, Dictionary<string, string> Blocks)
    {
        public string? Find(MinecraftIconRequest entry)
        {
            var name = entry.Key[MinecraftNamespace.Length..];
            var path = (entry.IsItem ? Items.GetValueOrDefault(name) : null) ?? Blocks.GetValueOrDefault(name)
                ?? (entry.IsItem ? null : Items.GetValueOrDefault(name));
            return path == null ? null : $"{BaseUrl}/{path}.png";
        }
    }
}

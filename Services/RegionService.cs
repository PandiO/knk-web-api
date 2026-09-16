using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service for managing WorldGuard region operations via Minecraft plugin.
    /// Handles region renaming after entity creation/updates.
    /// </summary>
    public interface IRegionService
    {
        /// <summary>
        /// Rename a region from oldRegionId to newRegionId via the Minecraft plugin.
        /// </summary>
        Task<bool> RenameRegionAsync(string oldRegionId, string newRegionId);

        /// <summary>
        /// Check whether a location (x,z) lies inside the specified region.
        /// </summary>
        Task<bool> IsLocationInsideRegionAsync(string regionId, double x, double z, bool allowBoundary = false);

        /// <summary>
        /// Check whether a child region is contained within a parent region.
        /// </summary>
        Task<bool> IsRegionContainedAsync(string parentRegionId, string childRegionId, bool requireFullContainment = true);
    }

    /// <summary>
    /// Thrown when the Minecraft plugin's region HTTP endpoint cannot be reached at all
    /// (server/plugin offline, network unreachable, or request timed out) — distinct from
    /// a definitive "not contained" result returned by a reachable plugin.
    /// </summary>
    public class RegionServiceUnavailableException : Exception
    {
        public RegionServiceUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public class RegionService : IRegionService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<RegionService> _logger;
        private readonly string _minecraftPluginBaseUrl;

        public RegionService(IHttpClientFactory httpClientFactory, ILogger<RegionService> logger, string minecraftPluginBaseUrl)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _minecraftPluginBaseUrl = minecraftPluginBaseUrl;
        }

        public async Task<bool> IsLocationInsideRegionAsync(string regionId, double x, double z, bool allowBoundary = false)
        {
            Console.WriteLine("[VALIDATION_TRACE_BACKEND]     RegionService.IsLocationInsideRegionAsync started");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       regionId: {regionId}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       x: {x}, z: {z}, allowBoundary: {allowBoundary}");

            if (string.IsNullOrWhiteSpace(regionId))
            {
                Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Validation failed: regionId is null/empty");
                throw new ArgumentException("regionId is required.");
            }

            try
            {
                using var client = _httpClientFactory.CreateClient();
                var xInvariant = x.ToString(CultureInfo.InvariantCulture);
                var zInvariant = z.ToString(CultureInfo.InvariantCulture);
                var url = $"{_minecraftPluginBaseUrl.TrimEnd('/')}/api/regions/{Uri.EscapeDataString(regionId)}/contains-location?x={xInvariant}&z={zInvariant}&allowBoundary={allowBoundary.ToString().ToLowerInvariant()}";
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       GET {url}");
                var response = await client.GetAsync(url);
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Response status: {(int)response.StatusCode} ({response.StatusCode})");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Region containment HTTP call failed, returning false");
                    _logger.LogWarning("Region containment check failed for {RegionId} (HTTP {StatusCode})", regionId, response.StatusCode);
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Response content: {content}");
                if (bool.TryParse(content, out var result))
                {
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Parsed result: {result}");
                    return result;
                }

                Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Unexpected response format, returning false");
                _logger.LogWarning("Unexpected response when checking region containment for {RegionId}: {Content}", regionId, content);
                return false;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Plugin unreachable: {ex.Message}");
                _logger.LogError(ex, "Minecraft plugin unreachable while checking if location is inside region {RegionId}", regionId);
                throw new RegionServiceUnavailableException(
                    $"Could not reach the Minecraft plugin at {_minecraftPluginBaseUrl}. Make sure the Minecraft server and knk-plugin are running.", ex);
            }
            catch (TaskCanceledException ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Plugin request timed out: {ex.Message}");
                _logger.LogError(ex, "Minecraft plugin request timed out while checking if location is inside region {RegionId}", regionId);
                throw new RegionServiceUnavailableException(
                    $"Timed out waiting for the Minecraft plugin at {_minecraftPluginBaseUrl}. Make sure the Minecraft server and knk-plugin are running and responsive.", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Exception: {ex.Message}");
                _logger.LogError(ex, "Error checking if location is inside region {RegionId}", regionId);
                throw;
            }
        }

        public async Task<bool> IsRegionContainedAsync(string parentRegionId, string childRegionId, bool requireFullContainment = true)
        {
            Console.WriteLine("[VALIDATION_TRACE_BACKEND]     RegionService.IsRegionContainedAsync started");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       parentRegionId: {parentRegionId}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       childRegionId: {childRegionId}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       requireFullContainment: {requireFullContainment}");

            if (string.IsNullOrWhiteSpace(parentRegionId) || string.IsNullOrWhiteSpace(childRegionId))
            {
                Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Validation failed: parentRegionId or childRegionId is null/empty");
                throw new ArgumentException("Both parentRegionId and childRegionId are required.");
            }

            try
            {
                using var client = _httpClientFactory.CreateClient();
                var url = $"{_minecraftPluginBaseUrl.TrimEnd('/')}/api/regions/{Uri.EscapeDataString(parentRegionId)}/contains-region/{Uri.EscapeDataString(childRegionId)}?requireFullContainment={requireFullContainment.ToString().ToLowerInvariant()}";
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       GET {url}");
                var response = await client.GetAsync(url);
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Response status: {(int)response.StatusCode} ({response.StatusCode})");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Region-to-region containment HTTP call failed, returning false");
                    _logger.LogWarning("Region containment check failed for {ParentRegionId}->{ChildRegionId} (HTTP {StatusCode})", parentRegionId, childRegionId, response.StatusCode);
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Response content: {content}");
                if (bool.TryParse(content, out var result))
                {
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Parsed result: {result}");
                    return result;
                }

                Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Unexpected response format, returning false");
                _logger.LogWarning("Unexpected response when checking region containment {ParentRegionId}->{ChildRegionId}: {Content}", parentRegionId, childRegionId, content);
                return false;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Plugin unreachable: {ex.Message}");
                _logger.LogError(ex, "Minecraft plugin unreachable while checking region containment {ParentRegionId}->{ChildRegionId}", parentRegionId, childRegionId);
                throw new RegionServiceUnavailableException(
                    $"Could not reach the Minecraft plugin at {_minecraftPluginBaseUrl}. Make sure the Minecraft server and knk-plugin are running.", ex);
            }
            catch (TaskCanceledException ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Plugin request timed out: {ex.Message}");
                _logger.LogError(ex, "Minecraft plugin request timed out while checking region containment {ParentRegionId}->{ChildRegionId}", parentRegionId, childRegionId);
                throw new RegionServiceUnavailableException(
                    $"Timed out waiting for the Minecraft plugin at {_minecraftPluginBaseUrl}. Make sure the Minecraft server and knk-plugin are running and responsive.", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Exception: {ex.Message}");
                _logger.LogError(ex, "Error checking region containment {ParentRegionId}->{ChildRegionId}", parentRegionId, childRegionId);
                throw;
            }
        }

        public async Task<bool> RenameRegionAsync(string oldRegionId, string newRegionId)
        {
            Console.WriteLine("[VALIDATION_TRACE_BACKEND]     RegionService.RenameRegionAsync started");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       oldRegionId: {oldRegionId}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       newRegionId: {newRegionId}");

            if (string.IsNullOrWhiteSpace(oldRegionId) || string.IsNullOrWhiteSpace(newRegionId))
            {
                Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Validation failed: oldRegionId or newRegionId is null/empty");
                throw new ArgumentException("Both oldRegionId and newRegionId are required.");
            }

            if (oldRegionId.Equals(newRegionId, StringComparison.OrdinalIgnoreCase))
            {
                // Already the desired name
                Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Region already has desired name, returning true");
                _logger.LogInformation($"Region already has the desired name: {oldRegionId}");
                return true;
            }

            try
            {
                // Call the Minecraft plugin API to rename the region
                using (var client = _httpClientFactory.CreateClient())
                {
                    var url = $"{_minecraftPluginBaseUrl.TrimEnd('/')}/Regions/rename?oldRegionId={Uri.EscapeDataString(oldRegionId)}&newRegionId={Uri.EscapeDataString(newRegionId)}";
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       POST {url}");
                    var response = await client.PostAsync(url, new StringContent(""));
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Response status: {(int)response.StatusCode} ({response.StatusCode})");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Response content: {content}");
                        bool result = content.Equals("true", StringComparison.OrdinalIgnoreCase);
                        Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Parsed rename result: {result}");
                        _logger.LogInformation($"Region rename completed: {oldRegionId} -> {newRegionId}, result: {result}");
                        return result;
                    }
                    else
                    {
                        Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Region rename HTTP call failed, returning false");
                        _logger.LogWarning($"Failed to rename region: HTTP {response.StatusCode}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Exception: {ex.Message}");
                _logger.LogError($"Error renaming region from {oldRegionId} to {newRegionId}: {ex.Message}");
                throw;
            }
        }
    }
}

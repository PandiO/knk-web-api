using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The dashboard's bulk actions (PandiO/knk-workspace#280, DESIGN.md § 4.7, § 4.8), run through each type's own
    /// services from <see cref="DashboardAccess"/>. The caller (DashboardController) has checked the nodes and limits.
    /// </summary>
    public interface IDashboardActionService
    {
        /// <summary>Deletes each id through the type's own delete, each in its own scope and save, so a refused row
        /// (still in use, not found, a business rule) leaves the others alone. Writes one audit entry.</summary>
        Task<DashboardBulkDeleteResponseDto> BulkDeleteAsync(DashboardTypeAccess type, IReadOnlyList<int> ids, int actorUserId);

        /// <summary>Reads the selected ids, or the whole search, through the type's own search in pages of
        /// <see cref="DashboardActionService.ExportPageSize"/>, at most <see cref="DashboardActionService.MaxExportRows"/>
        /// rows. Writes one audit entry.</summary>
        Task<DashboardExportResponseDto> ExportAsync(DashboardTypeAccess type, IReadOnlyList<int>? ids, DashboardExportQueryDto? query, int actorUserId);
    }

    public class DashboardActionService : IDashboardActionService
    {
        public const int MaxBulkDeleteIds = 100;
        public const int MaxExportRows = 10_000;
        public const int ExportPageSize = 500;

        /// <summary>A selection export scans the search for its ids; it stops after this many rows.</summary>
        public const int MaxSelectionScanRows = 100_000;

        public const string NotFoundReason = "Not found (already deleted?).";
        public const string FailedReason = "This item couldn't be deleted.";

        private static readonly ConcurrentDictionary<Type, PropertyInfo?> IdProperties = new();

        private readonly IServiceScopeFactory _scopes;
        private readonly IAuditLogService _audit;
        private readonly ILogger<DashboardActionService> _logger;

        public DashboardActionService(IServiceScopeFactory scopes, IAuditLogService audit, ILogger<DashboardActionService> logger)
        {
            _scopes = scopes;
            _audit = audit;
            _logger = logger;
        }

        public async Task<DashboardBulkDeleteResponseDto> BulkDeleteAsync(DashboardTypeAccess type, IReadOnlyList<int> ids, int actorUserId)
        {
            if (type.Delete == null) throw new ArgumentException($"{type.TypeName} can't be deleted.", nameof(type));

            var result = new DashboardBulkDeleteResponseDto();
            foreach (var id in ids)
            {
                var reason = await DeleteOneAsync(type, id);
                if (reason == null)
                {
                    result.Deleted.Add(id);
                }
                else
                {
                    result.Refused.Add(new DashboardBulkDeleteRefusalDto { Id = id, Reason = reason });
                }
            }

            await _audit.RecordAsync(actorUserId, actorUserId, AuditAction.DashboardBulkDelete, JsonSerializer.Serialize(new
            {
                entityType = type.TypeName,
                requested = ids.Count,
                count = result.Deleted.Count,
                ids = result.Deleted,
                refused = result.Refused.Count,
            }));
            _logger.LogInformation("Bulk delete of {EntityType}: {Deleted} deleted, {Refused} refused", type.TypeName,
                result.Deleted.Count, result.Refused.Count);
            return result;
        }

        /// <summary>Null when deleted, else the reason, worded as the single delete's answer would be.</summary>
        private async Task<string?> DeleteOneAsync(DashboardTypeAccess type, int id)
        {
            try
            {
                // A fresh scope per row: a failed save must not leave its tracked entities for the next row's save.
                using var scope = _scopes.CreateScope();
                await type.Delete!(scope.ServiceProvider, id);
                return null;
            }
            catch (KeyNotFoundException)
            {
                return NotFoundReason;
            }
            catch (Exception ex) when (DeleteConstraintExceptionFilter.IsConstraintFailure(ex))
            {
                return await DescribeConstraintAsync(type, id);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Business rules (has children, still in a lootbox pool, …): their messages are written for admins.
                return ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Bulk delete of {EntityType} {Id} failed: {Error}", type.TypeName, id, ex.GetType().Name);
                return FailedReason;
            }
        }

        private async Task<string> DescribeConstraintAsync(DashboardTypeAccess type, int id)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var references = scope.ServiceProvider.GetRequiredService<IEntityReferenceService>();
                var found = await references.GetReferencesAsync(type.TypeName, id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    blockingOnly: true);
                return DeleteConstraintExceptionFilter.DescribeRefusal(found);
            }
            catch (Exception ex)
            {
                // The reason is a courtesy, as in DeleteConstraintExceptionFilter.
                _logger.LogWarning("Could not list what still uses {EntityType} {Id}: {Error}", type.TypeName, id, ex.GetType().Name);
                return DeleteConstraintExceptionFilter.DescribeRefusal(null);
            }
        }

        public async Task<DashboardExportResponseDto> ExportAsync(DashboardTypeAccess type, IReadOnlyList<int>? ids, DashboardExportQueryDto? query,
            int actorUserId)
        {
            if (type.Search == null) throw new ArgumentException($"{type.TypeName} has no list to export.", nameof(type));

            var selection = ids is { Count: > 0 } ? new HashSet<int>(ids) : null;
            var result = selection == null
                ? await ReadSearchAsync(type, query)
                : await ReadSelectionAsync(type, query, selection);

            await _audit.RecordAsync(actorUserId, actorUserId, AuditAction.DashboardExport, JsonSerializer.Serialize(new
            {
                entityType = type.TypeName,
                scope = selection == null ? "search" : "selection",
                count = result.Items.Count,
                truncated = result.Truncated,
            }));
            _logger.LogInformation("Export of {EntityType}: {Count} rows (truncated: {Truncated})", type.TypeName, result.Items.Count, result.Truncated);
            return result;
        }

        private async Task<DashboardExportResponseDto> ReadSearchAsync(DashboardTypeAccess type, DashboardExportQueryDto? query)
        {
            var result = new DashboardExportResponseDto();
            var total = 0;
            using var scope = _scopes.CreateScope();
            for (var pageNumber = 1; result.Items.Count < MaxExportRows; pageNumber++)
            {
                var page = await type.Search!(scope.ServiceProvider, PageQuery(query, pageNumber));
                total = page.Total;
                if (page.Items.Count == 0) break;
                result.Items.AddRange(page.Items.Take(MaxExportRows - result.Items.Count));
                if (result.Items.Count >= total) break;
            }
            result.Total = Math.Max(total, result.Items.Count);
            result.Truncated = result.Items.Count < result.Total;
            return result;
        }

        private async Task<DashboardExportResponseDto> ReadSelectionAsync(DashboardTypeAccess type, DashboardExportQueryDto? query, HashSet<int> selection)
        {
            var result = new DashboardExportResponseDto();
            var found = new HashSet<int>();
            var scanned = 0;
            var exhausted = false;
            using var scope = _scopes.CreateScope();
            for (var pageNumber = 1; found.Count < selection.Count && scanned < MaxSelectionScanRows; pageNumber++)
            {
                var page = await type.Search!(scope.ServiceProvider, PageQuery(query, pageNumber));
                if (page.Items.Count == 0)
                {
                    exhausted = true;
                    break;
                }
                scanned += page.Items.Count;
                foreach (var item in page.Items)
                {
                    if (IdOf(item) is int id && selection.Contains(id) && found.Add(id) && result.Items.Count < MaxExportRows)
                    {
                        result.Items.Add(item);
                    }
                }
                if (scanned >= page.Total)
                {
                    exhausted = true;
                    break;
                }
            }
            result.Total = found.Count;
            // Stopped by the scan limit with ids still missing: say so rather than pretend they don't exist.
            result.Truncated = result.Items.Count < found.Count || (!exhausted && found.Count < selection.Count);
            return result;
        }

        private static PagedQueryDto PageQuery(DashboardExportQueryDto? query, int pageNumber) => new()
        {
            PageNumber = pageNumber,
            PageSize = ExportPageSize,
            SearchTerm = query?.SearchTerm,
            Filters = query?.Filters,
            SortBy = query?.SortBy,
            SortDescending = query?.SortDescending ?? false,
        };

        /// <summary>The row's <c>Id</c> (every dashboard list DTO has one), or null.</summary>
        public static int? IdOf(object item)
        {
            var property = IdProperties.GetOrAdd(item.GetType(), t => t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
            return property?.GetValue(item) switch
            {
                int i => i,
                long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
                _ => null,
            };
        }
    }
}

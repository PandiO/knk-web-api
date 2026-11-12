using System.Globalization;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// "Show details" of a refused delete (PandiO/knk-workspace#44): which rows still use a record, by name, with no more
    /// than the caller may see under the #280 model (knk-workspace docs/specs/dashboard-access-model/DESIGN.md § 4.6):
    /// <list type="bullet">
    ///   <item>rows of a type the caller reads in full (one of its read nodes): id + lookup label, <c>readable</c>, so the
    ///     web app links them;</item>
    ///   <item>rows of another dashboard type: id + lookup label when the caller holds any area node (what
    ///     <c>api/Lookup</c> gives them anyway), else only the count with the nodes that would read them;</item>
    ///   <item>rows that aren't a dashboard type (form fields, drafts, sessions): only the count.</item>
    /// </list>
    /// The references are the refusal message's own (blocking only, EntityReferenceService.BlocksDelete). Labels come
    /// from <see cref="ILookupService"/>, so a user is named by the username and nothing else.
    /// </summary>
    public interface IEntityReferenceRowsService
    {
        /// <summary>Null when the record's type isn't a dashboard type or the id doesn't fit it.</summary>
        Task<EntityReferenceRowsDto?> GetAsync(DashboardTypeAccess type, int id, int userId, string? referencedBy, int take,
            HttpContext? http = null);
    }

    public class EntityReferenceRowsService : IEntityReferenceRowsService
    {
        public const int DefaultTake = 20;
        public const int MaxTake = 50;

        public const string NotADashboardType = "These records aren't listed here.";

        private readonly IEntityReferenceService _references;
        private readonly ILookupService _lookups;
        private readonly IPermissionResolutionService _permissions;
        private readonly IPermissionReadCache _cache;

        public EntityReferenceRowsService(IEntityReferenceService references, ILookupService lookups,
            IPermissionResolutionService permissions, IPermissionReadCache cache)
        {
            _references = references;
            _lookups = lookups;
            _permissions = permissions;
            _cache = cache;
        }

        public async Task<EntityReferenceRowsDto?> GetAsync(DashboardTypeAccess type, int id, int userId, string? referencedBy,
            int take, HttpContext? http = null)
        {
            var groups = await _references.GetReferencingRowsAsync(type.TypeName, id.ToString(CultureInfo.InvariantCulture), blockingOnly: true);
            if (groups == null) return null;
            take = Math.Clamp(take, 1, MaxTake);

            var result = new EntityReferenceRowsDto { EntityTypeName = type.TypeName, Id = id };
            bool? anyAreaNode = null;
            foreach (var group in groups)
            {
                if (!string.IsNullOrWhiteSpace(referencedBy)
                    && !string.Equals(group.ReferencedBy, referencedBy.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rowType = DashboardAccess.Find(group.EntityTypeName);
                var dto = new EntityReferenceGroupDto
                {
                    ReferencedBy = group.ReferencedBy,
                    EntityType = rowType?.TypeName ?? group.EntityTypeName,
                    Count = group.Count,
                };
                result.Groups.Add(dto);

                if (rowType == null || group.Ids.Count == 0)
                {
                    dto.Reason = NotADashboardType;
                    continue;
                }

                dto.Nodes = rowType.ReadNodes.ToList();
                dto.Readable = await _cache.HoldsAnyAsync(_permissions, userId, rowType.ReadNodes, http);
                if (!dto.Readable)
                {
                    anyAreaNode ??= await _cache.HoldsAnyAsync(_permissions, userId, DashboardAccess.AreaNodes, http);
                    if (anyAreaNode != true)
                    {
                        dto.Reason = $"Listing them requires one of: {string.Join(", ", rowType.ReadNodes)}.";
                        continue;
                    }
                }

                var ids = group.Ids
                    .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
                    .Where(n => n > 0)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
                dto.Listed = true;
                dto.More = ids.Count > take;
                foreach (var rowId in ids.Take(take))
                {
                    dto.Rows.Add(await LabelAsync(rowType, rowId));
                }
            }
            return result;
        }

        /// <summary>The lookup label, or "<Type> #id" when the type has no lookup by id or the row can't be read.</summary>
        private async Task<LookupItemDto> LabelAsync(DashboardTypeAccess type, int id)
        {
            if (type.LookupGettable)
            {
                try
                {
                    var item = await _lookups.GetAsync(type, id);
                    if (item != null) return item;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A label is a courtesy: one row that can't be loaded keeps its id.
                }
            }
            return new LookupItemDto { Id = id, Label = $"{type.TypeName} #{id}" };
        }
    }
}

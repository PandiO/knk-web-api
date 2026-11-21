using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Logistics
{
    public interface ILogisticsPolicyService
    {
        Task<LogisticsPolicyDto?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>Filters: scope, townId, warehouseId, lineageId, current (true by default; false = superseded only,
        /// all = every version), specialization; search term = name.</summary>
        Task<PagedResultDto<LogisticsPolicyListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);

        /// <summary>Every version of the policy's lineage, newest first.</summary>
        Task<List<LogisticsPolicyListDto>> VersionsAsync(int id, CancellationToken ct = default);

        /// <summary>Version 1 of a new policy. 400 on invalid rules; 409 when the town or warehouse already has one.</summary>
        Task<LogisticsPolicyDto> CreateAsync(LogisticsPolicyDto dto, int? actorUserId, CancellationToken ct = default);

        /// <summary>A save makes version n + 1 with the given rules and supersedes <paramref name="id"/>, which must be the
        /// current version (409 otherwise). Returns the new version.</summary>
        Task<LogisticsPolicyDto> UpdateAsync(int id, LogisticsPolicyDto dto, int? actorUserId, CancellationToken ct = default);

        /// <summary>Deletes the policy with all its versions (planner runs keep the plain id).</summary>
        Task DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>Copies a template's rules into the town's policy: a new version of it, or its version 1.</summary>
        Task<LogisticsPolicyDto> ApplyTemplateAsync(int templateId, LogisticsApplyTemplateDto dto, int? actorUserId, CancellationToken ct = default);

        Task<LogisticsRuleDto?> GetRuleAsync(int id, CancellationToken ct = default);

        /// <summary>The town's size and specialization labels with the template that fits them (#928). 404 for an unknown town.</summary>
        Task<TownLogisticsProfileDto> GetTownProfileAsync(int townId, CancellationToken ct = default);

        /// <summary>Sets the town's labels: a null field is left as it is, an empty string clears it. 400 on an unknown label.</summary>
        Task<TownLogisticsProfileDto> UpdateTownProfileAsync(int townId, TownLogisticsProfileDto dto, CancellationToken ct = default);

        /// <summary>Rules across policies (dashboard): filters logisticsPolicyId, townId, type, current (true by default).</summary>
        Task<PagedResultDto<LogisticsRuleListDto>> SearchRulesAsync(PagedQueryDto query, CancellationToken ct = default);
    }

    /// <summary>
    /// Versioned logistics policies (TOWN_LOGISTICS § 4, § 4.1): a town's, a warehouse's, and the admin-edited default
    /// templates (D-L3). A version is never changed after it is written; a save is a new version ("every policy change is
    /// a new version, kept for audit and for the dashboard"). Works while Production:Enabled is off (DESIGN § 18.2).
    /// </summary>
    public sealed class LogisticsPolicyService : ILogisticsPolicyService
    {
        public const int MaxRules = 200;

        private readonly KnKDbContext _db;
        private readonly TimeProvider _clock;

        public LogisticsPolicyService(KnKDbContext db, TimeProvider? clock = null)
        {
            _db = db;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ================= Reads =================

        private IQueryable<LogisticsPolicy> Full() => _db.Set<LogisticsPolicy>().AsNoTracking().AsSplitQuery()
            .Include(p => p.Town).Include(p => p.Warehouse)
            .Include(p => p.Rules).ThenInclude(r => r.Warehouse)
            .Include(p => p.Rules).ThenInclude(r => r.ItemBlueprint)
            .Include(p => p.Rules).ThenInclude(r => r.Category)
            .Include(p => p.Rules).ThenInclude(r => r.Tag)
            .Include(p => p.Rules).ThenInclude(r => r.Structure)
            .Include(p => p.Rules).ThenInclude(r => r.ProductionKind)
            .Include(p => p.Rules).ThenInclude(r => r.District);

        public async Task<LogisticsPolicyDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var p = await Full().FirstOrDefaultAsync(x => x.Id == id, ct);
            return p == null ? null : ToDto(p);
        }

        public static LogisticsPolicyDto ToDto(LogisticsPolicy p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Scope = p.Scope.ToString(),
            TownId = p.TownId,
            Town = p.Town == null ? null : new NamedRefDto { Id = p.Town.Id, Name = p.Town.Name },
            WarehouseId = p.WarehouseId,
            Warehouse = p.Warehouse == null ? null : new NamedRefDto { Id = p.Warehouse.Id, Name = p.Warehouse.Name },
            Specialization = p.Specialization?.ToString(),
            TownSize = p.TownSize?.ToString(),
            IsDefaultTemplate = p.IsDefaultTemplate,
            Version = p.Version,
            LineageId = p.LineageId,
            IsCurrent = p.IsCurrent,
            BasedOnPolicyId = p.BasedOnPolicyId,
            SupersededByPolicyId = p.SupersededByPolicyId,
            SupersededAt = p.SupersededAt,
            Author = p.Author.ToString(),
            Reason = p.Reason,
            CreatedAt = p.CreatedAt,
            CreatedByUserId = p.CreatedByUserId,
            Rules = p.Rules.OrderBy(r => r.Sequence).ThenBy(r => r.Id).Select(RuleDto).ToList(),
        };

        public static LogisticsRuleDto RuleDto(LogisticsRule r) => new()
        {
            Id = r.Id,
            LogisticsPolicyId = r.LogisticsPolicyId,
            Sequence = r.Sequence,
            Type = r.Type.ToString(),
            Scope = r.Scope.ToString(),
            WarehouseId = r.WarehouseId,
            Warehouse = Ref(r.WarehouseId, r.Warehouse?.Name),
            TargetKind = r.TargetKind.ToString(),
            ItemBlueprintId = r.ItemBlueprintId,
            ItemBlueprint = Ref(r.ItemBlueprintId, r.ItemBlueprint?.Name),
            CategoryId = r.CategoryId,
            Category = Ref(r.CategoryId, r.Category?.Name),
            TagId = r.TagId,
            Tag = Ref(r.TagId, r.Tag?.Name),
            ConsumerKind = r.ConsumerKind.ToString(),
            StructureId = r.StructureId,
            Structure = Ref(r.StructureId, r.Structure?.Name),
            ProductionKindId = r.ProductionKindId,
            ProductionKind = Ref(r.ProductionKindId, r.ProductionKind?.Name),
            AreaKind = r.AreaKind?.ToString(),
            DistrictId = r.DistrictId,
            District = Ref(r.DistrictId, r.District?.Name),
            Units = r.Units,
            DaysOfCover = r.DaysOfCover,
            ReorderPoint = r.ReorderPoint,
            TargetLevel = r.TargetLevel,
            PriorityClass = r.PriorityClass?.ToString(),
            ActiveFrom = r.ActiveFrom,
            ActiveUntil = r.ActiveUntil,
            Author = r.Author.ToString(),
            Reason = r.Reason,
            Summary = Summarize(r),
        };

        private static NamedRefDto? Ref(int? id, string? name) => id == null ? null : new NamedRefDto { Id = id.Value, Name = name ?? $"#{id}" };

        /// <summary>One line per rule, for lists and the planner's explanations.</summary>
        public static string Summarize(LogisticsRule r)
        {
            string items = r.TargetKind switch
            {
                LogisticsTargetKind.Blueprint => r.ItemBlueprint?.Name ?? $"blueprint {r.ItemBlueprintId}",
                LogisticsTargetKind.Category => $"category {r.Category?.Name ?? r.CategoryId?.ToString()}",
                LogisticsTargetKind.Tag => $"tag {r.Tag?.Name ?? r.TagId?.ToString()}",
                _ => "every item",
            };
            string consumers = r.ConsumerKind switch
            {
                LogisticsConsumerKind.Structure => r.Structure?.Name ?? $"structure {r.StructureId}",
                LogisticsConsumerKind.ProductionKind => $"every {r.ProductionKind?.Name ?? $"kind {r.ProductionKindId}"}",
                _ => "every consumer",
            };
            string where = r.Scope == LogisticsRuleScope.Warehouse ? $"at {r.Warehouse?.Name ?? $"warehouse {r.WarehouseId}"}" : "town-wide";
            return r.Type switch
            {
                LogisticsRuleType.Reserve => r.DaysOfCover.HasValue
                    ? $"Reserve {r.DaysOfCover.Value:0.##} days of {items}, {where}"
                    : $"Reserve {r.Units ?? 0} {items}, {where}",
                LogisticsRuleType.PriorityClass => $"{r.PriorityClass} priority for {items} at {consumers}",
                LogisticsRuleType.ServiceArea => $"{(r.AreaKind switch
                {
                    LogisticsAreaKind.District => $"District {r.District?.Name ?? r.DistrictId?.ToString()}",
                    LogisticsAreaKind.Structure => r.Structure?.Name ?? $"structure {r.StructureId}",
                    _ => "The rural territory",
                })} → {r.Warehouse?.Name ?? $"warehouse {r.WarehouseId}"}",
                LogisticsRuleType.Replenishment => $"{consumers}: {items} min {r.ReorderPoint}, max {r.TargetLevel}",
                _ => r.Type.ToString(),
            };
        }

        public async Task<PagedResultDto<LogisticsPolicyListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<LogisticsPolicy> q = _db.Set<LogisticsPolicy>().AsNoTracking();
            var current = Filter(query, "current")?.ToLowerInvariant();
            if (current is null or "true") q = q.Where(p => p.CurrentKey != null);
            else if (current == "false") q = q.Where(p => p.CurrentKey == null);
            if (Filter(query, "scope") is { } sc && Enum.TryParse<LogisticsPolicyScope>(sc, true, out var scope)) q = q.Where(p => p.Scope == scope);
            if (TerritoryPaging.IntFilter(query, "townId") is { } town)
                q = q.Where(p => p.TownId == town || (p.Warehouse != null && (p.Warehouse.TownId == town || (p.Warehouse.District != null && p.Warehouse.District.TownId == town))));
            if (TerritoryPaging.IntFilter(query, "warehouseId") is { } wh) q = q.Where(p => p.WarehouseId == wh);
            if (TerritoryPaging.IntFilter(query, "lineageId") is { } lineage) q = q.Where(p => p.LineageId == lineage);
            if (Filter(query, "specialization") is { } spec && Enum.TryParse<ResourceCategory>(spec, true, out var s)) q = q.Where(p => p.Specialization == s);
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(p => p.Name.ToLower().Contains(term));
            q = q.OrderBy(p => p.Scope).ThenBy(p => p.Name).ThenByDescending(p => p.Version);
            return await TerritoryPaging.PageAsync(q.Select(ListProjection()), query);
        }

        private static System.Linq.Expressions.Expression<Func<LogisticsPolicy, LogisticsPolicyListDto>> ListProjection() => p => new LogisticsPolicyListDto
        {
            Id = p.Id,
            Name = p.Name,
            Scope = p.Scope.ToString(),
            TownId = p.TownId,
            TownName = p.Town != null ? p.Town.Name : null,
            WarehouseId = p.WarehouseId,
            WarehouseName = p.Warehouse != null ? p.Warehouse.Name : null,
            Specialization = p.Specialization != null ? p.Specialization.ToString() : null,
            TownSize = p.TownSize != null ? p.TownSize.ToString() : null,
            IsDefaultTemplate = p.IsDefaultTemplate,
            Version = p.Version,
            LineageId = p.LineageId,
            IsCurrent = p.CurrentKey != null,
            RuleCount = p.Rules.Count,
            Reason = p.Reason,
            CreatedAt = p.CreatedAt,
        };

        public async Task<List<LogisticsPolicyListDto>> VersionsAsync(int id, CancellationToken ct = default)
        {
            var lineage = await _db.Set<LogisticsPolicy>().AsNoTracking().Where(p => p.Id == id).Select(p => (int?)p.LineageId).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"Logistics policy {id} not found.");
            return await _db.Set<LogisticsPolicy>().AsNoTracking().Where(p => p.LineageId == lineage)
                .OrderByDescending(p => p.Version).Select(ListProjection()).ToListAsync(ct);
        }

        public async Task<LogisticsRuleDto?> GetRuleAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<LogisticsRule>().AsNoTracking()
                .Include(x => x.Warehouse).Include(x => x.ItemBlueprint).Include(x => x.Category).Include(x => x.Tag)
                .Include(x => x.Structure).Include(x => x.ProductionKind).Include(x => x.District)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return r == null ? null : RuleDto(r);
        }

        public async Task<PagedResultDto<LogisticsRuleListDto>> SearchRulesAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<LogisticsRule> q = _db.Set<LogisticsRule>().AsNoTracking()
                .Include(r => r.LogisticsPolicy).Include(r => r.Warehouse).Include(r => r.ItemBlueprint).Include(r => r.Category)
                .Include(r => r.Tag).Include(r => r.Structure).Include(r => r.ProductionKind).Include(r => r.District);
            var current = Filter(query, "current")?.ToLowerInvariant();
            if (current is null or "true") q = q.Where(r => r.LogisticsPolicy.CurrentKey != null);
            else if (current == "false") q = q.Where(r => r.LogisticsPolicy.CurrentKey == null);
            if (TerritoryPaging.IntFilter(query, "logisticsPolicyId") is { } pid) q = q.Where(r => r.LogisticsPolicyId == pid);
            if (TerritoryPaging.IntFilter(query, "townId") is { } town) q = q.Where(r => r.LogisticsPolicy.TownId == town);
            if (Filter(query, "type") is { } t && Enum.TryParse<LogisticsRuleType>(t, true, out var type)) q = q.Where(r => r.Type == type);
            q = q.OrderBy(r => r.LogisticsPolicyId).ThenBy(r => r.Sequence).ThenBy(r => r.Id);
            int size = Math.Clamp(query.PageSize, 1, TerritoryPaging.MaxPageSize);
            int number = Math.Max(1, query.PageNumber);
            int total = await q.CountAsync(ct);
            var rows = await q.Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return new PagedResultDto<LogisticsRuleListDto>
            {
                Items = rows.Select(r => new LogisticsRuleListDto
                {
                    Id = r.Id, LogisticsPolicyId = r.LogisticsPolicyId, PolicyName = r.LogisticsPolicy.Name,
                    PolicyVersion = r.LogisticsPolicy.Version, PolicyIsCurrent = r.LogisticsPolicy.IsCurrent, Sequence = r.Sequence,
                    Type = r.Type.ToString(), Scope = r.Scope.ToString(), Summary = Summarize(r), Reason = r.Reason,
                }).ToList(),
                TotalCount = total, PageNumber = number, PageSize = size,
            };
        }

        private static string? Filter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        // ================= Writes =================

        public async Task<LogisticsPolicyDto> CreateAsync(LogisticsPolicyDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var head = await ValidateHeadAsync(dto, null, ct);
            var rules = await ValidateRulesAsync(dto.Rules, head, ct);
            var key = head.Scope == LogisticsPolicyScope.Template ? null : LogisticsPolicy.KeyFor(head.Scope, head.TownId, head.WarehouseId, 0);
            if (key != null && await _db.Set<LogisticsPolicy>().AnyAsync(p => p.CurrentKey == key, ct))
                throw new InvalidOperationException(head.Scope == LogisticsPolicyScope.Town
                    ? $"Town {head.TownId} already has a logistics policy: edit it (a save makes a new version)."
                    : $"Warehouse {head.WarehouseId} already has a logistics policy: edit it (a save makes a new version).");
            var now = Now;
            await using var tx = await BeginAsync(ct);
            head.Version = 1;
            head.CreatedAt = now;
            head.CreatedByUserId = actorUserId;
            head.Reason = Clean(dto.Reason, LogisticsPolicy.ReasonMaxLength, "reason") ?? "Created.";
            head.CurrentKey = key ?? $"new:{Guid.NewGuid():N}";
            AddRules(head, rules);
            _db.Set<LogisticsPolicy>().Add(head);
            await SaveAsync(ct);
            head.LineageId = head.Id;
            head.CurrentKey = LogisticsPolicy.KeyFor(head.Scope, head.TownId, head.WarehouseId, head.Id);
            if (head.IsDefaultTemplate) await ClearOtherDefaultsAsync(head.LineageId, head.Id, ct);
            await SaveAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(head.Id, ct))!;
        }

        public async Task<LogisticsPolicyDto> UpdateAsync(int id, LogisticsPolicyDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var old = await _db.Set<LogisticsPolicy>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new KeyNotFoundException($"Logistics policy {id} not found.");
            if (!old.IsCurrent)
            {
                var cur = await _db.Set<LogisticsPolicy>().AsNoTracking().Where(p => p.LineageId == old.LineageId && p.CurrentKey != null)
                    .Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
                throw new InvalidOperationException($"Version {old.Version} is not the current version{(cur == null ? "" : $" (#{cur})")}: edit the current one.");
            }
            var head = await ValidateHeadAsync(dto, old, ct);
            var rules = await ValidateRulesAsync(dto.Rules, head, ct);
            return await NewVersionAsync(old, head, rules, Clean(dto.Reason, LogisticsPolicy.ReasonMaxLength, "reason") ?? "Edited.", old.Id, actorUserId, ct);
        }

        private async Task<LogisticsPolicyDto> NewVersionAsync(LogisticsPolicy old, LogisticsPolicy head, List<LogisticsRule> rules,
            string reason, int basedOn, int? actorUserId, CancellationToken ct)
        {
            var now = Now;
            await using var tx = await BeginAsync(ct);
            var tracked = await _db.Set<LogisticsPolicy>().FirstAsync(p => p.Id == old.Id, ct);
            if (_db.Database.IsRelational())
                await _db.Database.ExecuteSqlRawAsync($"SELECT Id FROM logistics_policies WHERE Id = {old.Id} FOR UPDATE", ct);
            await _db.Entry(tracked).ReloadAsync(ct);
            if (tracked.CurrentKey == null) throw new InvalidOperationException("Another save made a newer version first: reload and try again.");
            string key = tracked.CurrentKey;
            tracked.CurrentKey = null;
            tracked.SupersededAt = now;
            await SaveAsync(ct);
            head.Version = await _db.Set<LogisticsPolicy>().Where(p => p.LineageId == old.LineageId).MaxAsync(p => p.Version, ct) + 1;
            head.LineageId = old.LineageId;
            head.CurrentKey = key;
            head.BasedOnPolicyId = basedOn;
            head.CreatedAt = now;
            head.CreatedByUserId = actorUserId;
            head.Reason = reason;
            AddRules(head, rules);
            _db.Set<LogisticsPolicy>().Add(head);
            await SaveAsync(ct);
            tracked.SupersededByPolicyId = head.Id;
            if (head.IsDefaultTemplate) await ClearOtherDefaultsAsync(head.LineageId, head.Id, ct);
            await SaveAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(head.Id, ct))!;
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var lineage = await _db.Set<LogisticsPolicy>().AsNoTracking().Where(p => p.Id == id).Select(p => (int?)p.LineageId).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"Logistics policy {id} not found.");
            var rows = await _db.Set<LogisticsPolicy>().Include(p => p.Rules).Where(p => p.LineageId == lineage).ToListAsync(ct);
            _db.Set<LogisticsPolicy>().RemoveRange(rows);
            await SaveAsync(ct);
            _db.ChangeTracker.Clear();
        }

        public async Task<LogisticsPolicyDto> ApplyTemplateAsync(int templateId, LogisticsApplyTemplateDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var template = await _db.Set<LogisticsPolicy>().AsNoTracking().Include(p => p.Rules).FirstOrDefaultAsync(p => p.Id == templateId, ct)
                ?? throw new KeyNotFoundException($"Logistics policy {templateId} not found.");
            if (template.Scope != LogisticsPolicyScope.Template) throw new ArgumentException($"Policy {templateId} is not a template.");
            var town = await _db.Towns.AsNoTracking().Where(t => t.Id == dto.TownId).Select(t => new { t.Id, t.Name }).FirstOrDefaultAsync(ct)
                ?? throw new ArgumentException($"Town {dto.TownId} not found.");
            var reason = Clean(dto.Reason, LogisticsPolicy.ReasonMaxLength, "reason") ?? $"From template \"{template.Name}\" v{template.Version}.";
            var copy = template.Rules.OrderBy(r => r.Sequence).Select(CopyRule).ToList();
            string key = LogisticsPolicy.KeyFor(LogisticsPolicyScope.Town, town.Id, null, 0);
            var current = await _db.Set<LogisticsPolicy>().AsNoTracking().FirstOrDefaultAsync(p => p.CurrentKey == key, ct);
            var head = new LogisticsPolicy
            {
                Name = current?.Name ?? $"{town.Name} logistics", Scope = LogisticsPolicyScope.Town, TownId = town.Id, Author = LogisticsAuthor.Admin,
            };
            if (current != null) return await NewVersionAsync(current, head, copy, reason, template.Id, actorUserId, ct);
            var now = Now;
            await using var tx = await BeginAsync(ct);
            head.Version = 1;
            head.CurrentKey = key;
            head.BasedOnPolicyId = template.Id;
            head.CreatedAt = now;
            head.CreatedByUserId = actorUserId;
            head.Reason = reason;
            AddRules(head, copy);
            _db.Set<LogisticsPolicy>().Add(head);
            await SaveAsync(ct);
            head.LineageId = head.Id;
            await SaveAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(head.Id, ct))!;
        }

        private static LogisticsRule CopyRule(LogisticsRule r) => new()
        {
            Sequence = r.Sequence, Type = r.Type, Scope = r.Scope, WarehouseId = r.WarehouseId, TargetKind = r.TargetKind,
            ItemBlueprintId = r.ItemBlueprintId, CategoryId = r.CategoryId, TagId = r.TagId, ConsumerKind = r.ConsumerKind,
            StructureId = r.StructureId, ProductionKindId = r.ProductionKindId, AreaKind = r.AreaKind, DistrictId = r.DistrictId,
            Units = r.Units, DaysOfCover = r.DaysOfCover, ReorderPoint = r.ReorderPoint, TargetLevel = r.TargetLevel,
            PriorityClass = r.PriorityClass, ActiveFrom = r.ActiveFrom, ActiveUntil = r.ActiveUntil, Author = r.Author, Reason = r.Reason,
        };

        private static void AddRules(LogisticsPolicy head, List<LogisticsRule> rules)
        {
            int seq = 0;
            foreach (var r in rules)
            {
                r.Id = 0;
                r.Sequence = ++seq;
                head.Rules.Add(r);
            }
        }

        private async Task ClearOtherDefaultsAsync(int lineageId, int keepId, CancellationToken ct)
        {
            var others = await _db.Set<LogisticsPolicy>()
                .Where(p => p.Scope == LogisticsPolicyScope.Template && p.IsDefaultTemplate && p.CurrentKey != null && p.LineageId != lineageId && p.Id != keepId)
                .ToListAsync(ct);
            foreach (var o in others) o.IsDefaultTemplate = false;
        }

        // ================= Town profile (#928 point 1) =================

        public async Task<TownLogisticsProfileDto> GetTownProfileAsync(int townId, CancellationToken ct = default)
        {
            var town = await _db.Towns.AsNoTracking().Where(t => t.Id == townId)
                .Select(t => new { t.Id, t.Name, t.LogisticsTownSize, t.LogisticsSpecialization }).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"Town {townId} not found.");
            var dto = new TownLogisticsProfileDto
            {
                TownId = town.Id, TownName = town.Name, TownSize = town.LogisticsTownSize?.ToString(), Specialization = town.LogisticsSpecialization?.ToString(),
            };
            var (template, match) = await SuggestTemplateAsync(town.LogisticsTownSize, town.LogisticsSpecialization, ct);
            dto.SuggestedTemplateId = template?.Id;
            dto.SuggestedTemplateName = template?.Name;
            dto.SuggestedTemplateMatch = match;
            return dto;
        }

        public async Task<TownLogisticsProfileDto> UpdateTownProfileAsync(int townId, TownLogisticsProfileDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.TownId != 0 && dto.TownId != townId) throw new ArgumentException("townId can't change.");
            var town = await _db.Towns.FirstOrDefaultAsync(t => t.Id == townId, ct) ?? throw new KeyNotFoundException($"Town {townId} not found.");
            if (dto.TownSize != null) town.LogisticsTownSize = ParseTownSize(dto.TownSize);
            if (dto.Specialization != null) town.LogisticsSpecialization = ParseSpecialization(dto.Specialization);
            await SaveAsync(ct);
            _db.ChangeTracker.Clear();
            return await GetTownProfileAsync(townId, ct);
        }

        /// <summary>The current template for a size and specialization: both labels equal (Exact), else the same size with a
        /// general specialization (Size), else the default template (Default); the oldest lineage wins a tie.</summary>
        private async Task<(LogisticsPolicy? Template, string Match)> SuggestTemplateAsync(LogisticsTownSize? size, ResourceCategory? spec, CancellationToken ct)
        {
            var templates = await _db.Set<LogisticsPolicy>().AsNoTracking()
                .Where(p => p.Scope == LogisticsPolicyScope.Template && p.CurrentKey != null).OrderBy(p => p.LineageId).ToListAsync(ct);
            if (size != null || spec != null)
            {
                var exact = templates.FirstOrDefault(p => p.TownSize == size && p.Specialization == spec);
                if (exact != null) return (exact, "Exact");
                if (size != null && spec != null && templates.FirstOrDefault(p => p.TownSize == size && p.Specialization == null) is { } bySize)
                    return (bySize, "Size");
            }
            var fallback = templates.FirstOrDefault(p => p.IsDefaultTemplate);
            return fallback == null ? (null, "None") : (fallback, "Default");
        }

        // ================= Validation =================

        private async Task<LogisticsPolicy> ValidateHeadAsync(LogisticsPolicyDto dto, LogisticsPolicy? old, CancellationToken ct)
        {
            var name = Clean(dto.Name, LogisticsPolicy.NameMaxLength, "name") ?? old?.Name
                ?? throw new ArgumentException("name is required.");
            LogisticsPolicyScope scope;
            if (old != null)
            {
                scope = old.Scope;
                if (dto.Scope != null && !string.Equals(dto.Scope.Trim(), old.Scope.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("scope can't change: make a new policy.");
            }
            else if (!Enum.TryParse(dto.Scope?.Trim(), true, out scope) || !Enum.IsDefined(scope))
                throw new ArgumentException("scope must be Town, Warehouse or Template.");
            var head = new LogisticsPolicy { Name = name, Scope = scope, Author = LogisticsAuthor.Admin };
            if (dto.Author != null)
            {
                if (!Enum.TryParse<LogisticsAuthor>(dto.Author.Trim(), true, out var author) || !Enum.IsDefined(author))
                    throw new ArgumentException("author must be Admin or ClanCouncil.");
                head.Author = author;
            }
            switch (scope)
            {
                case LogisticsPolicyScope.Town:
                    head.TownId = old?.TownId ?? dto.TownId ?? throw new ArgumentException("townId is required for a town policy.");
                    if (old != null && dto.TownId.HasValue && dto.TownId != old.TownId) throw new ArgumentException("townId can't change.");
                    if (!await _db.Towns.AnyAsync(t => t.Id == head.TownId, ct)) throw new ArgumentException($"Town {head.TownId} not found.");
                    break;
                case LogisticsPolicyScope.Warehouse:
                    head.WarehouseId = old?.WarehouseId ?? dto.WarehouseId ?? throw new ArgumentException("warehouseId is required for a warehouse policy.");
                    if (old != null && dto.WarehouseId.HasValue && dto.WarehouseId != old.WarehouseId) throw new ArgumentException("warehouseId can't change.");
                    if (!await _db.Warehouses.AnyAsync(w => w.Id == head.WarehouseId, ct)) throw new ArgumentException($"Warehouse {head.WarehouseId} not found.");
                    break;
                default:
                    if (dto.TownId.HasValue || dto.WarehouseId.HasValue) throw new ArgumentException("A template has no town or warehouse.");
                    // #928: like name, a label left out (null) keeps the current version's value; an empty string clears it.
                    head.Specialization = dto.Specialization == null ? old?.Specialization : ParseSpecialization(dto.Specialization);
                    head.TownSize = dto.TownSize == null ? old?.TownSize : ParseTownSize(dto.TownSize);
                    head.IsDefaultTemplate = dto.IsDefaultTemplate ?? old?.IsDefaultTemplate ?? false;
                    break;
            }
            if (scope != LogisticsPolicyScope.Template && (!string.IsNullOrWhiteSpace(dto.Specialization) || !string.IsNullOrWhiteSpace(dto.TownSize) || dto.IsDefaultTemplate == true))
                throw new ArgumentException("specialization, townSize and isDefaultTemplate are for templates only.");
            return head;
        }

        /// <summary>A specialization label: empty = general (null); otherwise a <see cref="ResourceCategory"/> name.</summary>
        internal static ResourceCategory? ParseSpecialization(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!Enum.TryParse<ResourceCategory>(value.Trim(), true, out var spec) || !Enum.IsDefined(spec))
                throw new ArgumentException($"specialization must be one of {string.Join(", ", Enum.GetNames<ResourceCategory>())} (or empty for general).");
            return spec;
        }

        /// <summary>A town size label: empty = none (null); otherwise Village, Town or City.</summary>
        internal static LogisticsTownSize? ParseTownSize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!Enum.TryParse<LogisticsTownSize>(value.Trim(), true, out var size) || !Enum.IsDefined(size))
                throw new ArgumentException("townSize must be Village, Town or City (or empty).");
            return size;
        }

        /// <summary>The town a policy's places must be in (null for templates).</summary>
        private async Task<int?> TownOfAsync(LogisticsPolicy head, CancellationToken ct)
        {
            if (head.Scope == LogisticsPolicyScope.Town) return head.TownId;
            if (head.Scope == LogisticsPolicyScope.Warehouse)
                return await _db.Warehouses.AsNoTracking().Where(w => w.Id == head.WarehouseId)
                    .Select(w => w.District != null ? (int?)w.District.TownId : w.TownId).FirstOrDefaultAsync(ct);
            return null;
        }

        private async Task<List<LogisticsRule>> ValidateRulesAsync(List<LogisticsRuleDto>? dtos, LogisticsPolicy head, CancellationToken ct)
        {
            var list = (dtos ?? new()).Where(r => r != null).ToList();
            if (list.Count > MaxRules) throw new ArgumentException($"A policy has at most {MaxRules} rules.");
            int? town = await TownOfAsync(head, ct);
            var errors = new List<string>();
            var rules = new List<LogisticsRule>();
            int n = 0;
            foreach (var d in list.OrderBy(x => x.Sequence))
            {
                n++;
                string at = $"Rule {n}: ";
                var r = new LogisticsRule { Reason = d.Reason?.Trim() };
                if (r.Reason?.Length > LogisticsRule.ReasonMaxLength) errors.Add($"{at}reason is at most {LogisticsRule.ReasonMaxLength} characters.");
                if (string.IsNullOrEmpty(r.Reason)) r.Reason = null;
                if (!Enum.TryParse<LogisticsRuleType>(d.Type?.Trim(), true, out var type) || !Enum.IsDefined(type))
                {
                    errors.Add($"{at}type must be Reserve, PriorityClass, ServiceArea or Replenishment (ceilings, quotas, rebalance, export/import, emergency modes and tax are Later, #324).");
                    continue;
                }
                r.Type = type;
                if (d.Author != null)
                {
                    if (Enum.TryParse<LogisticsAuthor>(d.Author.Trim(), true, out var a) && Enum.IsDefined(a)) r.Author = a;
                    else errors.Add($"{at}author must be Admin or ClanCouncil.");
                }
                // Scope and warehouse.
                if (d.Scope != null && (!Enum.TryParse<LogisticsRuleScope>(d.Scope.Trim(), true, out var sc) || !Enum.IsDefined(sc)))
                    errors.Add($"{at}scope must be Town or Warehouse.");
                else r.Scope = d.Scope == null ? LogisticsRuleScope.Town : Enum.Parse<LogisticsRuleScope>(d.Scope.Trim(), true);
                r.WarehouseId = d.WarehouseId;
                if (head.Scope == LogisticsPolicyScope.Warehouse)
                {
                    if (type is not (LogisticsRuleType.Reserve or LogisticsRuleType.ServiceArea))
                        errors.Add($"{at}a warehouse policy holds local reserves and service areas only.");
                    r.Scope = type == LogisticsRuleType.Reserve ? LogisticsRuleScope.Warehouse : LogisticsRuleScope.Town;
                    if (d.WarehouseId.HasValue && d.WarehouseId != head.WarehouseId) errors.Add($"{at}a warehouse policy's rules are about its own warehouse.");
                    r.WarehouseId = head.WarehouseId;
                }
                if (head.Scope == LogisticsPolicyScope.Template)
                {
                    if (r.Scope == LogisticsRuleScope.Warehouse || type == LogisticsRuleType.ServiceArea || d.WarehouseId.HasValue)
                        errors.Add($"{at}a template is for any town: no warehouse rules or service areas.");
                    if (d.StructureId.HasValue || d.DistrictId.HasValue) errors.Add($"{at}a template is for any town: no structure or district.");
                }
                bool needsWarehouse = (type == LogisticsRuleType.Reserve && r.Scope == LogisticsRuleScope.Warehouse) || type == LogisticsRuleType.ServiceArea;
                if (needsWarehouse)
                {
                    if (r.WarehouseId == null) errors.Add($"{at}warehouseId is required.");
                    else if (town != null && !await WarehouseInTownAsync(r.WarehouseId.Value, town.Value, ct))
                        errors.Add($"{at}warehouse {r.WarehouseId} is not a warehouse of this town.");
                }
                else if (type != LogisticsRuleType.Reserve || r.Scope == LogisticsRuleScope.Town) r.WarehouseId = null;

                // Items.
                if (type != LogisticsRuleType.ServiceArea)
                {
                    if (d.TargetKind != null && (!Enum.TryParse<LogisticsTargetKind>(d.TargetKind.Trim(), true, out var tk) || !Enum.IsDefined(tk)))
                        errors.Add($"{at}targetKind must be Any, Blueprint, Category or Tag.");
                    else r.TargetKind = d.TargetKind == null
                        ? (d.ItemBlueprintId.HasValue ? LogisticsTargetKind.Blueprint : LogisticsTargetKind.Any)
                        : Enum.Parse<LogisticsTargetKind>(d.TargetKind.Trim(), true);
                    switch (r.TargetKind)
                    {
                        case LogisticsTargetKind.Blueprint:
                            if (d.ItemBlueprintId is not { } b || !await _db.ItemBlueprints.AnyAsync(x => x.Id == b, ct)) errors.Add($"{at}an existing itemBlueprintId is required.");
                            r.ItemBlueprintId = d.ItemBlueprintId;
                            break;
                        case LogisticsTargetKind.Category:
                            if (d.CategoryId is not { } c || !await _db.Categories.AnyAsync(x => x.Id == c, ct)) errors.Add($"{at}an existing categoryId is required.");
                            r.CategoryId = d.CategoryId;
                            break;
                        case LogisticsTargetKind.Tag:
                            if (d.TagId is not { } t || !await _db.Tags.AnyAsync(x => x.Id == t, ct)) errors.Add($"{at}an existing tagId is required.");
                            r.TagId = d.TagId;
                            break;
                    }
                }

                // Consumers.
                if (type is LogisticsRuleType.PriorityClass or LogisticsRuleType.Replenishment)
                {
                    if (d.ConsumerKind != null && (!Enum.TryParse<LogisticsConsumerKind>(d.ConsumerKind.Trim(), true, out var ck) || !Enum.IsDefined(ck)))
                        errors.Add($"{at}consumerKind must be Any, Structure or ProductionKind.");
                    else r.ConsumerKind = d.ConsumerKind == null
                        ? (d.StructureId.HasValue ? LogisticsConsumerKind.Structure : d.ProductionKindId.HasValue ? LogisticsConsumerKind.ProductionKind : LogisticsConsumerKind.Any)
                        : Enum.Parse<LogisticsConsumerKind>(d.ConsumerKind.Trim(), true);
                    if (r.ConsumerKind == LogisticsConsumerKind.Structure)
                    {
                        if (d.StructureId is not { } s || !await StructureInTownAsync(s, town, ct)) errors.Add($"{at}a structure of this town is required.");
                        r.StructureId = d.StructureId;
                    }
                    if (r.ConsumerKind == LogisticsConsumerKind.ProductionKind)
                    {
                        if (d.ProductionKindId is not { } k || !await _db.ProductionKinds.AnyAsync(x => x.Id == k, ct)) errors.Add($"{at}an existing productionKindId is required.");
                        r.ProductionKindId = d.ProductionKindId;
                    }
                }

                // Parameters per type.
                switch (type)
                {
                    case LogisticsRuleType.Reserve:
                        if (d.Units.HasValue == d.DaysOfCover.HasValue) errors.Add($"{at}a reserve has units or daysOfCover (one of them).");
                        if (d.Units is < 0 or > LogisticsRule.MaxUnits) errors.Add($"{at}units must be 0 to {LogisticsRule.MaxUnits}.");
                        if (d.DaysOfCover is <= 0 or > LogisticsRule.MaxDays) errors.Add($"{at}daysOfCover must be above 0 and at most {LogisticsRule.MaxDays}.");
                        r.Units = d.Units;
                        r.DaysOfCover = d.DaysOfCover;
                        break;
                    case LogisticsRuleType.PriorityClass:
                        if (!Enum.TryParse<LogisticsPriorityClass>(d.PriorityClass?.Trim(), true, out var pc) || !Enum.IsDefined(pc))
                            errors.Add($"{at}priorityClass must be Critical, High, Normal or Low.");
                        else r.PriorityClass = pc;
                        break;
                    case LogisticsRuleType.ServiceArea:
                        if (!Enum.TryParse<LogisticsAreaKind>(d.AreaKind?.Trim(), true, out var ak) || !Enum.IsDefined(ak))
                        {
                            errors.Add($"{at}areaKind must be District, Rural or Structure.");
                            break;
                        }
                        r.AreaKind = ak;
                        if (ak == LogisticsAreaKind.District)
                        {
                            if (d.DistrictId is not { } di || !await _db.Districts.AnyAsync(x => x.Id == di && (town == null || x.TownId == town), ct))
                                errors.Add($"{at}a district of this town is required.");
                            r.DistrictId = d.DistrictId;
                        }
                        if (ak == LogisticsAreaKind.Structure)
                        {
                            if (d.StructureId is not { } s || !await StructureInTownAsync(s, town, ct)) errors.Add($"{at}a structure of this town is required.");
                            r.StructureId = d.StructureId;
                        }
                        break;
                    case LogisticsRuleType.Replenishment:
                        if (d.ReorderPoint is not { } rp || d.TargetLevel is not { } tl || rp < 0 || tl <= 0 || rp > tl || tl > LogisticsRule.MaxUnits)
                            errors.Add($"{at}reorderPoint (0 or more) and targetLevel (above 0, at most {LogisticsRule.MaxUnits}) are required, reorderPoint ≤ targetLevel.");
                        r.ReorderPoint = d.ReorderPoint;
                        r.TargetLevel = d.TargetLevel;
                        break;
                }
                r.ActiveFrom = Utc(d.ActiveFrom);
                r.ActiveUntil = Utc(d.ActiveUntil);
                if (r.ActiveFrom.HasValue && r.ActiveUntil.HasValue && r.ActiveUntil <= r.ActiveFrom) errors.Add($"{at}activeUntil must be after activeFrom.");
                rules.Add(r);
            }
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
            return rules;
        }

        private static DateTime? Utc(DateTime? d) => d.HasValue ? DateTime.SpecifyKind(d.Value.ToUniversalTime(), DateTimeKind.Utc) : null;

        private Task<bool> WarehouseInTownAsync(int warehouseId, int townId, CancellationToken ct) =>
            _db.Warehouses.AnyAsync(w => w.Id == warehouseId && (w.TownId == townId || (w.District != null && w.District.TownId == townId)), ct);

        private Task<bool> StructureInTownAsync(int structureId, int? townId, CancellationToken ct) =>
            _db.Structures.AnyAsync(s => s.Id == structureId && (townId == null || s.TownId == townId || (s.District != null && s.District.TownId == townId)), ct);

        private static string? Clean(string? value, int max, string field)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (v.Length > max) throw new ArgumentException($"{field} is at most {max} characters.");
            return v;
        }

        private async Task SaveAsync(CancellationToken ct)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
                throw new InvalidOperationException("Another save of this policy (or a rule's target) got in first, or a rule points at something that is gone: reload and try again.", ex);
            }
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
            _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct)
                : null;
    }
}

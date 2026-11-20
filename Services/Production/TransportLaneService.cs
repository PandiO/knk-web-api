using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    public interface ITransportLaneService
    {
        Task<TransportLaneDto?> GetByIdAsync(int id);
        Task<PagedResultDto<TransportLaneListDto>> SearchAsync(PagedQueryDto query);
        Task<TransportLaneDto> CreateAsync(TransportLaneDto dto);
        Task<TransportLaneDto> UpdateAsync(int id, TransportLaneDto dto);
        Task DeleteAsync(int id);

        /// <summary>The game server's measurements (P5b): upserted by (from, to), source Plugin.</summary>
        Task<TransportLaneReportResultDto> ReportAsync(TransportLaneReportDto dto, CancellationToken ct = default);

        /// <summary>The pairs to measure: each production structure to every warehouse of its town, and the warehouses of a
        /// town among themselves (re-routes), never measured or older than <c>laneMaxAgeHours</c>. Empty while
        /// Production:Enabled is off (A21).</summary>
        Task<List<TransportLaneDueDto>> DueAsync(int limit, CancellationToken ct = default);
    }

    /// <summary>Transport lanes (production DESIGN § 7.6, P5 #79): the read model the simulated ETA uses.</summary>
    public sealed class TransportLaneService : ITransportLaneService
    {
        public const int MaxReport = 500;
        public const int MaxDue = 500;
        public const decimal MaxDistance = 1_000_000m;

        private readonly KnKDbContext _db;
        private readonly IProductionSettingsReader _settings;
        private readonly TimeProvider _clock;

        public TransportLaneService(KnKDbContext db, IProductionSettingsReader settings, TimeProvider? clock = null)
        {
            _db = db;
            _settings = settings;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        public async Task<TransportLaneDto?> GetByIdAsync(int id)
        {
            var l = await _db.TransportLanes.AsNoTracking().Include(x => x.FromStructure).Include(x => x.ToStructure)
                .FirstOrDefaultAsync(x => x.Id == id);
            return l == null ? null : ToDto(l);
        }

        public async Task<PagedResultDto<TransportLaneListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<TransportLane> q = _db.TransportLanes.AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "fromStructureId") is { } f) q = q.Where(l => l.FromStructureId == f);
            if (TerritoryPaging.IntFilter(query, "toStructureId") is { } t) q = q.Where(l => l.ToStructureId == t);
            if (TerritoryPaging.IntFilter(query, "structureId") is { } s) q = q.Where(l => l.FromStructureId == s || l.ToStructureId == s);
            if (query.Filters != null && query.Filters.TryGetValue("source", out var src) && Enum.TryParse<TransportLaneSource>(src, true, out var source))
                q = q.Where(l => l.Source == source);
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(l => l.FromStructure.Name.ToLower().Contains(term) || l.ToStructure.Name.ToLower().Contains(term));
            q = query.SortBy != null && !query.SortDescending ? q.OrderBy(l => l.Id) : q.OrderByDescending(l => l.MeasuredAt).ThenByDescending(l => l.Id);
            var page = await TerritoryPaging.PageAsync(q.Select(l => new TransportLaneListDto
            {
                Id = l.Id,
                FromStructureId = l.FromStructureId,
                FromStructureName = l.FromStructure.Name,
                ToStructureId = l.ToStructureId,
                ToStructureName = l.ToStructure.Name,
                Reachable = l.Reachable,
                RoadDistance = l.RoadDistance,
                StraightDistance = l.StraightDistance,
                Source = l.Source.ToString(),
                MeasuredAt = l.MeasuredAt,
            }), query);
            return page;
        }

        public async Task<TransportLaneDto> CreateAsync(TransportLaneDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var lane = new TransportLane();
            await ApplyAsync(lane, dto, isNew: true, defaultSource: TransportLaneSource.Manual);
            if (await _db.TransportLanes.AnyAsync(l => l.FromStructureId == lane.FromStructureId && l.ToStructureId == lane.ToStructureId))
                throw new InvalidOperationException("A lane between these two structures (in this direction) already exists; edit it instead.");
            _db.TransportLanes.Add(lane);
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(lane.Id))!;
        }

        public async Task<TransportLaneDto> UpdateAsync(int id, TransportLaneDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var lane = await _db.TransportLanes.FirstOrDefaultAsync(l => l.Id == id)
                ?? throw new KeyNotFoundException($"TransportLane with id {id} not found.");
            await ApplyAsync(lane, dto, isNew: false, defaultSource: lane.Source);
            if (await _db.TransportLanes.AnyAsync(l => l.Id != id && l.FromStructureId == lane.FromStructureId && l.ToStructureId == lane.ToStructureId))
                throw new InvalidOperationException("Another lane between these two structures (in this direction) exists.");
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(id))!;
        }

        public async Task DeleteAsync(int id)
        {
            var lane = await _db.TransportLanes.FirstOrDefaultAsync(l => l.Id == id)
                ?? throw new KeyNotFoundException($"TransportLane with id {id} not found.");
            _db.TransportLanes.Remove(lane);
            await _db.SaveChangesAsync();
        }

        public async Task<TransportLaneReportResultDto> ReportAsync(TransportLaneReportDto dto, CancellationToken ct = default)
        {
            if (dto?.Lanes == null) throw new ArgumentException("lanes is required.");
            if (dto.Lanes.Count > MaxReport) throw new ArgumentException($"At most {MaxReport} lanes per report.");
            var result = new TransportLaneReportResultDto();
            foreach (var item in dto.Lanes)
            {
                if (item == null) throw new ArgumentException("A lane is empty.");
                var lane = item.FromStructureId.HasValue && item.ToStructureId.HasValue
                    ? await _db.TransportLanes.FirstOrDefaultAsync(l => l.FromStructureId == item.FromStructureId && l.ToStructureId == item.ToStructureId, ct)
                    : null;
                bool isNew = lane == null;
                lane ??= new TransportLane();
                item.Source = TransportLaneSource.Plugin.ToString();
                item.MeasuredAt ??= Now;
                await ApplyAsync(lane, item, isNew, TransportLaneSource.Plugin);
                if (isNew)
                {
                    _db.TransportLanes.Add(lane);
                    result.Created++;
                }
                else result.Updated++;
            }
            await _db.SaveChangesAsync(ct);
            return result;
        }

        private async Task ApplyAsync(TransportLane lane, TransportLaneDto dto, bool isNew, TransportLaneSource defaultSource)
        {
            int from = dto.FromStructureId ?? (isNew ? throw new ArgumentException("fromStructureId is required.") : lane.FromStructureId);
            int to = dto.ToStructureId ?? (isNew ? throw new ArgumentException("toStructureId is required.") : lane.ToStructureId);
            if (from == to) throw new ArgumentException("A lane joins two different structures.");
            foreach (var id in new[] { from, to })
                if (!await _db.Structures.AnyAsync(s => s.Id == id)) throw new ArgumentException($"Structure {id} not found.");
            lane.FromStructureId = from;
            lane.ToStructureId = to;
            lane.Reachable = dto.Reachable ?? (isNew ? true : lane.Reachable);
            if (dto.RoadDistance is < 0 or > MaxDistance) throw new ArgumentException($"roadDistance must be 0 to {MaxDistance}.");
            if (dto.StraightDistance is < 0 or > MaxDistance) throw new ArgumentException($"straightDistance must be 0 to {MaxDistance}.");
            if (dto.MeasuredSeconds is < 0) throw new ArgumentException("measuredSeconds can't be negative.");
            lane.RoadDistance = lane.Reachable ? dto.RoadDistance ?? (isNew ? null : lane.RoadDistance) : null;
            if (lane.Reachable && lane.RoadDistance == null)
                throw new ArgumentException("roadDistance is required for a reachable lane (reachable = false when there is no road).");
            lane.StraightDistance = dto.StraightDistance ?? (isNew ? null : lane.StraightDistance);
            lane.MeasuredSeconds = dto.MeasuredSeconds ?? (isNew ? null : lane.MeasuredSeconds);
            if (dto.RouteEdgeIds != null)
            {
                var json = JsonSerializer.Serialize(dto.RouteEdgeIds);
                if (json.Length > TransportLane.EdgeIdsMaxLength) throw new ArgumentException("routeEdgeIds is too long.");
                lane.RouteEdgeIdsJson = json;
            }
            var source = defaultSource;
            if (!string.IsNullOrWhiteSpace(dto.Source))
            {
                if (!Enum.TryParse<TransportLaneSource>(dto.Source.Trim(), true, out source) || !Enum.IsDefined(source))
                    throw new ArgumentException("source must be Plugin or Manual.");
            }
            lane.Source = source;
            lane.MeasuredAt = dto.MeasuredAt.HasValue ? DateTime.SpecifyKind(dto.MeasuredAt.Value.ToUniversalTime(), DateTimeKind.Utc)
                : isNew || dto.RoadDistance.HasValue || dto.Reachable.HasValue ? Now : lane.MeasuredAt;
            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            if (note?.Length > TransportLane.NoteMaxLength) throw new ArgumentException($"note is at most {TransportLane.NoteMaxLength} characters.");
            if (dto.Note != null || isNew) lane.Note = note;
        }

        public async Task<List<TransportLaneDueDto>> DueAsync(int limit, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            var list = new List<TransportLaneDueDto>();
            if (!settings.Enabled) return list;
            limit = Math.Clamp(limit, 1, MaxDue);
            var staleBefore = Now.AddHours(-settings.Transport!.LaneMaxAgeHours);

            var warehouses = await _db.Warehouses.AsNoTracking().Include(w => w.District).Include(w => w.Location)
                .Where(w => w.AcceptsDeliveries).ToListAsync(ct);
            var producers = await _db.ProductionStructures.AsNoTracking().Include(p => p.District).Include(p => p.Location).ToListAsync(ct);
            var lanes = await _db.TransportLanes.AsNoTracking().ToListAsync(ct);
            var pairs = new List<(Structure A, Structure B)>();
            foreach (var p in producers.OrderBy(p => p.Id))
                foreach (var w in warehouses.Where(w => w.EffectiveTownId != null && w.EffectiveTownId == p.EffectiveTownId).OrderBy(w => w.Id))
                    pairs.Add((p, w));
            foreach (var a in warehouses.OrderBy(w => w.Id))
                foreach (var b in warehouses.Where(b => b.Id > a.Id && b.EffectiveTownId != null && b.EffectiveTownId == a.EffectiveTownId).OrderBy(b => b.Id))
                    pairs.Add((a, b));

            foreach (var (a, b) in pairs)
            {
                var lane = lanes.Where(l => (l.FromStructureId == a.Id && l.ToStructureId == b.Id) || (l.FromStructureId == b.Id && l.ToStructureId == a.Id))
                    .OrderByDescending(l => l.MeasuredAt).FirstOrDefault();
                string? reason = lane == null ? "Missing"
                    : lane.Source == TransportLaneSource.Plugin && lane.MeasuredAt < staleBefore ? "Stale" : null;
                if (reason == null) continue;
                list.Add(new TransportLaneDueDto
                {
                    FromStructureId = a.Id, FromStructureName = a.Name, From = Point(a),
                    ToStructureId = b.Id, ToStructureName = b.Name, To = Point(b),
                    Reason = reason, LastMeasuredAt = lane?.MeasuredAt,
                });
                if (list.Count >= limit) break;
            }
            return list;
        }

        private static TransportPointDto? Point(Structure s) =>
            s.Location == null ? null : new TransportPointDto { World = s.Location.World, X = s.Location.X, Y = s.Location.Y, Z = s.Location.Z };

        private static TransportLaneDto ToDto(TransportLane l) => new()
        {
            Id = l.Id,
            FromStructureId = l.FromStructureId,
            FromStructure = new NamedRefDto { Id = l.FromStructureId, Name = l.FromStructure?.Name ?? $"#{l.FromStructureId}" },
            ToStructureId = l.ToStructureId,
            ToStructure = new NamedRefDto { Id = l.ToStructureId, Name = l.ToStructure?.Name ?? $"#{l.ToStructureId}" },
            Reachable = l.Reachable,
            RoadDistance = l.RoadDistance,
            StraightDistance = l.StraightDistance,
            RouteEdgeIds = string.IsNullOrEmpty(l.RouteEdgeIdsJson) ? null : JsonSerializer.Deserialize<List<long>>(l.RouteEdgeIdsJson),
            MeasuredSeconds = l.MeasuredSeconds,
            Source = l.Source.ToString(),
            MeasuredAt = l.MeasuredAt,
            Note = l.Note,
        };
    }
}

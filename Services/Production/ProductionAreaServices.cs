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
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Services.ValidationMethods;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    // Yield areas and surveys P4 (PandiO/knk-workspace#138): admin CRUD of areas and Hybrid overrides (always on, DESIGN
    // § 18.2), the survey queue and report the game server uses (only while Production:Enabled is on, A21), the
    // survey history, and the materialization of a structure's outputs into lines.

    // ===================================================================== Materialization

    public interface IProductionYieldService
    {
        /// <summary>Makes sure every output the structure yields has a line (PRODUCTION_KINDS § 4.1). Settles first.</summary>
        Task<YieldMaterializationDto> MaterializeAsync(int structureId, CancellationToken ct = default);

        /// <summary>The structure's yield with every rate explained (the web app's survey viewer and rate explanation).</summary>
        Task<ProductionYieldDto?> ExplainAsync(int structureId, CancellationToken ct = default);
    }

    public sealed class ProductionYieldService : IProductionYieldService
    {
        private readonly KnKDbContext _db;
        private readonly IProductionYieldCalculator _calculator;
        private readonly IProductionEngine _engine;
        private readonly IProductionSettingsReader _settings;
        private readonly TimeProvider _clock;

        public ProductionYieldService(KnKDbContext db, IProductionYieldCalculator calculator, IProductionEngine engine,
            IProductionSettingsReader settings, TimeProvider? clock = null)
        {
            _db = db;
            _calculator = calculator;
            _engine = engine;
            _settings = settings;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<YieldMaterializationDto> MaterializeAsync(int structureId, CancellationToken ct = default)
        {
            var structure = await _db.ProductionStructures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == structureId, ct)
                ?? throw new KeyNotFoundException($"ProductionStructure with id {structureId} not found.");
            await _engine.SettleStructureAsync(structureId, ct);
            _db.ChangeTracker.Clear();
            var result = new YieldMaterializationDto { ProductionStructureId = structureId, YieldSource = structure.YieldSource.ToString() };
            var lines = await _db.ProductionLines.Where(l => l.ProductionStructureId == structureId).OrderBy(l => l.Id).ToListAsync(ct);

            if (structure.YieldSource == ProductionRateSource.Configured)
            {
                // A Configured structure is never surveyed (G5): its survey lines become Configured lines (rate kept).
                foreach (var line in lines.Where(l => l.RateSource != ProductionRateSource.Configured))
                {
                    line.RateSource = ProductionRateSource.Configured;
                    result.LinesUpdated.Add(line.Id);
                }
                if (result.LinesUpdated.Count > 0)
                    result.Notes.Add("The structure is Configured: its survey lines now use their configured rate.");
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
                return result;
            }

            var yield = await _calculator.ComputeAsync(structureId, structure.YieldSource == ProductionRateSource.Hybrid, ct);
            var wanted = yield.Outputs.Where(o => o.BaseRatePerHour > 0m).Select(o => o.ItemBlueprintId).ToHashSet();
            var now = _clock.GetUtcNow().UtcDateTime;
            var engineSettings = (await _settings.GetAsync()).Engine;
            var level = await ProductionLevels.FindAsync(_db, structure.ProductionKindId, structure.Level, ct);
            var created = new List<ProductionLine>();
            foreach (var bp in wanted.OrderBy(b => b))
            {
                var line = lines.FirstOrDefault(l => l.ItemBlueprintId == bp);
                if (line == null)
                {
                    line = new ProductionLine
                    {
                        ProductionStructureId = structureId, ItemBlueprintId = bp, RateSource = structure.YieldSource,
                        AnchorAt = now, LastSettledAt = now, NextSettleAt = now + ProductionRate.Cycle(engineSettings, level), CreatedAt = now,
                    };
                    _db.ProductionLines.Add(line);
                    created.Add(line);
                }
                else if (line.RateSource == ProductionRateSource.Configured)
                {
                    result.SkippedConfigured.Add(line.Id);
                }
                else if (line.RateSource != structure.YieldSource)
                {
                    line.RateSource = structure.YieldSource;
                    result.LinesUpdated.Add(line.Id);
                }
            }
            foreach (var line in lines.Where(l => l.RateSource != ProductionRateSource.Configured && !wanted.Contains(l.ItemBlueprintId)))
            {
                if (line.RateSource != structure.YieldSource)
                {
                    line.RateSource = structure.YieldSource;
                    result.LinesUpdated.Add(line.Id);
                }
                result.LinesWithoutYield.Add(line.Id);
            }
            await _db.SaveChangesAsync(ct);
            result.LinesCreated.AddRange(created.Select(l => l.Id));
            if (result.SkippedConfigured.Count > 0)
                result.Notes.Add("Outputs with a Configured line keep that line and its configured rate.");
            if (result.LinesWithoutYield.Count > 0)
                result.Notes.Add("Lines whose output the latest surveys no longer yield are kept with a base rate of 0.");
            _db.ChangeTracker.Clear();
            return result;
        }

        public async Task<ProductionYieldDto?> ExplainAsync(int structureId, CancellationToken ct = default)
        {
            var structure = await _db.ProductionStructures.AsNoTracking().Include(s => s.ProductionKind)
                .FirstOrDefaultAsync(s => s.Id == structureId, ct);
            if (structure == null) return null;
            bool hybrid = structure.YieldSource == ProductionRateSource.Hybrid;
            var yield = await _calculator.ComputeAsync(structureId, hybrid, ct);
            var level = await ProductionLevels.FindAsync(_db, structure.ProductionKindId, structure.Level, ct);
            var lines = await _db.ProductionLines.AsNoTracking().Where(l => l.ProductionStructureId == structureId).ToListAsync(ct);
            var dto = new ProductionYieldDto
            {
                ProductionStructureId = structureId,
                YieldSource = structure.YieldSource.ToString(),
                ProductionKind = structure.ProductionKind == null ? null
                    : new ProductionKindNavDto { Id = structure.ProductionKind.Id, Name = structure.ProductionKind.Name },
                Level = structure.Level,
                SurveysRun = structure.YieldSource != ProductionRateSource.Configured,
                Areas = yield.Areas.Select(a => ProductionAreaService.ToDto(a, structure, level)).ToList(),
                UnmatchedUnitKeys = yield.UnmatchedUnitKeys,
            };
            if (structure.YieldSource == ProductionRateSource.Configured)
                dto.Notes.Add("Configured: the lines use the rates staff entered; the areas are never surveyed (G5).");
            else
            {
                if (yield.Areas.Count == 0) dto.Notes.Add("The structure has no area yet: nothing to survey.");
                else if (yield.Areas.All(a => a.LastSurveyId == null)) dto.Notes.Add("No area has been surveyed yet.");
                if (structure.ProductionKindId == null) dto.Notes.Add("The structure has no production kind, so no yield table.");
                if (structure.YieldSource == ProductionRateSource.Derived
                    && await _db.ProductionYieldOverrides.AnyAsync(o => o.ProductionStructureId == structureId, ct))
                    dto.Notes.Add("The structure is Derived: its Hybrid overrides are ignored.");
            }
            foreach (var o in yield.Outputs)
            {
                var line = lines.FirstOrDefault(l => l.ItemBlueprintId == o.ItemBlueprintId);
                o.ProductionLineId = line?.Id;
                o.LineRateSource = line?.RateSource.ToString();
                if (structure.YieldSource == ProductionRateSource.Configured) o.BaseRatePerHour = line?.ConfiguredRatePerHour ?? 0m;
                else if (line?.RateSource == ProductionRateSource.Configured) o.BaseRatePerHour = line.ConfiguredRatePerHour;
                dto.Outputs.Add(o);
            }
            return dto;
        }
    }

    // ===================================================================== Areas

    public interface IProductionAreaService
    {
        Task<IEnumerable<ProductionAreaDto>> GetAllAsync();
        Task<ProductionAreaDto?> GetByIdAsync(int id);
        Task<ProductionAreaDto> CreateAsync(ProductionAreaDto dto);
        Task<ProductionAreaDto> UpdateAsync(int id, ProductionAreaDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProductionAreaListDto>> SearchAsync(PagedQueryDto query);

        /// <summary>Every other area whose bounding box overlaps this one.</summary>
        Task<List<ProductionAreaOverlapDto>> OverlapsAsync(int id);

        /// <summary>The areas the game server should survey now (empty while Production:Enabled is off).</summary>
        Task<List<SurveyDueAreaDto>> SurveyDueAsync(int limit);

        /// <summary>Asks for a survey of the structure's enabled areas (or one of them); 409 for a Configured structure (G5).</summary>
        Task<List<ProductionAreaDto>> RequestSurveyAsync(int structureId, int? areaId);

        /// <summary>Stores a survey the game server ran and materializes the structure's lines.</summary>
        Task<AreaSurveyDto> ReportSurveyAsync(int areaId, AreaSurveyDto dto);
    }

    public sealed class ProductionAreaService : IProductionAreaService
    {
        public const int MaxSurveyDue = 200;
        public static readonly TimeSpan SurveyInterval = TimeSpan.FromHours(24);
        public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromHours(1);

        private readonly KnKDbContext _db;
        private readonly IProductionSettingsReader _settings;
        private readonly IProductionEngine _engine;
        private readonly IProductionYieldService _yield;
        private readonly TimeProvider _clock;
        private readonly ISavedEntityRuleValidator? _rules;

        public ProductionAreaService(KnKDbContext db, IProductionSettingsReader settings, IProductionEngine engine,
            IProductionYieldService yield, TimeProvider? clock = null, ISavedEntityRuleValidator? rules = null)
        {
            _db = db;
            _settings = settings;
            _engine = engine;
            _yield = yield;
            _clock = clock ?? TimeProvider.System;
            _rules = rules;
        }

        /// <summary>
        /// Placement rule P-3 (#883, DESIGN § 3.4): runs the field rules of the default ProductionArea form against the
        /// area about to be saved, as the structure services do (<see cref="IStructurePlacement.CheckRulesAsync"/>). A
        /// failed blocking rule refuses the save (400); failed warnings and rules that could not run are returned.
        /// </summary>
        private async Task<List<string>> CheckPlacementAsync(ProductionArea area)
        {
            var warnings = new List<string>();
            if (_rules == null) return warnings;
            // #900: a Polygon area's points are not a form field; hand them to the rule as extra context.
            var extra = area.Shape == ProductionAreaShape.Polygon && area.PointsJson != null
                ? new Dictionary<string, object?> { [ProductionAreaPlacementValidator.PointsContextKey] = area.PointsJson }
                : null;
            var validation = await _rules.ValidateAsync(nameof(ProductionArea), area, extra);
            foreach (var check in validation.Results.Where(r => !r.Result.IsValid))
            {
                if (check.CouldNotRun)
                {
                    warnings.Add($"{check.FieldLabel}: not checked ({check.Message})");
                    continue;
                }
                if (check.Result.IsBlocking) throw new ArgumentException($"{check.FieldLabel}: {check.Message}");
                warnings.Add($"{check.FieldLabel}: {check.Message}");
            }
            return warnings;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        public static ProductionAreaDto ToDto(ProductionArea a, ProductionStructure? s, ProductionKindLevel? level) => new()
        {
            Id = a.Id,
            ProductionStructureId = a.ProductionStructureId,
            ProductionStructure = s == null ? null : new ProductionStructureNavDto { Id = s.Id, Name = s.Name },
            Name = a.Name,
            Model = a.Model.ToString(),
            Shape = a.Shape.ToString(),
            Exclusive = a.Exclusive,
            WorldName = a.WorldName,
            WgRegionId = a.WgRegionId,
            Points = AreaGeometry.Points(a.PointsJson),
            CenterX = a.CenterX,
            CenterY = a.CenterY,
            CenterZ = a.CenterZ,
            Radius = a.Radius,
            MinX = a.MinX, MinY = a.MinY, MinZ = a.MinZ, MaxX = a.MaxX, MaxY = a.MaxY, MaxZ = a.MaxZ,
            Size = a.Size,
            SizeUnit = AreaGeometry.SizeUnit(a.Model),
            MaxSize = level?.MaxAreaSize,
            Enabled = a.Enabled,
            SurveyRequestedAt = a.SurveyRequestedAt,
            SurveyRequestTrigger = a.SurveyRequestTrigger?.ToString(),
            LastSurveyId = a.LastSurveyId,
            LastSurveyedAt = a.LastSurveyedAt,
            CreatedAt = a.CreatedAt,
        };

        private static ProductionAreaOverlapDto OverlapDto(ProductionArea a, string structureName) => new()
        {
            ProductionAreaId = a.Id, Name = a.Name, ProductionStructureId = a.ProductionStructureId, ProductionStructureName = structureName,
            Model = a.Model.ToString(), Shape = a.Shape.ToString(), Exclusive = a.Exclusive, WgRegionId = a.WgRegionId,
            Points = AreaGeometry.Points(a.PointsJson), CenterX = a.CenterX, CenterY = a.CenterY, CenterZ = a.CenterZ, Radius = a.Radius,
            MinX = a.MinX, MinY = a.MinY, MinZ = a.MinZ, MaxX = a.MaxX, MaxY = a.MaxY, MaxZ = a.MaxZ,
        };

        private async Task<ProductionAreaDto> DtoAsync(ProductionArea a)
        {
            var s = await _db.ProductionStructures.AsNoTracking().FirstAsync(x => x.Id == a.ProductionStructureId);
            return ToDto(a, s, await ProductionLevels.FindAsync(_db, s.ProductionKindId, s.Level));
        }

        public async Task<IEnumerable<ProductionAreaDto>> GetAllAsync()
        {
            var list = new List<ProductionAreaDto>();
            foreach (var a in await _db.ProductionAreas.AsNoTracking().OrderBy(a => a.Id).ToListAsync()) list.Add(await DtoAsync(a));
            return list;
        }

        public async Task<ProductionAreaDto?> GetByIdAsync(int id)
        {
            var a = await _db.ProductionAreas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return a == null ? null : await DtoAsync(a);
        }

        public async Task<ProductionAreaDto> CreateAsync(ProductionAreaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.ProductionStructureId is not > 0) throw new ArgumentException("productionStructureId is required.");
            var structure = await _db.ProductionStructures.AsNoTracking().Include(s => s.ProductionKind).Include(s => s.Location)
                .FirstOrDefaultAsync(s => s.Id == dto.ProductionStructureId)
                ?? throw new ArgumentException($"ProductionStructure with id {dto.ProductionStructureId} not found.");
            var area = new ProductionArea { ProductionStructureId = structure.Id, CreatedAt = Now };
            await ApplyAsync(area, dto, structure, isCreate: true);
            // A Derived or Hybrid structure is surveyed when its area is made (A10); a Configured one never (G5).
            if (structure.YieldSource != ProductionRateSource.Configured)
            {
                area.SurveyRequestedAt = Now;
                area.SurveyRequestTrigger = AreaSurveyTrigger.Creation;
            }
            var warnings = await CheckPlacementAsync(area);
            _db.ProductionAreas.Add(area);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            var created = (await GetByIdAsync(area.Id))!;
            created.PlacementWarnings = warnings;
            return created;
        }

        public async Task<ProductionAreaDto> UpdateAsync(int id, ProductionAreaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var existing = await _db.ProductionAreas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new KeyNotFoundException($"ProductionArea with id {id} not found.");
            if (dto.ProductionStructureId.HasValue && dto.ProductionStructureId != existing.ProductionStructureId)
                throw new ArgumentException("An area can't move to another structure; delete it and create one there.");
            // The time before the change counts with the old area.
            await _engine.SettleStructureAsync(existing.ProductionStructureId);
            _db.ChangeTracker.Clear();
            var area = await _db.ProductionAreas.FirstAsync(a => a.Id == id);
            var structure = await _db.ProductionStructures.AsNoTracking().Include(s => s.ProductionKind).Include(s => s.Location)
                .FirstAsync(s => s.Id == area.ProductionStructureId);
            var before = (area.Shape, area.WorldName, area.WgRegionId, area.PointsJson, area.MinX, area.MinY, area.MinZ, area.MaxX, area.MaxY, area.MaxZ);
            await ApplyAsync(area, dto, structure, isCreate: false);
            var after = (area.Shape, area.WorldName, area.WgRegionId, area.PointsJson, area.MinX, area.MinY, area.MinZ, area.MaxX, area.MaxY, area.MaxZ);
            if (before != after && structure.YieldSource != ProductionRateSource.Configured && area.Enabled)
            {
                area.SurveyRequestedAt = Now; // a new shape holds other contents
                area.SurveyRequestTrigger = AreaSurveyTrigger.Admin;
            }
            var warnings = await CheckPlacementAsync(area);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            var updated = (await GetByIdAsync(id))!;
            updated.PlacementWarnings = warnings;
            return updated;
        }

        private async Task ApplyAsync(ProductionArea area, ProductionAreaDto dto, ProductionStructure structure, bool isCreate)
        {
            var kind = structure.ProductionKind
                ?? throw new ArgumentException("Give the structure a production kind first: the kind sets the area model.");
            if (isCreate || dto.Name != null)
            {
                var name = ProductionInput.Name(dto.Name, ProductionArea.NameMaxLength);
                if (await _db.ProductionAreas.AnyAsync(a => a.Id != area.Id && a.ProductionStructureId == structure.Id && a.Name == name))
                    throw new ArgumentException($"The structure already has an area named '{name}'.");
                area.Name = name;
            }
            area.Model = kind.AreaModel;
            if (isCreate) area.Exclusive = kind.ExclusiveAreas;
            if (dto.Exclusive.HasValue) area.Exclusive = dto.Exclusive.Value;
            if (dto.Enabled.HasValue) area.Enabled = dto.Enabled.Value;
            if (isCreate || dto.WorldName != null)
            {
                var world = string.IsNullOrWhiteSpace(dto.WorldName)
                    ? structure.WorldName ?? structure.Location?.World ?? "world"
                    : dto.WorldName.Trim();
                if (world.Length > ProductionArea.WorldNameMaxLength) throw new ArgumentException("worldName is too long.");
                area.WorldName = world;
            }

            bool shapeSent = dto.Shape != null || dto.WgRegionId != null || dto.Points != null || dto.Radius.HasValue
                || dto.CenterX.HasValue || dto.CenterY.HasValue || dto.CenterZ.HasValue || dto.MinX.HasValue || dto.MinY.HasValue
                || dto.MinZ.HasValue || dto.MaxX.HasValue || dto.MaxY.HasValue || dto.MaxZ.HasValue;
            if (isCreate || shapeSent)
            {
                await FillRegionBoundsAsync(area, dto);
                ApplyShape(area, dto, structure, isCreate);
            }
            area.Size = AreaGeometry.Size(area, AreaGeometry.Points(area.PointsJson));

            // G6, A11: the level's cap, by the model (blocks, volume or radius).
            var level = await ProductionLevels.FindAsync(_db, structure.ProductionKindId, structure.Level);
            if (level?.MaxAreaSize is { } max && area.Size > max)
                throw new ArgumentException($"The area is {area.Size} ({AreaGeometry.SizeUnit(area.Model)}); level {structure.Level} of "
                    + $"{kind.Name} allows at most {max}.");

            // Exclusive areas of the same model may not overlap (§ 3.1), checked from bounding boxes.
            if (area.Exclusive)
            {
                var others = await _db.ProductionAreas.AsNoTracking().Include(a => a.ProductionStructure)
                    .Where(a => a.Id != area.Id && a.Exclusive && a.Model == area.Model && a.WorldName == area.WorldName
                        && a.MinX <= area.MaxX && area.MinX <= a.MaxX && a.MinZ <= area.MaxZ && area.MinZ <= a.MaxZ
                        && a.MinY <= area.MaxY && area.MinY <= a.MaxY)
                    .OrderBy(a => a.Id).FirstOrDefaultAsync();
                if (others != null)
                    throw new InvalidOperationException($"The area overlaps the exclusive area '{others.Name}' of "
                        + $"{others.ProductionStructure.Name}: exclusive {area.Model} areas may not overlap.");
            }
        }

        /// <summary>
        /// #877: a Region-shaped area takes its bounds from the region. When the region id is sent and a bound is left
        /// empty, the bounds come from the region's stored outline (<see cref="MapRegionOutline"/>, reported by the game
        /// server): x and z from its points, y from its height. A bound that is sent wins; without an outline the bounds
        /// stay required as before.
        /// </summary>
        private async Task FillRegionBoundsAsync(ProductionArea area, ProductionAreaDto dto)
        {
            var shape = dto.Shape != null && Enum.TryParse<ProductionAreaShape>(dto.Shape, true, out var sent) ? sent : area.Shape;
            var region = dto.WgRegionId?.Trim();
            if (shape != ProductionAreaShape.Region || string.IsNullOrEmpty(region)) return;
            if (dto.MinX.HasValue && dto.MinY.HasValue && dto.MinZ.HasValue && dto.MaxX.HasValue && dto.MaxY.HasValue && dto.MaxZ.HasValue) return;
            var id = region.ToLowerInvariant();
            var outlines = await _db.MapRegionOutlines.AsNoTracking().Where(o => o.WgRegionId == id).ToListAsync();
            var outline = outlines.FirstOrDefault(o => o.WorldName == area.WorldName) ?? (outlines.Count == 1 ? outlines[0] : null);
            var points = outline == null ? null : knkwebapi_v2.Services.Map.MapOutlinePoints.Parse(outline.PointsJson);
            if (outline == null || points == null || points.Count == 0) return;
            dto.MinX ??= points.Min(p => p.X);
            dto.MaxX ??= points.Max(p => p.X);
            dto.MinZ ??= points.Min(p => p.Z);
            dto.MaxZ ??= points.Max(p => p.Z);
            dto.MinY ??= outline.MinY;
            dto.MaxY ??= outline.MaxY;
        }

        private static void ApplyShape(ProductionArea area, ProductionAreaDto dto, ProductionStructure structure, bool isCreate)
        {
            if (dto.Shape != null) area.Shape = ProductionInput.Parse<ProductionAreaShape>(dto.Shape, "shape");
            else if (isCreate) throw new ArgumentException("shape is required: Region, Cuboid, Polygon or Radius.");
            int? Req(int? value, int? current, string field) =>
                value ?? current ?? throw new ArgumentException($"{field} is required for shape {area.Shape}.");
            static void CheckY(int min, int max)
            {
                if (min > max) throw new ArgumentException("minY must be at most maxY.");
                if (min < AreaGeometry.MinCoordinateY || max > AreaGeometry.MaxCoordinateY)
                    throw new ArgumentException($"y must be {AreaGeometry.MinCoordinateY} to {AreaGeometry.MaxCoordinateY}.");
            }
            bool keep = !isCreate && dto.Shape == null; // a partial edit of the same shape keeps the other values
            switch (area.Shape)
            {
                case ProductionAreaShape.Region:
                case ProductionAreaShape.Cuboid:
                {
                    if (area.Shape == ProductionAreaShape.Region)
                    {
                        var region = dto.WgRegionId?.Trim();
                        if (string.IsNullOrEmpty(region)) region = keep ? area.WgRegionId : null;
                        if (string.IsNullOrEmpty(region)) throw new ArgumentException("wgRegionId is required for shape Region.");
                        if (region.Length > ProductionArea.RegionIdMaxLength) throw new ArgumentException("wgRegionId is too long.");
                        area.WgRegionId = region;
                    }
                    else area.WgRegionId = null;
                    int x1 = Req(dto.MinX, keep ? area.MinX : null, "minX")!.Value, x2 = Req(dto.MaxX, keep ? area.MaxX : null, "maxX")!.Value;
                    int y1 = Req(dto.MinY, keep ? area.MinY : null, "minY")!.Value, y2 = Req(dto.MaxY, keep ? area.MaxY : null, "maxY")!.Value;
                    int z1 = Req(dto.MinZ, keep ? area.MinZ : null, "minZ")!.Value, z2 = Req(dto.MaxZ, keep ? area.MaxZ : null, "maxZ")!.Value;
                    (area.MinX, area.MaxX) = (Math.Min(x1, x2), Math.Max(x1, x2));
                    (area.MinY, area.MaxY) = (Math.Min(y1, y2), Math.Max(y1, y2));
                    (area.MinZ, area.MaxZ) = (Math.Min(z1, z2), Math.Max(z1, z2));
                    CheckY(area.MinY, area.MaxY);
                    area.PointsJson = null;
                    area.CenterX = area.CenterY = area.CenterZ = area.Radius = null;
                    break;
                }
                case ProductionAreaShape.Polygon:
                {
                    var points = dto.Points ?? (keep ? AreaGeometry.Points(area.PointsJson) : null);
                    if (points == null || points.Count < 3) throw new ArgumentException("A polygon needs at least 3 points.");
                    if (points.Count > ProductionArea.MaxPolygonPoints)
                        throw new ArgumentException($"A polygon has at most {ProductionArea.MaxPolygonPoints} points.");
                    int y1 = Req(dto.MinY, keep ? area.MinY : null, "minY")!.Value, y2 = Req(dto.MaxY, keep ? area.MaxY : null, "maxY")!.Value;
                    CheckY(y1, y2);
                    area.PointsJson = JsonSerializer.Serialize(points.Select(p => new AreaPointDto { X = p.X, Z = p.Z }).ToList());
                    if (area.PointsJson.Length > ProductionArea.PointsMaxLength) throw new ArgumentException("The polygon's points are too long.");
                    area.MinX = points.Min(p => p.X);
                    area.MaxX = points.Max(p => p.X);
                    area.MinZ = points.Min(p => p.Z);
                    area.MaxZ = points.Max(p => p.Z);
                    (area.MinY, area.MaxY) = (y1, y2);
                    area.WgRegionId = null;
                    area.CenterX = area.CenterY = area.CenterZ = area.Radius = null;
                    break;
                }
                case ProductionAreaShape.Radius:
                {
                    int radius = Req(dto.Radius, keep ? area.Radius : null, "radius")!.Value;
                    if (radius is < 1 or > ProductionArea.MaxRadius)
                        throw new ArgumentException($"radius must be 1 to {ProductionArea.MaxRadius}.");
                    var loc = structure.Location;
                    int? cx = dto.CenterX ?? (keep ? area.CenterX : null) ?? (loc == null ? null : (int)Math.Floor(loc.X));
                    int? cy = dto.CenterY ?? (keep ? area.CenterY : null) ?? (loc == null ? null : (int)Math.Floor(loc.Y));
                    int? cz = dto.CenterZ ?? (keep ? area.CenterZ : null) ?? (loc == null ? null : (int)Math.Floor(loc.Z));
                    if (cx == null || cz == null)
                        throw new ArgumentException("centerX and centerZ are required: the structure has no Location to centre the radius on.");
                    int y1 = dto.MinY ?? (keep ? area.MinY : ProductionArea.WorldMinY);
                    int y2 = dto.MaxY ?? (keep ? area.MaxY : ProductionArea.WorldMaxY);
                    CheckY(y1, y2);
                    area.Radius = radius;
                    (area.CenterX, area.CenterY, area.CenterZ) = (cx, cy, cz);
                    (area.MinX, area.MaxX, area.MinZ, area.MaxZ) = (cx.Value - radius, cx.Value + radius, cz.Value - radius, cz.Value + radius);
                    (area.MinY, area.MaxY) = (y1, y2);
                    area.WgRegionId = null;
                    area.PointsJson = null;
                    break;
                }
            }
        }

        public async Task DeleteAsync(int id)
        {
            var area = await _db.ProductionAreas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new KeyNotFoundException($"ProductionArea with id {id} not found.");
            await _engine.SettleStructureAsync(area.ProductionStructureId);
            _db.ChangeTracker.Clear();
            // Its surveys and their units go with it (Cascade); loaded so the delete is the same without a database cascade.
            var tracked = await _db.ProductionAreas.FirstAsync(a => a.Id == id);
            var surveys = await _db.AreaSurveys.Include(s => s.Units).Where(s => s.ProductionAreaId == id).ToListAsync();
            _db.AreaSurveys.RemoveRange(surveys);
            _db.ProductionAreas.Remove(tracked);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        public async Task<PagedResultDto<ProductionAreaListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ProductionArea> q = _db.ProductionAreas.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(a => a.Name.ToLower().Contains(term) || a.ProductionStructure.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "productionStructureId") is { } sid) q = q.Where(a => a.ProductionStructureId == sid);
            if (ProductionInput.Filter(query, "model") is { } m && Enum.TryParse<ProductionAreaModel>(m, true, out var model))
                q = q.Where(a => a.Model == model);
            if (ProductionInput.Flag(query, "exclusive") is { } ex) q = q.Where(a => a.Exclusive == ex);
            if (ProductionInput.Flag(query, "enabled") is { } en) q = q.Where(a => a.Enabled == en);
            q = (query.SortBy ?? "name").ToLowerInvariant() switch
            {
                "id" => ProductionInput.Sort(q, query.SortDescending, a => a.Id),
                "lastsurveyedat" => ProductionInput.Sort(q, query.SortDescending, a => a.LastSurveyedAt),
                _ => ProductionInput.Sort(q, query.SortDescending, a => a.Name),
            };
            return await TerritoryPaging.PageAsync(q.Select(a => new ProductionAreaListDto
            {
                Id = a.Id, Name = a.Name, ProductionStructureId = a.ProductionStructureId, ProductionStructureName = a.ProductionStructure.Name,
                Model = a.Model.ToString(), Shape = a.Shape.ToString(), Exclusive = a.Exclusive, Size = a.Size, Enabled = a.Enabled,
                LastSurveyedAt = a.LastSurveyedAt,
            }), query);
        }

        public async Task<List<ProductionAreaOverlapDto>> OverlapsAsync(int id)
        {
            var area = await _db.ProductionAreas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new KeyNotFoundException($"ProductionArea with id {id} not found.");
            return await OverlapsOfAsync(area, sharedOnly: false);
        }

        private async Task<List<ProductionAreaOverlapDto>> OverlapsOfAsync(ProductionArea area, bool sharedOnly)
        {
            var q = _db.ProductionAreas.AsNoTracking().Include(a => a.ProductionStructure).ThenInclude(s => s.ProductionKind)
                .Where(a => a.Id != area.Id && a.WorldName == area.WorldName
                    && a.MinX <= area.MaxX && area.MinX <= a.MaxX && a.MinZ <= area.MaxZ && area.MinZ <= a.MaxZ
                    && a.MinY <= area.MaxY && area.MinY <= a.MaxY);
            if (sharedOnly) q = q.Where(a => !a.Exclusive && a.Enabled && a.ProductionStructureId != area.ProductionStructureId);
            var rows = await q.OrderBy(a => a.Id).ToListAsync();
            return rows.Select(a =>
            {
                var dto = OverlapDto(a, a.ProductionStructure.Name);
                dto.UnitTypes = UnitTypes(a.ProductionStructure.ProductionKind?.SurveyProfileJson);
                return dto;
            }).ToList();
        }

        /// <summary>The <c>unitTypes</c> of a kind's survey profile (#875); null when missing or unreadable.</summary>
        public static List<string>? UnitTypes(string? surveyProfileJson)
        {
            if (string.IsNullOrWhiteSpace(surveyProfileJson)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(surveyProfileJson);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("unitTypes", out var types)
                    || types.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
                return types.EnumerateArray().Where(t => t.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(t => t.GetString()!).ToList();
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        public async Task<List<SurveyDueAreaDto>> SurveyDueAsync(int limit)
        {
            var result = new List<SurveyDueAreaDto>();
            if (!(await _settings.GetAsync()).Enabled) return result; // A21: no surveys while the feature is off
            limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, MaxSurveyDue);
            var now = Now;
            var staleBefore = now - SurveyInterval;
            var rows = await _db.ProductionAreas.AsNoTracking().Include(a => a.ProductionStructure).ThenInclude(s => s.ProductionKind)
                .Where(a => a.Enabled && a.ProductionStructure.YieldSource != ProductionRateSource.Configured
                    && a.ProductionStructure.ProductionKindId != null
                    && (a.SurveyRequestedAt != null || a.LastSurveyedAt == null || a.LastSurveyedAt <= staleBefore))
                .OrderBy(a => a.SurveyRequestedAt == null).ThenBy(a => a.SurveyRequestedAt).ThenBy(a => a.LastSurveyedAt).ThenBy(a => a.Id)
                .Take(limit).ToListAsync();
            foreach (var a in rows)
            {
                var s = a.ProductionStructure;
                var level = await ProductionLevels.FindAsync(_db, s.ProductionKindId, s.Level);
                result.Add(new SurveyDueAreaDto
                {
                    Area = ToDto(a, s, level),
                    ProductionKindId = s.ProductionKindId,
                    ProductionKindName = s.ProductionKind?.Name,
                    SurveyProfileJson = s.ProductionKind?.SurveyProfileJson,
                    SurveyProfileVersion = s.ProductionKind?.SurveyProfileVersion ?? 0,
                    Trigger = (a.SurveyRequestedAt != null ? a.SurveyRequestTrigger ?? AreaSurveyTrigger.Admin
                        : a.LastSurveyedAt == null ? AreaSurveyTrigger.Creation : AreaSurveyTrigger.Scheduled).ToString(),
                    OverlappingSharedAreas = a.Exclusive ? new() : await OverlapsOfAsync(a, sharedOnly: true),
                });
            }
            return result;
        }

        public async Task<List<ProductionAreaDto>> RequestSurveyAsync(int structureId, int? areaId)
        {
            var structure = await _db.ProductionStructures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == structureId)
                ?? throw new KeyNotFoundException($"ProductionStructure with id {structureId} not found.");
            if (structure.YieldSource == ProductionRateSource.Configured)
                throw new InvalidOperationException("The structure is Configured: it is never surveyed (G5). Switch it to Derived or Hybrid first.");
            var q = _db.ProductionAreas.Where(a => a.ProductionStructureId == structureId && a.Enabled);
            if (areaId.HasValue) q = q.Where(a => a.Id == areaId.Value);
            var areas = await q.OrderBy(a => a.Id).ToListAsync();
            if (areaId.HasValue && areas.Count == 0)
                throw new KeyNotFoundException($"The structure has no enabled area with id {areaId}.");
            if (areas.Count == 0) throw new InvalidOperationException("The structure has no enabled area to survey.");
            foreach (var a in areas)
            {
                a.SurveyRequestedAt = Now;
                a.SurveyRequestTrigger = AreaSurveyTrigger.Admin;
            }
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            var list = new List<ProductionAreaDto>();
            foreach (var a in areas) list.Add((await GetByIdAsync(a.Id))!);
            return list;
        }

        public async Task<AreaSurveyDto> ReportSurveyAsync(int areaId, AreaSurveyDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (!(await _settings.GetAsync()).Enabled)
                throw new InvalidOperationException("Production is turned off (Production:Enabled): surveys are not taken.");
            var area = await _db.ProductionAreas.AsNoTracking().Include(a => a.ProductionStructure).ThenInclude(s => s.ProductionKind)
                .FirstOrDefaultAsync(a => a.Id == areaId)
                ?? throw new KeyNotFoundException($"ProductionArea with id {areaId} not found.");
            var structure = area.ProductionStructure;
            if (structure.YieldSource == ProductionRateSource.Configured)
                throw new InvalidOperationException("The structure is Configured: it is never surveyed (G5).");

            var now = Now;
            var surveyedAt = dto.SurveyedAt?.ToUniversalTime() ?? now;
            if (surveyedAt > now + MaxFutureSkew) throw new ArgumentException("surveyedAt is in the future.");
            var trigger = dto.Trigger != null ? ProductionInput.Parse<AreaSurveyTrigger>(dto.Trigger, "trigger")
                : area.SurveyRequestTrigger ?? AreaSurveyTrigger.Scheduled;
            int version = dto.ProfileVersion ?? structure.ProductionKind?.SurveyProfileVersion ?? 0;
            if (version < 0) throw new ArgumentException("profileVersion can't be negative.");
            var hash = ProductionInput.Text(dto.ContentHash, AreaSurvey.ContentHashMaxLength, "contentHash");
            var note = ProductionInput.Text(dto.Note, AreaSurvey.NoteMaxLength, "note");
            var units = new List<AreaSurveyUnit>();
            foreach (var u in dto.Units ?? new List<AreaSurveyUnitDto>())
            {
                var key = SurveyUnitKeys.NormalizeKey(u.UnitKey, "unitKey");
                if (u.Count < 0) throw new ArgumentException($"{key}: count can't be negative.");
                int sharedBy = u.SharedBy ?? 1;
                if (sharedBy is < 1 or > AreaSurveyUnit.MaxSharedBy)
                    throw new ArgumentException($"{key}: sharedBy must be 1 to {AreaSurveyUnit.MaxSharedBy}.");
                if (units.Any(x => x.UnitKey == key && x.SharedBy == sharedBy))
                    throw new ArgumentException($"{key} is listed twice with sharedBy {sharedBy}.");
                units.Add(new AreaSurveyUnit
                {
                    UnitKey = key, Count = u.Count, SharedBy = sharedBy,
                    Extra = ProductionInput.Text(u.Extra, AreaSurveyUnit.ExtraMaxLength, $"{key} extra"),
                });
            }
            if (units.Count > AreaSurvey.MaxUnits) throw new ArgumentException($"A survey has at most {AreaSurvey.MaxUnits} unit rows.");

            // The time before the survey counts at the old contents.
            await _engine.SettleStructureAsync(structure.Id);
            _db.ChangeTracker.Clear();

            var survey = new AreaSurvey
            {
                ProductionAreaId = areaId, ProductionStructureId = structure.Id, SurveyedAt = surveyedAt, ReceivedAt = now,
                ProfileVersion = version, Trigger = trigger, ContentHash = hash, Note = note,
                TotalUnits = units.Sum(u => u.Count), Units = units,
            };
            _db.AreaSurveys.Add(survey);
            await _db.SaveChangesAsync();
            var tracked = await _db.ProductionAreas.FirstAsync(a => a.Id == areaId);
            // An older survey that arrives late is kept in the history but doesn't replace a newer one.
            if (tracked.LastSurveyedAt == null || surveyedAt >= tracked.LastSurveyedAt)
            {
                tracked.LastSurveyId = survey.Id;
                tracked.LastSurveyedAt = surveyedAt;
                // P8a (#685): the area's resource pool takes its capacity from the newest survey (PRODUCTION_KINDS § 6.3).
                var pool = await _db.Set<ResourcePool>().FirstOrDefaultAsync(p => p.ProductionAreaId == areaId);
                if (pool != null) ResourcePoolService.ApplySurvey(pool, survey.TotalUnits, survey.Id, now);
            }
            if (tracked.SurveyRequestedAt != null && tracked.SurveyRequestedAt <= now)
            {
                tracked.SurveyRequestedAt = null;
                tracked.SurveyRequestTrigger = null;
            }
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();

            var result = await AreaSurveyService.ToDtoAsync(_db, survey.Id);
            result!.Materialization = await _yield.MaterializeAsync(structure.Id);
            return result;
        }
    }

    // ===================================================================== Survey history

    public interface IAreaSurveyService
    {
        Task<AreaSurveyDto?> GetByIdAsync(long id);
        Task<PagedResultDto<AreaSurveyListDto>> SearchAsync(PagedQueryDto query);
        Task<AreaSurveyTrendDto> TrendAsync(int areaId, int take);
    }

    public sealed class AreaSurveyService : IAreaSurveyService
    {
        public const int MaxTrend = 100;
        private readonly KnKDbContext _db;

        public AreaSurveyService(KnKDbContext db) => _db = db;

        public static async Task<AreaSurveyDto?> ToDtoAsync(KnKDbContext db, long id)
        {
            var s = await db.AreaSurveys.AsNoTracking().Include(x => x.Units).Include(x => x.ProductionArea).FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return null;
            return new AreaSurveyDto
            {
                Id = s.Id, ProductionAreaId = s.ProductionAreaId,
                ProductionArea = new NamedRefDto { Id = s.ProductionAreaId, Name = s.ProductionArea.Name },
                ProductionStructureId = s.ProductionStructureId, SurveyedAt = s.SurveyedAt, ReceivedAt = s.ReceivedAt,
                ProfileVersion = s.ProfileVersion, Trigger = s.Trigger.ToString(), ContentHash = s.ContentHash, TotalUnits = s.TotalUnits,
                Note = s.Note,
                Units = s.Units.OrderByDescending(u => u.Count).ThenBy(u => u.UnitKey).Select(u => new AreaSurveyUnitDto
                {
                    Id = u.Id, UnitKey = u.UnitKey, Count = u.Count, SharedBy = u.SharedBy, Extra = u.Extra,
                }).ToList(),
            };
        }

        public Task<AreaSurveyDto?> GetByIdAsync(long id) => ToDtoAsync(_db, id);

        private static IQueryable<AreaSurveyListDto> List(IQueryable<AreaSurvey> q) => q.Select(s => new AreaSurveyListDto
        {
            Id = s.Id, Name = s.ProductionArea.Name + " survey " + s.Id, ProductionAreaId = s.ProductionAreaId,
            ProductionAreaName = s.ProductionArea.Name, ProductionStructureId = s.ProductionStructureId, SurveyedAt = s.SurveyedAt,
            Trigger = s.Trigger.ToString(), ProfileVersion = s.ProfileVersion, ContentHash = s.ContentHash, TotalUnits = s.TotalUnits,
        });

        public async Task<PagedResultDto<AreaSurveyListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<AreaSurvey> q = _db.AreaSurveys.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(s => s.ProductionArea.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "productionAreaId") is { } aid) q = q.Where(s => s.ProductionAreaId == aid);
            if (TerritoryPaging.IntFilter(query, "productionStructureId") is { } sid) q = q.Where(s => s.ProductionStructureId == sid);
            if (ProductionInput.Filter(query, "trigger") is { } t && Enum.TryParse<AreaSurveyTrigger>(t, true, out var trigger))
                q = q.Where(s => s.Trigger == trigger);
            q = (query.SortBy ?? "surveyedat").ToLowerInvariant() switch
            {
                "id" => ProductionInput.Sort(q, query.SortDescending, s => s.Id),
                _ => query.SortBy == null ? q.OrderByDescending(s => s.SurveyedAt).ThenByDescending(s => s.Id)
                    : ProductionInput.Sort(q, query.SortDescending, s => s.SurveyedAt),
            };
            return await TerritoryPaging.PageAsync(List(q), query);
        }

        public async Task<AreaSurveyTrendDto> TrendAsync(int areaId, int take)
        {
            if (!await _db.ProductionAreas.AnyAsync(a => a.Id == areaId)) throw new KeyNotFoundException($"ProductionArea with id {areaId} not found.");
            take = Math.Clamp(take <= 0 ? 30 : take, 1, MaxTrend);
            var latest = await List(_db.AreaSurveys.AsNoTracking().Where(s => s.ProductionAreaId == areaId)
                .OrderByDescending(s => s.SurveyedAt).ThenByDescending(s => s.Id).Take(take)).ToListAsync();
            latest.Reverse();
            var ids = latest.Select(s => s.Id).ToList();
            var units = await _db.AreaSurveyUnits.AsNoTracking().Where(u => ids.Contains(u.AreaSurveyId))
                .Select(u => new { u.AreaSurveyId, u.UnitKey, u.Count }).ToListAsync();
            var dto = new AreaSurveyTrendDto { ProductionAreaId = areaId, Surveys = latest };
            foreach (var key in units.Select(u => u.UnitKey).Distinct().OrderBy(k => k, StringComparer.Ordinal))
                dto.Series[key] = ids.Select(id => units.Where(u => u.AreaSurveyId == id && u.UnitKey == key).Sum(u => u.Count)).ToList();
            return dto;
        }
    }

    // ===================================================================== Hybrid overrides

    public interface IProductionYieldOverrideService
    {
        Task<IEnumerable<ProductionYieldOverrideDto>> GetAllAsync();
        Task<ProductionYieldOverrideDto?> GetByIdAsync(int id);
        Task<ProductionYieldOverrideDto> CreateAsync(ProductionYieldOverrideDto dto);
        Task UpdateAsync(int id, ProductionYieldOverrideDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProductionYieldOverrideListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>Hybrid pins, exclusions and scales (PRODUCTION_KINDS § 4.1). A change settles the structure first and
    /// materializes its lines after (a pin makes its line at once).</summary>
    public sealed class ProductionYieldOverrideService : IProductionYieldOverrideService
    {
        private readonly KnKDbContext _db;
        private readonly IProductionEngine _engine;
        private readonly IProductionYieldService _yield;

        public ProductionYieldOverrideService(KnKDbContext db, IProductionEngine engine, IProductionYieldService yield)
        {
            _db = db;
            _engine = engine;
            _yield = yield;
        }

        private IQueryable<ProductionYieldOverride> Query() =>
            _db.ProductionYieldOverrides.AsNoTracking().Include(o => o.ProductionStructure).Include(o => o.ItemBlueprint);

        private static ProductionYieldOverrideDto ToDto(ProductionYieldOverride o) => new()
        {
            Id = o.Id, ProductionStructureId = o.ProductionStructureId,
            ProductionStructure = o.ProductionStructure == null ? null : new ProductionStructureNavDto { Id = o.ProductionStructureId, Name = o.ProductionStructure.Name },
            ItemBlueprintId = o.ItemBlueprintId,
            ItemBlueprint = o.ItemBlueprint == null ? null : new NamedRefDto { Id = o.ItemBlueprintId, Name = o.ItemBlueprint.Name },
            Kind = o.Kind.ToString(), Value = o.Value, Note = o.Note, CreatedAt = o.CreatedAt,
        };

        public async Task<IEnumerable<ProductionYieldOverrideDto>> GetAllAsync() =>
            (await Query().OrderBy(o => o.Id).ToListAsync()).Select(ToDto).ToList();

        public async Task<ProductionYieldOverrideDto?> GetByIdAsync(int id)
        {
            var o = await Query().FirstOrDefaultAsync(x => x.Id == id);
            return o == null ? null : ToDto(o);
        }

        public async Task<ProductionYieldOverrideDto> CreateAsync(ProductionYieldOverrideDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.ProductionStructureId is not > 0) throw new ArgumentException("productionStructureId is required.");
            if (dto.ItemBlueprintId is not > 0) throw new ArgumentException("itemBlueprintId is required.");
            if (!await _db.ProductionStructures.AnyAsync(s => s.Id == dto.ProductionStructureId))
                throw new ArgumentException($"ProductionStructure with id {dto.ProductionStructureId} not found.");
            var o = new ProductionYieldOverride { ProductionStructureId = dto.ProductionStructureId.Value, CreatedAt = DateTime.UtcNow };
            await ApplyAsync(o, dto, isCreate: true);
            await _engine.SettleStructureAsync(o.ProductionStructureId);
            _db.ChangeTracker.Clear();
            _db.ProductionYieldOverrides.Add(o);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            await _yield.MaterializeAsync(o.ProductionStructureId);
            return (await GetByIdAsync(o.Id))!;
        }

        public async Task UpdateAsync(int id, ProductionYieldOverrideDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var existing = await _db.ProductionYieldOverrides.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id)
                ?? throw new KeyNotFoundException($"ProductionYieldOverride with id {id} not found.");
            if (dto.ProductionStructureId.HasValue && dto.ProductionStructureId != existing.ProductionStructureId)
                throw new ArgumentException("An override can't move to another structure.");
            await _engine.SettleStructureAsync(existing.ProductionStructureId);
            _db.ChangeTracker.Clear();
            var o = await _db.ProductionYieldOverrides.FirstAsync(x => x.Id == id);
            await ApplyAsync(o, dto, isCreate: false);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            await _yield.MaterializeAsync(o.ProductionStructureId);
        }

        private async Task ApplyAsync(ProductionYieldOverride o, ProductionYieldOverrideDto dto, bool isCreate)
        {
            if (isCreate || (dto.ItemBlueprintId.HasValue && dto.ItemBlueprintId != o.ItemBlueprintId))
            {
                int bp = dto.ItemBlueprintId!.Value;
                if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == bp)) throw new ArgumentException($"ItemBlueprint with id {bp} not found.");
                if (await _db.ProductionYieldOverrides.AnyAsync(x => x.Id != o.Id && x.ProductionStructureId == o.ProductionStructureId && x.ItemBlueprintId == bp))
                    throw new ArgumentException("The structure already has an override for this blueprint.");
                o.ItemBlueprintId = bp;
            }
            if (dto.Kind != null) o.Kind = ProductionInput.Parse<ProductionYieldOverrideKind>(dto.Kind, "kind");
            else if (isCreate) throw new ArgumentException("kind is required: Pin, Exclude or Scale.");
            if (dto.Value.HasValue) o.Value = Math.Round(dto.Value.Value, ProductionRate.Decimals);
            else if (isCreate && o.Kind != ProductionYieldOverrideKind.Exclude) throw new ArgumentException($"value is required for a {o.Kind}.");
            switch (o.Kind)
            {
                case ProductionYieldOverrideKind.Pin when o.Value <= 0 || o.Value > ProductionLine.MaxRatePerHour:
                    throw new ArgumentException($"A pin's value is its base rate per hour: above 0 and at most {ProductionLine.MaxRatePerHour}.");
                case ProductionYieldOverrideKind.Scale when o.Value < 0 || o.Value > ProductionYieldOverride.MaxScale:
                    throw new ArgumentException($"A scale's value is a factor from 0 to {ProductionYieldOverride.MaxScale}.");
                case ProductionYieldOverrideKind.Exclude:
                    o.Value = 0m;
                    break;
            }
            if (dto.Note != null) o.Note = ProductionInput.Text(dto.Note, ProductionYieldOverride.NoteMaxLength, "note");
        }

        public async Task DeleteAsync(int id)
        {
            var o = await _db.ProductionYieldOverrides.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"ProductionYieldOverride with id {id} not found.");
            await _engine.SettleStructureAsync(o.ProductionStructureId);
            _db.ChangeTracker.Clear();
            _db.ProductionYieldOverrides.Remove(await _db.ProductionYieldOverrides.FirstAsync(x => x.Id == id));
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            await _yield.MaterializeAsync(o.ProductionStructureId);
        }

        public async Task<PagedResultDto<ProductionYieldOverrideListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ProductionYieldOverride> q = _db.ProductionYieldOverrides.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(o => o.ItemBlueprint.Name.ToLower().Contains(term) || o.ProductionStructure.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "productionStructureId") is { } sid) q = q.Where(o => o.ProductionStructureId == sid);
            if (TerritoryPaging.IntFilter(query, "itemBlueprintId") is { } bid) q = q.Where(o => o.ItemBlueprintId == bid);
            if (ProductionInput.Filter(query, "kind") is { } k && Enum.TryParse<ProductionYieldOverrideKind>(k, true, out var kind))
                q = q.Where(o => o.Kind == kind);
            q = (query.SortBy ?? "id").ToLowerInvariant() switch
            {
                "name" => ProductionInput.Sort(q, query.SortDescending, o => o.ItemBlueprint.Name),
                _ => ProductionInput.Sort(q, query.SortDescending, o => o.Id),
            };
            return await TerritoryPaging.PageAsync(q.Select(o => new ProductionYieldOverrideListDto
            {
                Id = o.Id, Name = o.ProductionStructure.Name + ": " + o.Kind.ToString() + " " + o.ItemBlueprint.Name,
                ProductionStructureId = o.ProductionStructureId, ProductionStructureName = o.ProductionStructure.Name,
                ItemBlueprintId = o.ItemBlueprintId, ItemBlueprintName = o.ItemBlueprint.Name, Kind = o.Kind.ToString(), Value = o.Value,
            }), query);
        }
    }
}

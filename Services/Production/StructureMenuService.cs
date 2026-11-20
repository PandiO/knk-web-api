using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Stock;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    public interface IStructureMenuService
    {
        /// <summary>The structures overview (M2): production structures and warehouses, public fields only. Filters:
        /// <c>townId</c>, <c>productionKindId</c>, <c>structureType</c> (ProductionStructure or Warehouse),
        /// <c>status</c> (Working, Stalled, NoRoad, Full); the search term matches the name.</summary>
        Task<PagedResultDto<StructureMenuRowDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);

        /// <summary>The structure menu for a viewer (M3): the detail only for members of the town and staff.</summary>
        Task<StructureMenuDto?> GetAsync(int structureId, ProductionViewer viewer, CancellationToken ct = default);

        /// <summary>The structure's transport orders, newest first (members and staff only; null when not found).</summary>
        Task<PagedResultDto<TransportOrderListDto>?> OrdersAsync(int structureId, ProductionViewer viewer, PagedQueryDto query,
            CancellationToken ct = default);
    }

    /// <summary>
    /// The in-game structure menus' reads (P6a, PandiO/knk-workspace#683, production DESIGN § 11.2, decision record § 15.1
    /// M1-M5). Every player sees every production structure and warehouse with its name, kind, town, level, products and a
    /// status; members of the structure's town and staff (<c>knk.admin.world</c>) also get rates, storage contents,
    /// workers and the transport orders. The filter is applied here, so the game server never receives stock a player
    /// may not see (A25). Reads are always on (G16); the public rows read the stored line states and never settle.
    /// </summary>
    public sealed class StructureMenuService : IStructureMenuService
    {
        public const string ProductionStructureType = "ProductionStructure";
        public const string WarehouseType = "Warehouse";
        public const int MaxStorageLines = 54;

        private readonly KnKDbContext _db;
        private readonly IProductionViewerResolver _viewers;
        private readonly IProductionEngine _engine;
        private readonly ITransportService _transport;

        public StructureMenuService(KnKDbContext db, IProductionViewerResolver viewers, IProductionEngine engine, ITransportService transport)
        {
            _db = db;
            _viewers = viewers;
            _engine = engine;
            _transport = transport;
        }

        public async Task<PagedResultDto<StructureMenuRowDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            query ??= new PagedQueryDto();
            var rows = await RowsAsync(null, ct);
            var term = string.IsNullOrWhiteSpace(query.SearchTerm) ? null : query.SearchTerm.Trim();
            IEnumerable<StructureMenuRowDto> q = rows;
            if (term != null)
                q = q.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (r.KindName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || r.Products.Any(p => p.Contains(term, StringComparison.OrdinalIgnoreCase)));
            if (IntFilter(query, "townId") is { } townId) q = q.Where(r => r.TownId == townId);
            if (IntFilter(query, "productionKindId") is { } kindId) q = q.Where(r => r.ProductionKindId == kindId);
            if (Filter(query, "structureType") is { } type) q = q.Where(r => r.StructureType.Equals(type, StringComparison.OrdinalIgnoreCase));
            if (Filter(query, "status") is { } status) q = q.Where(r => r.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            IOrderedEnumerable<StructureMenuRowDto> sorted = (query.SortBy?.ToLowerInvariant()) switch
            {
                "town" => query.SortDescending ? q.OrderByDescending(r => r.TownName) : q.OrderBy(r => r.TownName),
                "kind" => query.SortDescending ? q.OrderByDescending(r => r.KindName) : q.OrderBy(r => r.KindName),
                "status" => query.SortDescending ? q.OrderByDescending(r => r.Status) : q.OrderBy(r => r.Status),
                _ => query.SortDescending ? q.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    : q.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            };
            var list = sorted.ThenBy(r => r.Id).ToList();
            int size = Math.Clamp(query.PageSize, 1, 200);
            int number = Math.Max(1, query.PageNumber);
            return new PagedResultDto<StructureMenuRowDto>
            {
                Items = list.Skip((number - 1) * size).Take(size).ToList(),
                TotalCount = list.Count,
                PageNumber = number,
                PageSize = size,
            };
        }

        public async Task<StructureMenuDto?> GetAsync(int structureId, ProductionViewer viewer, CancellationToken ct = default)
        {
            var row = (await RowsAsync(structureId, ct)).FirstOrDefault();
            if (row == null) return null;
            var dto = new StructureMenuDto
            {
                Id = row.Id, StructureType = row.StructureType, Name = row.Name, ProductionKindId = row.ProductionKindId,
                KindName = row.KindName, TownId = row.TownId, TownName = row.TownName, IsRural = row.IsRural, Level = row.Level,
                Products = row.Products, Status = row.Status, StatusText = row.StatusText,
                ViewerIsStaff = viewer.IsStaff,
                ViewerSeesDetails = await _viewers.SeesDetailsAsync(viewer, row.TownId, ct),
            };
            if (!dto.ViewerSeesDetails) return dto;

            var detail = new StructureMenuDetailDto();
            if (row.StructureType == ProductionStructureType)
            {
                var overview = await _engine.OverviewAsync(structureId, ct);
                if (overview != null)
                {
                    detail.WorkersPresent = overview.WorkersPresent;
                    detail.WorkersTotal = overview.WorkersTotal;
                    detail.NotProducingReasons = overview.NotProducingReasons.ToList();
                    detail.Rates = overview.Lines.Select(l => new StructureMenuRateDto
                    {
                        ProductionLineId = l.Line.Id,
                        ItemBlueprintId = l.Line.ItemBlueprintId ?? 0,
                        ItemBlueprintName = l.Line.ItemBlueprint?.Name,
                        RatePerHour = l.EffectiveRatePerHour,
                        Status = l.Line.Status ?? "",
                        NotProducingReasons = l.NotProducingReasons.ToList(),
                    }).ToList();
                }
                var transport = await _transport.StructureViewAsync(structureId, ct);
                if (transport != null)
                {
                    detail.WorkersAway = transport.WorkersAway;
                    detail.Warnings.AddRange(transport.Warnings);
                    detail.OpenOrderCount = transport.OpenOrders.Count;
                }
            }
            else
            {
                detail.OpenOrderCount = await _db.TransportOrders.AsNoTracking()
                    .CountAsync(o => o.NextActionAt != null && o.Stops.Any(s => s.StructureId == structureId), ct);
                if (row.Status == StructureMenuStatus.Full) detail.Warnings.Add(row.StatusText);
                if (row.Status == StructureMenuStatus.Stalled) detail.Warnings.Add(row.StatusText);
            }
            detail.Storages = await StoragesAsync(structureId, ct);
            dto.Detail = detail;
            return dto;
        }

        public async Task<PagedResultDto<TransportOrderListDto>?> OrdersAsync(int structureId, ProductionViewer viewer, PagedQueryDto query,
            CancellationToken ct = default)
        {
            var row = (await RowsAsync(structureId, ct)).FirstOrDefault();
            if (row == null) return null;
            if (!await _viewers.SeesDetailsAsync(viewer, row.TownId, ct))
                throw new StorageAccessDeniedException("Only members of the structure's town and staff see its transport orders.");
            query ??= new PagedQueryDto();
            var filters = new Dictionary<string, string>(query.Filters ?? new Dictionary<string, string>()) { ["structureId"] = structureId.ToString() };
            return await _transport.SearchAsync(new PagedQueryDto
            {
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                SortDescending = true,
                Filters = filters,
            }, ct);
        }

        // ================= Helpers =================

        private async Task<List<StructureMenuRowDto>> RowsAsync(int? onlyId, CancellationToken ct)
        {
            var producers = _db.ProductionStructures.AsNoTracking();
            var warehouses = _db.Warehouses.AsNoTracking();
            if (onlyId is { } id)
            {
                producers = producers.Where(p => p.Id == id);
                warehouses = warehouses.Where(w => w.Id == id);
            }
            var p = await producers.Select(x => new
            {
                x.Id, x.Name, x.ProductionKindId, KindName = x.ProductionKind != null ? x.ProductionKind.Name : null,
                TownId = x.District != null ? (int?)x.District.TownId : x.TownId,
                TownName = x.District != null ? x.District.Town.Name : x.Town != null ? x.Town.Name : null,
                IsRural = x.DistrictId == null, x.Level, x.ProductionEnabled, x.RoadConnection, x.WorkersPresent,
                Lines = x.Lines.Select(l => new { l.Enabled, l.Status, Name = l.ItemBlueprint.Name }).ToList(),
            }).ToListAsync(ct);
            var w = await warehouses.Select(x => new
            {
                x.Id, x.Name, x.AcceptsDeliveries,
                TownId = x.District != null ? (int?)x.District.TownId : x.TownId,
                TownName = x.District != null ? x.District.Town.Name : x.Town != null ? x.Town.Name : null,
                IsRural = x.DistrictId == null,
                Total = x.DefaultStorage != null ? (long?)x.DefaultStorage.TotalUnits : null,
                Max = x.DefaultStorage != null ? x.DefaultStorage.MaxUnits : null,
            }).ToListAsync(ct);

            var rows = new List<StructureMenuRowDto>(p.Count + w.Count);
            foreach (var x in p)
            {
                var (status, text) = ProducerStatus(x.ProductionEnabled, x.IsRural, x.RoadConnection, x.WorkersPresent,
                    x.Lines.Select(l => (l.Enabled, l.Status)).ToList());
                rows.Add(new StructureMenuRowDto
                {
                    Id = x.Id, StructureType = ProductionStructureType, Name = x.Name, ProductionKindId = x.ProductionKindId,
                    KindName = x.KindName, TownId = x.TownId, TownName = x.TownName, IsRural = x.IsRural, Level = x.Level,
                    Products = x.Lines.Select(l => l.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                    Status = status, StatusText = text,
                });
            }
            foreach (var x in w)
            {
                var (status, text) = x.Max is { } max && x.Total >= max
                    ? (StructureMenuStatus.Full, $"Its delivery storage is full ({x.Total} of {max} units).")
                    : !x.AcceptsDeliveries
                        ? (StructureMenuStatus.Stalled, "It takes no deliveries.")
                        : (StructureMenuStatus.Working, "Taking deliveries.");
                rows.Add(new StructureMenuRowDto
                {
                    Id = x.Id, StructureType = WarehouseType, Name = x.Name, KindName = WarehouseType, TownId = x.TownId,
                    TownName = x.TownName, IsRural = x.IsRural, Status = status, StatusText = text,
                });
            }
            return rows;
        }

        /// <summary>The public status of a production structure (M3), from stored state only: Full when a line is blocked
        /// full; NoRoad for a rural structure not connected to its town's roads (D5); Stalled when it is switched off, has
        /// no running line or no worker present; else Working.</summary>
        public static (string Status, string Text) ProducerStatus(bool productionEnabled, bool isRural, RoadConnectionState road,
            int workersPresent, IReadOnlyList<(bool Enabled, ProductionLineStatus Status)> lines)
        {
            if (lines.Any(l => l.Enabled && l.Status == ProductionLineStatus.BlockedFull))
                return (StructureMenuStatus.Full, "Its storage is full: production stops until goods are carried away.");
            if (isRural && road != RoadConnectionState.Connected)
                return (StructureMenuStatus.NoRoad, "Not connected to its town's roads: goods are not carried away.");
            if (!productionEnabled) return (StructureMenuStatus.Stalled, "Production is switched off.");
            if (!lines.Any(l => l.Enabled && l.Status == ProductionLineStatus.Running))
                return (StructureMenuStatus.Stalled, lines.Count == 0 ? "It produces nothing yet." : "No production line is running.");
            if (workersPresent <= 0) return (StructureMenuStatus.Stalled, "All workers are away.");
            return (StructureMenuStatus.Working, "Working.");
        }

        private async Task<List<StructureMenuStorageDto>> StoragesAsync(int structureId, CancellationToken ct)
        {
            var storages = await _db.Storages.AsNoTracking()
                .Where(s => s.Kind == StorageKind.Domain && s.DomainId == structureId).OrderBy(s => s.Id)
                .Select(s => new StructureMenuStorageDto
                {
                    StorageId = s.Id, Name = s.Name, Purpose = s.Purpose.ToString(), TotalUnits = s.TotalUnits, MaxUnits = s.MaxUnits,
                }).ToListAsync(ct);
            foreach (var s in storages)
                s.Lines = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == s.StorageId)
                    .OrderByDescending(l => l.Amount).ThenBy(l => l.ItemBlueprintId).Take(MaxStorageLines)
                    .Select(l => new TerminalEntryDto { ItemBlueprintId = l.ItemBlueprintId, ItemBlueprintName = l.ItemBlueprint.Name, Amount = l.Amount })
                    .ToListAsync(ct);
            return storages;
        }

        private static string? Filter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        private static int? IntFilter(PagedQueryDto q, string name) => int.TryParse(Filter(q, name), out var i) ? i : null;
    }
}

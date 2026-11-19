using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    /// <summary>The game server's report was refused because <c>Production:Enabled</c> is off (G16).</summary>
    public sealed class ProductionDisabledException : InvalidOperationException
    {
        public ProductionDisabledException()
            : base("Production:Enabled is off: the territory report is not processed. Turn it on in the game settings (production).")
        {
        }
    }

    public interface ITerritoryService
    {
        /// <summary>Everything the game server's chunk cache is built from (territory DESIGN § 1).</summary>
        Task<TerritoryInputsDto> GetInputsAsync();

        /// <summary>Processes the game server's report for one world (overlaps and structure flags).</summary>
        Task<TerritoryReportResultDto> ApplyReportAsync(TerritoryReportDto report);

        Task<TownTerritoryDto?> GetTownTerritoryAsync(int townId);

        Task<StructureTerritoryDto?> GetStructureTerritoryAsync(int structureId);

        Task<StructureTerritoryDto> SetLoyaltyAsync(int structureId, StructureLoyaltyUpdateDto dto);

        Task<StructureTerritoryDto> ClearFlagAsync(int structureId);
    }

    /// <summary>
    /// Territory V1, the API side (territory DESIGN § 1-4, PandiO/knk-workspace#326). The territory itself is computed by
    /// the game server from its chunk cache; the API holds the inputs (holders, regions, power, settings, awards) and the
    /// results the server reports (conflicts, structure flags). The contract:
    /// <list type="number">
    ///   <item>The server reads <see cref="GetInputsAsync"/> at start and again after a <c>TerritoryChanged</c> realtime
    ///     event (sent when any input changes); <c>version</c> lets it skip an unchanged rebuild.</item>
    ///   <item>After a rebuild it sends one report per world (<see cref="ApplyReportAsync"/>), a full snapshot.</item>
    /// </list>
    /// The report is the only background work and runs only with <c>Production:Enabled</c> on (G16).
    /// </summary>
    public sealed class TerritoryService : ITerritoryService
    {
        /// <summary>The most chunks one overlap may list; a larger one is skipped (logged in the result).</summary>
        public const int MaxChunksPerOverlap = 100_000;

        private readonly KnKDbContext _db;
        private readonly IProductionSettingsReader _settings;
        private readonly ILogger<TerritoryService> _logger;

        public TerritoryService(KnKDbContext db, IProductionSettingsReader settings, ILogger<TerritoryService> logger)
        {
            _db = db;
            _settings = settings;
            _logger = logger;
        }

        public async Task<TerritoryInputsDto> GetInputsAsync()
        {
            var production = await _settings.GetAsync();
            var s = production.Territory;
            var towns = await _db.Towns.AsNoTracking().OrderBy(t => t.Id)
                .Select(t => new { t.Id, t.Name, t.KingdomId, t.ProvinceId, t.Power }).ToListAsync();
            var powerOf = towns.ToDictionary(t => t.Id, t => t.Power);
            var districts = await _db.Districts.AsNoTracking().OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.Name, d.WorldName, d.WgRegionId, d.TownId, d.IsCapital }).ToListAsync();
            var crown = await _db.CrownLands.AsNoTracking().OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.Name, c.WorldName, c.WgRegionId, c.KingdomId, c.Range }).ToListAsync();
            var domainIds = districts.Select(d => d.Id).Concat(crown.Select(c => c.Id)).ToList();
            var subRegions = (await _db.DomainSubRegions.AsNoTracking().Where(r => domainIds.Contains(r.DomainId))
                    .OrderBy(r => r.Id).Select(r => new { r.DomainId, r.WgRegionId }).ToListAsync())
                .GroupBy(r => r.DomainId).ToDictionary(g => g.Key, g => g.Select(r => r.WgRegionId).ToList());
            var awards = await _db.TerritoryConflicts.AsNoTracking()
                .Where(c => c.Status == TerritoryConflictStatus.Resolved && c.Resolution == TerritoryConflictResolution.Awarded
                    && c.AwardedHolderKind != null && c.AwardedHolderId != null)
                .OrderBy(c => c.Id).ToListAsync();

            var dto = new TerritoryInputsDto
            {
                ProductionEnabled = production.Enabled,
                Settings = s,
                Kingdoms = await _db.Kingdoms.AsNoTracking().OrderBy(k => k.Id)
                    .Select(k => new TerritoryInputKingdomDto { Id = k.Id, Name = k.Name }).ToListAsync(),
                Towns = towns.Select(t => new TerritoryInputTownDto
                {
                    Id = t.Id, Name = t.Name, KingdomId = t.KingdomId, ProvinceId = t.ProvinceId, Power = t.Power,
                    Range = TerritoryRules.TownRange(s, t.Power),
                }).ToList(),
                Districts = districts.Select(d => new TerritoryInputRegionDto
                {
                    Id = d.Id, Name = d.Name, World = d.WorldName, WgRegionId = d.WgRegionId,
                    SubRegionIds = subRegions.GetValueOrDefault(d.Id) ?? new List<string>(),
                    Holder = new TerritoryHolderDto { Kind = nameof(TerritoryHolderKind.Town), Id = d.TownId },
                    IsCapital = d.IsCapital,
                    Rank = TerritoryRules.Rank(TerritoryHolderKind.Town),
                    Range = TerritoryRules.DistrictRange(s, powerOf.GetValueOrDefault(d.TownId), d.IsCapital),
                }).ToList(),
                CrownLands = crown.Select(c => new TerritoryInputRegionDto
                {
                    Id = c.Id, Name = c.Name, World = c.WorldName, WgRegionId = c.WgRegionId,
                    SubRegionIds = subRegions.GetValueOrDefault(c.Id) ?? new List<string>(),
                    Holder = new TerritoryHolderDto { Kind = nameof(TerritoryHolderKind.Kingdom), Id = c.KingdomId },
                    Rank = TerritoryRules.Rank(TerritoryHolderKind.Kingdom),
                    Range = c.Range,
                }).ToList(),
                Awards = awards.Select(a => new TerritoryInputAwardDto
                {
                    ConflictId = a.Id,
                    World = a.World,
                    Holder = new TerritoryHolderDto { Kind = a.AwardedHolderKind!.Value.ToString(), Id = a.AwardedHolderId!.Value },
                    Chunks = JsonColumn.DeserializeList<int[]>(a.ChunksJson),
                }).ToList(),
            };
            dto.Version = VersionOf(dto);
            dto.GeneratedAt = DateTime.UtcNow;
            return dto;
        }

        /// <summary>A hash over the inputs (without <c>version</c> and <c>generatedAt</c>).</summary>
        public static string VersionOf(TerritoryInputsDto dto)
        {
            var copy = new
            {
                dto.ProductionEnabled, dto.Settings, dto.Kingdoms, dto.Towns, dto.Districts, dto.CrownLands, dto.Awards,
            };
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(copy)));
            return Convert.ToHexString(bytes, 0, 12).ToLowerInvariant();
        }

        public async Task<TerritoryReportResultDto> ApplyReportAsync(TerritoryReportDto report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var world = report.World?.Trim();
            if (string.IsNullOrEmpty(world) || world.Length > TerritoryConflict.WorldMaxLength)
                throw new ArgumentException("world is required (at most 100 characters).", nameof(report));
            if (!(await _settings.GetAsync()).Enabled) throw new ProductionDisabledException();

            var result = new TerritoryReportResultDto { World = world };
            var now = DateTime.UtcNow;
            var townIds = (await _db.Towns.Select(t => t.Id).ToListAsync()).ToHashSet();
            var kingdomIds = (await _db.Kingdoms.Select(k => k.Id).ToListAsync()).ToHashSet();
            bool Exists(TerritoryHolder h) => h.Kind == TerritoryHolderKind.Town ? townIds.Contains(h.Id) : kingdomIds.Contains(h.Id);

            var open = await _db.TerritoryConflicts
                .Where(c => c.World == world && c.Status == TerritoryConflictStatus.Open).ToListAsync();
            var seen = new HashSet<int>();
            var byPair = new Dictionary<(TerritoryHolder, TerritoryHolder), TerritoryConflict>();

            foreach (var overlap in report.Overlaps ?? new List<TerritoryReportOverlapDto>())
            {
                TerritoryHolder a, b;
                try
                {
                    (a, b) = TerritoryRules.Normalize(TerritoryRules.Parse(overlap?.HolderA, "holderA"),
                        TerritoryRules.Parse(overlap?.HolderB, "holderB"));
                }
                catch (ArgumentException ex)
                {
                    result.Skipped.Add($"overlap: {ex.Message}");
                    continue;
                }
                if (!TerritoryRules.IsConflictPair(a, b))
                {
                    result.Skipped.Add($"overlap {a} / {b}: not a conflict (same holder or unequal rank; crown land outranks towns)");
                    continue;
                }
                if (!Exists(a) || !Exists(b))
                {
                    result.Skipped.Add($"overlap {a} / {b}: unknown holder");
                    continue;
                }
                List<int[]> chunks;
                int minX, minZ, maxX, maxZ;
                try
                {
                    (chunks, minX, minZ, maxX, maxZ) = TerritoryRules.NormalizeChunks(overlap!.Chunks);
                }
                catch (ArgumentException ex)
                {
                    result.Skipped.Add($"overlap {a} / {b}: {ex.Message}");
                    continue;
                }
                if (chunks.Count == 0 || chunks.Count > MaxChunksPerOverlap)
                {
                    result.Skipped.Add($"overlap {a} / {b}: {chunks.Count} chunks (1 to {MaxChunksPerOverlap} allowed)");
                    continue;
                }

                var conflict = byPair.GetValueOrDefault((a, b)) ?? open.FirstOrDefault(c =>
                    c.HolderAKind == a.Kind && c.HolderAId == a.Id && c.HolderBKind == b.Kind && c.HolderBId == b.Id);
                if (conflict == null)
                {
                    conflict = new TerritoryConflict
                    {
                        World = world, HolderAKind = a.Kind, HolderAId = a.Id, HolderBKind = b.Kind, HolderBId = b.Id,
                        DetectedAt = now,
                    };
                    _db.TerritoryConflicts.Add(conflict);
                    open.Add(conflict);
                    result.ConflictsCreated++;
                }
                else if (byPair.ContainsKey((a, b)))
                {
                    // The same pair twice in one report: merge the areas.
                    (chunks, minX, minZ, maxX, maxZ) = TerritoryRules.NormalizeChunks(
                        JsonColumn.DeserializeList<int[]>(conflict.ChunksJson).Concat(chunks));
                }
                else
                {
                    result.ConflictsUpdated++;
                }
                byPair[(a, b)] = conflict;
                conflict.ChunksJson = JsonColumn.Serialize(chunks);
                conflict.ChunkCount = chunks.Count;
                (conflict.MinChunkX, conflict.MinChunkZ, conflict.MaxChunkX, conflict.MaxChunkZ) = (minX, minZ, maxX, maxZ);
                conflict.LastReportedAt = now;
                conflict.StillOverlapping = true;
                if (conflict.Id > 0) seen.Add(conflict.Id);
            }

            foreach (var c in open.Where(c => c.Id > 0 && !seen.Contains(c.Id) && c.StillOverlapping))
            {
                c.StillOverlapping = false;
                result.ConflictsNoLongerOverlapping++;
            }
            await _db.SaveChangesAsync();

            await ApplyStructureFlagsAsync(world, report.Structures ?? new List<TerritoryReportStructureDto>(), byPair, open, now, result);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Territory report for {World} (inputs {Version}): {Created} conflicts created, {Updated} updated, {Gone} no longer overlapping, {Flagged} structures flagged, {Cleared} cleared, {Skipped} entries skipped",
                world, report.InputsVersion ?? "?", result.ConflictsCreated, result.ConflictsUpdated,
                result.ConflictsNoLongerOverlapping, result.StructuresFlagged, result.StructuresCleared, result.Skipped.Count);
            return result;
        }

        private async Task ApplyStructureFlagsAsync(string world, List<TerritoryReportStructureDto> entries,
            Dictionary<(TerritoryHolder, TerritoryHolder), TerritoryConflict> byPair, List<TerritoryConflict> open,
            DateTime now, TerritoryReportResultDto result)
        {
            var ids = entries.Where(e => e != null).Select(e => e.StructureId).Distinct().ToList();
            var flaggedNow = await _db.Structures.Where(s => s.WorldName == world
                && (s.TerritoryFlag != StructureTerritoryFlag.None || ids.Contains(s.Id))).ToListAsync();
            var byId = flaggedNow.ToDictionary(s => s.Id);
            var reported = new HashSet<int>();

            foreach (var e in entries.Where(e => e != null))
            {
                if (!byId.TryGetValue(e.StructureId, out var structure))
                {
                    result.Skipped.Add($"structure {e.StructureId}: not found in world {world}");
                    continue;
                }
                if (!Enum.TryParse<StructureTerritoryFlag>(e.Flag?.Trim(), true, out var flag)
                    || flag == StructureTerritoryFlag.None || !Enum.IsDefined(flag))
                {
                    result.Skipped.Add($"structure {e.StructureId}: flag must be OutsideTerritory or Contested");
                    continue;
                }
                int? conflictId = null;
                if (flag == StructureTerritoryFlag.Contested && e.HolderA != null && e.HolderB != null)
                {
                    try
                    {
                        var pair = TerritoryRules.Normalize(TerritoryRules.Parse(e.HolderA, "holderA"), TerritoryRules.Parse(e.HolderB, "holderB"));
                        var conflict = byPair.GetValueOrDefault(pair) ?? open.FirstOrDefault(c =>
                            c.HolderAKind == pair.A.Kind && c.HolderAId == pair.A.Id && c.HolderBKind == pair.B.Kind && c.HolderBId == pair.B.Id);
                        conflictId = conflict?.Id;
                    }
                    catch (ArgumentException)
                    {
                        // The flag still holds without a conflict link.
                    }
                }
                reported.Add(structure.Id);
                if (structure.TerritoryFlag != flag || structure.TerritoryConflictId != conflictId)
                {
                    if (structure.TerritoryFlag != flag) structure.TerritoryFlaggedAt = now;
                    structure.TerritoryFlag = flag;
                    structure.TerritoryConflictId = conflictId;
                    result.StructuresFlagged++;
                }
            }

            foreach (var s in flaggedNow.Where(s => s.TerritoryFlag != StructureTerritoryFlag.None && !reported.Contains(s.Id)))
            {
                s.TerritoryFlag = StructureTerritoryFlag.None;
                s.TerritoryFlaggedAt = null;
                s.TerritoryConflictId = null;
                result.StructuresCleared++;
            }
        }

        public async Task<TownTerritoryDto?> GetTownTerritoryAsync(int townId)
        {
            var town = await _db.Towns.AsNoTracking().Where(t => t.Id == townId)
                .Select(t => new { t.Id, t.Power, t.KingdomId, t.ProvinceId }).FirstOrDefaultAsync();
            if (town == null) return null;
            var s = (await _settings.GetAsync()).Territory;
            var districts = await _db.Districts.AsNoTracking().Where(d => d.TownId == townId).OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.Name, d.WorldName, d.WgRegionId, d.IsCapital }).ToListAsync();
            return new TownTerritoryDto
            {
                TownId = town.Id,
                Power = town.Power,
                KingdomId = town.KingdomId,
                ProvinceId = town.ProvinceId,
                Range = TerritoryRules.DistrictRange(s, town.Power, false),
                CapitalRange = TerritoryRules.DistrictRange(s, town.Power, true),
                Districts = districts.Select(d => new TerritoryInputRegionDto
                {
                    Id = d.Id, Name = d.Name, World = d.WorldName, WgRegionId = d.WgRegionId, IsCapital = d.IsCapital,
                    Holder = new TerritoryHolderDto { Kind = nameof(TerritoryHolderKind.Town), Id = town.Id },
                    Rank = TerritoryRules.Rank(TerritoryHolderKind.Town),
                    Range = TerritoryRules.DistrictRange(s, town.Power, d.IsCapital),
                }).ToList(),
            };
        }

        public async Task<StructureTerritoryDto?> GetStructureTerritoryAsync(int structureId)
        {
            var s = await _db.Structures.AsNoTracking().Include(x => x.District).Include(x => x.LoyaltyTown)
                .FirstOrDefaultAsync(x => x.Id == structureId);
            return s == null ? null : ToDto(s);
        }

        public async Task<StructureTerritoryDto> SetLoyaltyAsync(int structureId, StructureLoyaltyUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var s = await _db.Structures.FirstOrDefaultAsync(x => x.Id == structureId)
                ?? throw new KeyNotFoundException($"Structure with id {structureId} not found.");
            if (dto.LoyaltyTownId.HasValue && !await _db.Towns.AnyAsync(t => t.Id == dto.LoyaltyTownId.Value))
                throw new ArgumentException($"Town with id {dto.LoyaltyTownId} not found.", nameof(dto));
            s.LoyaltyTownId = dto.LoyaltyTownId;
            await _db.SaveChangesAsync();
            return (await GetStructureTerritoryAsync(structureId))!;
        }

        public async Task<StructureTerritoryDto> ClearFlagAsync(int structureId)
        {
            var s = await _db.Structures.FirstOrDefaultAsync(x => x.Id == structureId)
                ?? throw new KeyNotFoundException($"Structure with id {structureId} not found.");
            s.TerritoryFlag = StructureTerritoryFlag.None;
            s.TerritoryFlaggedAt = null;
            s.TerritoryConflictId = null;
            await _db.SaveChangesAsync();
            return (await GetStructureTerritoryAsync(structureId))!;
        }

        private static StructureTerritoryDto ToDto(Structure s) => new()
        {
            StructureId = s.Id,
            StructureName = s.Name,
            TownId = s.District?.TownId ?? s.TownId, // effective town (P2, #91)
            LoyaltyTownId = s.LoyaltyTownId,
            LoyaltyTownName = s.LoyaltyTown?.Name,
            TerritoryFlag = s.TerritoryFlag.ToString(),
            TerritoryFlaggedAt = s.TerritoryFlaggedAt,
            TerritoryConflictId = s.TerritoryConflictId,
        };
    }
}

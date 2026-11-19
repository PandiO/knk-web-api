using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    public interface ITerritoryConflictService
    {
        Task<TerritoryConflictDto?> GetByIdAsync(int id);
        Task<IEnumerable<TerritoryConflictDto>> GetAllAsync(bool openOnly);
        Task<PagedResultDto<TerritoryConflictListDto>> SearchAsync(PagedQueryDto query);
        Task<TerritoryConflictDto> ResolveAsync(int id, TerritoryConflictResolveDto dto, int? userId);
        Task<TerritoryConflictDto> ReopenAsync(int id);
        Task DeleteAsync(int id);
    }

    /// <summary>
    /// Territory conflicts (territory DESIGN § 3): written by the game server's report, resolved by admins only. An
    /// award makes the area the winner's in the game server's cache (it is listed in the territory inputs); "redrawn"
    /// records that the admin changed the inputs so the lines no longer overlap. Reopen undoes a resolution.
    /// </summary>
    public sealed class TerritoryConflictService : ITerritoryConflictService
    {
        private readonly KnKDbContext _db;

        public TerritoryConflictService(KnKDbContext db) => _db = db;

        public async Task<TerritoryConflictDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var c = await _db.TerritoryConflicts.AsNoTracking().Include(x => x.FlaggedStructures)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return null;
            var names = await HolderNames.LoadAsync(_db, new[] { c });
            return ToDto(c, names);
        }

        public async Task<IEnumerable<TerritoryConflictDto>> GetAllAsync(bool openOnly)
        {
            var q = _db.TerritoryConflicts.AsNoTracking().Include(x => x.FlaggedStructures).AsQueryable();
            if (openOnly) q = q.Where(c => c.Status == TerritoryConflictStatus.Open);
            var rows = await q.OrderByDescending(c => c.DetectedAt).ToListAsync();
            var names = await HolderNames.LoadAsync(_db, rows);
            return rows.Select(c => ToDto(c, names)).ToList();
        }

        public async Task<PagedResultDto<TerritoryConflictListDto>> SearchAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var q = _db.TerritoryConflicts.AsNoTracking();
            if (query.Filters != null && query.Filters.TryGetValue("status", out var status)
                && Enum.TryParse<TerritoryConflictStatus>(status, true, out var parsed))
            {
                q = q.Where(c => c.Status == parsed);
            }
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(c => c.World.ToLower().Contains(term));
            q = query.SortDescending ? q.OrderByDescending(c => c.Id) : q.OrderBy(c => c.Id);
            var page = await TerritoryPaging.PageAsync(q.Select(c => new
            {
                Conflict = c,
                Flagged = c.FlaggedStructures.Count,
            }), query);
            var names = await HolderNames.LoadAsync(_db, page.Items.Select(i => i.Conflict).ToList());
            return new PagedResultDto<TerritoryConflictListDto>
            {
                TotalCount = page.TotalCount,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                Items = page.Items.Select(i => new TerritoryConflictListDto
                {
                    Id = i.Conflict.Id,
                    Name = $"{names.Name(i.Conflict.HolderAKind, i.Conflict.HolderAId)} vs {names.Name(i.Conflict.HolderBKind, i.Conflict.HolderBId)}",
                    World = i.Conflict.World,
                    Status = i.Conflict.Status.ToString(),
                    StillOverlapping = i.Conflict.StillOverlapping,
                    ChunkCount = i.Conflict.ChunkCount,
                    Resolution = i.Conflict.Resolution.ToString(),
                    DetectedAt = i.Conflict.DetectedAt,
                    FlaggedStructureCount = i.Flagged,
                }).ToList(),
            };
        }

        public async Task<TerritoryConflictDto> ResolveAsync(int id, TerritoryConflictResolveDto dto, int? userId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var c = await _db.TerritoryConflicts.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Territory conflict with id {id} not found.");
            if (c.Status == TerritoryConflictStatus.Resolved)
                throw new InvalidOperationException("This conflict is already resolved; reopen it first.");
            if (!Enum.TryParse<TerritoryConflictResolution>(dto.Resolution?.Trim(), true, out var resolution)
                || resolution == TerritoryConflictResolution.None || !Enum.IsDefined(resolution))
                throw new ArgumentException("Resolution must be Awarded or Redrawn.", nameof(dto));
            if ((dto.Note?.Length ?? 0) > TerritoryConflict.NoteMaxLength)
                throw new ArgumentException($"Note is at most {TerritoryConflict.NoteMaxLength} characters.", nameof(dto));

            c.AwardedHolderKind = null;
            c.AwardedHolderId = null;
            if (resolution == TerritoryConflictResolution.Awarded)
            {
                var winner = TerritoryRules.Parse(dto.AwardedHolder, "awardedHolder");
                var a = new TerritoryHolder(c.HolderAKind, c.HolderAId);
                var b = new TerritoryHolder(c.HolderBKind, c.HolderBId);
                if (winner != a && winner != b)
                    throw new ArgumentException("The area can only be awarded to one of the conflict's two holders.", nameof(dto));
                c.AwardedHolderKind = winner.Kind;
                c.AwardedHolderId = winner.Id;
            }
            c.Status = TerritoryConflictStatus.Resolved;
            c.Resolution = resolution;
            c.ResolvedAt = DateTime.UtcNow;
            c.ResolvedByUserId = userId;
            c.ResolutionNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(id))!;
        }

        public async Task<TerritoryConflictDto> ReopenAsync(int id)
        {
            var c = await _db.TerritoryConflicts.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Territory conflict with id {id} not found.");
            if (c.Status == TerritoryConflictStatus.Open) throw new InvalidOperationException("This conflict is already open.");
            bool anotherOpen = await _db.TerritoryConflicts.AnyAsync(x => x.Id != id && x.World == c.World
                && x.Status == TerritoryConflictStatus.Open && x.HolderAKind == c.HolderAKind && x.HolderAId == c.HolderAId
                && x.HolderBKind == c.HolderBKind && x.HolderBId == c.HolderBId);
            if (anotherOpen)
                throw new InvalidOperationException("Another open conflict exists for the same holders in this world; resolve or delete that one first.");
            c.Status = TerritoryConflictStatus.Open;
            c.Resolution = TerritoryConflictResolution.None;
            c.AwardedHolderKind = null;
            c.AwardedHolderId = null;
            c.ResolvedAt = null;
            c.ResolvedByUserId = null;
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(id))!;
        }

        public async Task DeleteAsync(int id)
        {
            var c = await _db.TerritoryConflicts.Include(x => x.FlaggedStructures).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Territory conflict with id {id} not found.");
            foreach (var s in c.FlaggedStructures) s.TerritoryConflictId = null;
            _db.TerritoryConflicts.Remove(c);
            await _db.SaveChangesAsync();
        }

        internal static TerritoryConflictDto ToDto(TerritoryConflict c, HolderNames names) => new()
        {
            Id = c.Id,
            World = c.World,
            HolderA = names.Dto(c.HolderAKind, c.HolderAId),
            HolderB = names.Dto(c.HolderBKind, c.HolderBId),
            Status = c.Status.ToString(),
            StillOverlapping = c.StillOverlapping,
            Chunks = JsonColumn.DeserializeList<int[]>(c.ChunksJson),
            ChunkCount = c.ChunkCount,
            MinChunkX = c.MinChunkX,
            MinChunkZ = c.MinChunkZ,
            MaxChunkX = c.MaxChunkX,
            MaxChunkZ = c.MaxChunkZ,
            DetectedAt = c.DetectedAt,
            LastReportedAt = c.LastReportedAt,
            Resolution = c.Resolution.ToString(),
            AwardedHolder = c.AwardedHolderKind is { } k && c.AwardedHolderId is { } aid ? names.Dto(k, aid) : null,
            ResolvedAt = c.ResolvedAt,
            ResolvedByUserId = c.ResolvedByUserId,
            ResolutionNote = c.ResolutionNote,
            FlaggedStructures = c.FlaggedStructures.OrderBy(s => s.Name)
                .Select(s => new TerritoryNavDto { Id = s.Id, Name = s.Name }).ToList(),
        };
    }

    /// <summary>Names of the towns and kingdoms some conflicts refer to, loaded in two queries.</summary>
    internal sealed class HolderNames
    {
        private readonly Dictionary<int, string> _towns;
        private readonly Dictionary<int, string> _kingdoms;

        private HolderNames(Dictionary<int, string> towns, Dictionary<int, string> kingdoms)
        {
            _towns = towns;
            _kingdoms = kingdoms;
        }

        public static async Task<HolderNames> LoadAsync(KnKDbContext db, IReadOnlyCollection<TerritoryConflict> conflicts)
        {
            var holders = conflicts.SelectMany(c => new[]
            {
                (c.HolderAKind, c.HolderAId), (c.HolderBKind, c.HolderBId),
                (c.AwardedHolderKind ?? TerritoryHolderKind.Town, c.AwardedHolderId ?? 0),
            }).ToList();
            var townIds = holders.Where(h => h.Item1 == TerritoryHolderKind.Town && h.Item2 > 0).Select(h => h.Item2).Distinct().ToList();
            var kingdomIds = holders.Where(h => h.Item1 == TerritoryHolderKind.Kingdom).Select(h => h.Item2).Distinct().ToList();
            var towns = townIds.Count == 0 ? new Dictionary<int, string>()
                : await db.Towns.AsNoTracking().Where(t => townIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);
            var kingdoms = kingdomIds.Count == 0 ? new Dictionary<int, string>()
                : await db.Kingdoms.AsNoTracking().Where(k => kingdomIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, k => k.Name);
            return new HolderNames(towns, kingdoms);
        }

        public string? NameOrNull(TerritoryHolderKind kind, int id) =>
            (kind == TerritoryHolderKind.Town ? _towns : _kingdoms).TryGetValue(id, out var n) ? n : null;

        public string Name(TerritoryHolderKind kind, int id) => NameOrNull(kind, id) ?? $"{kind} {id} (deleted)";

        public TerritoryHolderDto Dto(TerritoryHolderKind kind, int id) =>
            new() { Kind = kind.ToString(), Id = id, Name = NameOrNull(kind, id) };
    }
}

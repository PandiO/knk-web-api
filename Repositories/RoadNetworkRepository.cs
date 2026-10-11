using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Roads;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories;

/// <summary>EF Core implementation of <see cref="IRoadNetworkRepository"/>.</summary>
public class RoadNetworkRepository : IRoadNetworkRepository
{
    private readonly KnKDbContext _context;

    public RoadNetworkRepository(KnKDbContext context)
    {
        _context = context;
    }

    // Tiles

    public Task<RoadTile?> GetTileAsync(string world, int tileX, int tileZ) =>
        _context.RoadTiles.FirstOrDefaultAsync(t => t.World == world && t.TileX == tileX && t.TileZ == tileZ);

    public Task<RoadTile?> GetTileByIdAsync(int id) =>
        _context.RoadTiles.FirstOrDefaultAsync(t => t.Id == id);

    public Task<List<RoadTile>> GetTilesByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0
            ? Task.FromResult(new List<RoadTile>())
            : _context.RoadTiles.Where(t => list.Contains(t.Id)).ToListAsync();
    }

    public Task<List<RoadTile>> ListTilesAsync(string world) =>
        _context.RoadTiles.Where(t => t.World == world).OrderBy(t => t.TileX).ThenBy(t => t.TileZ).ToListAsync();

    public Task<RoadTileProposal?> GetProposalAsync(int tileId) =>
        _context.RoadTileProposals.FirstOrDefaultAsync(p => p.TileId == tileId);

    public Task<List<RoadTileProposal>> ListProposalsAsync(string world) =>
        _context.RoadTileProposals.Include(p => p.Tile)
            .Where(p => p.Tile.World == world)
            .OrderBy(p => p.Tile.TileX).ThenBy(p => p.Tile.TileZ)
            .ToListAsync();

    public async Task<RoadTile> AddTileAsync(RoadTile tile)
    {
        _context.RoadTiles.Add(tile);
        await _context.SaveChangesAsync();
        return tile;
    }

    public async Task LockTileAsync(int tileId)
    {
        if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction == null)
        {
            return;
        }
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM road_tiles WHERE Id = {tileId} FOR UPDATE");
    }

    // Nodes

    public Task<RoadNode?> GetNodeAsync(int id) =>
        _context.RoadNodes.FirstOrDefaultAsync(n => n.Id == id);

    public Task<List<RoadNode>> GetNodesByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0
            ? Task.FromResult(new List<RoadNode>())
            : _context.RoadNodes.Where(n => list.Contains(n.Id)).ToListAsync();
    }

    public Task<List<RoadNode>> GetTileNodesAsync(int tileId) =>
        _context.RoadNodes.Where(n => n.TileId == tileId).OrderBy(n => n.Id).ToListAsync();

    public Task<List<RoadNode>> GetWorldNodesAsync(string world) =>
        _context.RoadNodes.Where(n => n.World == world).OrderBy(n => n.Id).ToListAsync();

    public Task<List<RoadNode>> GetNodesInBoxAsync(string world, int minX, int minZ, int maxX, int maxZ) =>
        _context.RoadNodes
            .Where(n => n.World == world && n.X >= minX && n.X <= maxX && n.Z >= minZ && n.Z <= maxZ)
            .OrderBy(n => n.Id)
            .ToListAsync();

    public Task<RoadNode?> GetNodeAtAsync(string world, int x, int y, int z) =>
        _context.RoadNodes.FirstOrDefaultAsync(n => n.World == world && n.X == x && n.Y == y && n.Z == z);

    // Edges

    public Task<RoadEdge?> GetEdgeAsync(int id) =>
        _context.RoadEdges.FirstOrDefaultAsync(e => e.Id == id);

    public Task<List<RoadEdge>> GetTileEdgesAsync(int tileId) =>
        _context.RoadEdges.Where(e => e.TileId == tileId).OrderBy(e => e.Id).ToListAsync();

    public Task<List<RoadEdge>> GetEdgesTouchingNodesAsync(IEnumerable<int> nodeIds)
    {
        var list = nodeIds.Distinct().ToList();
        return list.Count == 0
            ? Task.FromResult(new List<RoadEdge>())
            : _context.RoadEdges.Where(e => list.Contains(e.FromNodeId) || list.Contains(e.ToNodeId)).OrderBy(e => e.Id).ToListAsync();
    }

    public Task<List<RoadEdge>> GetWorldEdgesAsync(string world) =>
        _context.RoadEdges.Where(e => e.World == world).OrderBy(e => e.Id).ToListAsync();

    public Task<List<RoadEdge>> GetEdgesInBoxAsync(string world, int minX, int minZ, int maxX, int maxZ) =>
        _context.RoadEdges
            .Where(e => e.World == world && e.MinX <= maxX && e.MaxX >= minX && e.MinZ <= maxZ && e.MaxZ >= minZ)
            .OrderBy(e => e.Id)
            .ToListAsync();

    public Task<List<RoadEdge>> GetEdgesByStreetAsync(int streetId) =>
        _context.RoadEdges.Where(e => e.StreetId == streetId).OrderBy(e => e.Id).ToListAsync();

    public Task<List<RoadEdge>> GetEdgesByProfileAsync(int profileId) =>
        _context.RoadEdges.Where(e => e.ProfileId == profileId).OrderBy(e => e.Id).ToListAsync();

    public Task<List<RoadSurvey>> GetSurveysByProfileAsync(int profileId) =>
        _context.RoadSurveys.Where(s => s.ProfileId == profileId).OrderBy(s => s.Id).ToListAsync();

    public async Task<PagedResult<RoadEdge>> SearchEdgesAsync(PagedQuery query)
    {
        var queryable = _context.RoadEdges.AsQueryable();
        var filters = query.Filters ?? new Dictionary<string, string>();

        if (filters.TryGetValue("world", out var world) && !string.IsNullOrWhiteSpace(world))
        {
            queryable = queryable.Where(e => e.World == world);
        }
        if (filters.TryGetValue("tileId", out var tileText) && int.TryParse(tileText, out var tileId))
        {
            queryable = queryable.Where(e => e.TileId == tileId);
        }
        if (filters.TryGetValue("streetId", out var streetText) && int.TryParse(streetText, out var streetId))
        {
            queryable = queryable.Where(e => e.StreetId == streetId);
        }
        if (filters.TryGetValue("unlabelled", out var unlabelled) && bool.TryParse(unlabelled, out var onlyUnlabelled) && onlyUnlabelled)
        {
            queryable = queryable.Where(e => e.StreetId == null);
        }
        if (filters.TryGetValue("stale", out var stale) && bool.TryParse(stale, out var onlyStale) && onlyStale)
        {
            queryable = queryable.Where(e => e.Status == RoadEdgeStatus.Stale);
        }

        queryable = (query.SortBy ?? "").ToLowerInvariant() switch
        {
            "length" => query.SortDescending ? queryable.OrderByDescending(e => e.Length) : queryable.OrderBy(e => e.Length),
            "streetid" => query.SortDescending ? queryable.OrderByDescending(e => e.StreetId) : queryable.OrderBy(e => e.StreetId),
            "tileid" => query.SortDescending ? queryable.OrderByDescending(e => e.TileId) : queryable.OrderBy(e => e.TileId),
            _ => query.SortDescending ? queryable.OrderByDescending(e => e.Id) : queryable.OrderBy(e => e.Id)
        };

        var pageSize = Math.Max(1, query.PageSize);
        var pageNumber = Math.Max(1, query.PageNumber);
        var totalCount = await queryable.CountAsync();
        var items = await queryable.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<RoadEdge> { Items = items, TotalCount = totalCount, PageNumber = pageNumber, PageSize = pageSize };
    }

    public async Task<Dictionary<int, (int edgeCount, double totalLength)>> GetStreetEdgeStatsAsync(IEnumerable<int>? streetIds)
    {
        var queryable = _context.RoadEdges.Where(e => e.StreetId != null);
        if (streetIds != null)
        {
            var list = streetIds.Distinct().ToList();
            if (list.Count == 0)
            {
                return new Dictionary<int, (int, double)>();
            }
            queryable = queryable.Where(e => list.Contains(e.StreetId!.Value));
        }

        var rows = await queryable
            .GroupBy(e => e.StreetId!.Value)
            .Select(g => new { StreetId = g.Key, Count = g.Count(), Length = g.Sum(e => e.Length) })
            .ToListAsync();
        return rows.ToDictionary(r => r.StreetId, r => (r.Count, r.Length));
    }

    public Task<List<int>> GetLabelledStreetIdsAsync(string world) =>
        _context.RoadEdges
            .Where(e => e.World == world && e.StreetId != null)
            .Select(e => e.StreetId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync();

    // Profiles, surveys, seeds

    public Task<List<RoadProfile>> ListProfilesAsync() =>
        _context.RoadProfiles.OrderBy(p => p.Name).ToListAsync();

    public Task<RoadProfile?> GetProfileAsync(int id) =>
        _context.RoadProfiles.FirstOrDefaultAsync(p => p.Id == id);

    public Task<RoadProfile?> GetProfileByNameAsync(string name) =>
        _context.RoadProfiles.FirstOrDefaultAsync(p => p.Name == name);

    public async Task<Dictionary<int, RoadClass>> GetProfileClassesAsync() =>
        await _context.RoadProfiles.Select(p => new { p.Id, p.RoadClass }).ToDictionaryAsync(p => p.Id, p => p.RoadClass);

    public Task<List<RoadSurvey>> ListSurveysAsync(string world) =>
        _context.RoadSurveys.Where(s => s.World == world).OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id).ToListAsync();

    public Task<List<RoadSeed>> ListSeedsAsync(string world) =>
        _context.RoadSeeds.Where(s => s.World == world).OrderBy(s => s.Id).ToListAsync();

    public Task<RoadSeed?> GetSeedAsync(int id) =>
        _context.RoadSeeds.FirstOrDefaultAsync(s => s.Id == id);

    public Task<bool> SurveyExistsAsync(int id) =>
        _context.RoadSurveys.AnyAsync(s => s.Id == id);

    // Other entities

    public async Task<List<RoadStreetLabeler.Structure>> GetStructuresWithLocationInBoxAsync(string world, int minX, int minZ, int maxX, int maxZ)
    {
        // Structure is table-per-type over domains; LocationId lives on Domain - use the navigation.
        var rows = await _context.Structures
            .Where(s => s.Location != null && s.Location.World == world
                        && s.Location.X >= minX && s.Location.X <= maxX
                        && s.Location.Z >= minZ && s.Location.Z <= maxZ)
            .Select(s => new { s.Id, s.StreetId, s.Location!.X, s.Location.Y, s.Location.Z })
            .ToListAsync();
        return rows.Select(r => new RoadStreetLabeler.Structure { Id = r.Id, StreetId = r.StreetId, X = r.X, Y = r.Y, Z = r.Z }).ToList();
    }

    public async Task<List<RoadSeedLocationDto>> GetDomainLocationsInBoxAsync(string world, int minX, int minZ, int maxX, int maxZ)
    {
        var domains = await _context.Domains
            .Include(d => d.Location)
            .Where(d => d.Location != null && d.Location.World == world
                        && d.Location.X >= minX && d.Location.X <= maxX
                        && d.Location.Z >= minZ && d.Location.Z <= maxZ)
            .OrderBy(d => d.Id)
            .ToListAsync();
        return domains.Select(d => new RoadSeedLocationDto
        {
            DomainId = d.Id,
            DomainType = d.GetType().Name,
            Name = d.Name,
            X = (int)Math.Floor(d.Location!.X),
            Y = (int)Math.Floor(d.Location.Y),
            Z = (int)Math.Floor(d.Location.Z)
        }).ToList();
    }

    public async Task<Dictionary<int, string>> GetStreetNamesAsync(IEnumerable<int> streetIds)
    {
        var list = streetIds.Distinct().ToList();
        if (list.Count == 0)
        {
            return new Dictionary<int, string>();
        }
        return await _context.Streets.Where(s => list.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);
    }

    public Task<bool> StreetExistsAsync(int streetId) =>
        _context.Streets.AnyAsync(s => s.Id == streetId);

    public Task<List<int>> GetExistingTownIdsAsync(IEnumerable<int> townIds)
    {
        var list = townIds.Distinct().ToList();
        return list.Count == 0
            ? Task.FromResult(new List<int>())
            : _context.Towns.Where(t => list.Contains(t.Id)).Select(t => t.Id).ToListAsync();
    }

    // Unit of work

    public void Add<T>(T entity) where T : class => _context.Set<T>().Add(entity);

    public void AddRange<T>(IEnumerable<T> entities) where T : class => _context.Set<T>().AddRange(entities);

    public void Remove<T>(T entity) where T : class => _context.Set<T>().Remove(entity);

    public void RemoveRange<T>(IEnumerable<T> entities) where T : class => _context.Set<T>().RemoveRange(entities);

    public Task SaveChangesAsync() => _context.SaveChangesAsync();

    public async Task<T> RunInTransactionAsync<T>(Func<Task<T>> work)
    {
        if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
        {
            return await work();
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        try
        {
            var result = await work();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

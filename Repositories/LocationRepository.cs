using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories;

public class LocationRepository : ILocationRepository
{
    private readonly KnKDbContext _context;

    public LocationRepository(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Location>> GetAllAsync()
    {
        return await _context.Locations.ToListAsync();
    }

    public async Task<Location?> GetByIdAsync(int id)
    {
        return await _context.Locations.FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task AddLocationAsync(Location location)
    {
        await _context.Locations.AddAsync(location);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateLocationAsync(Location location)
    {
        _context.Locations.Update(location);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteLocationAsync(int id)
    {
        var location = await _context.Locations.FindAsync(id);
        if (location != null)
        {
            _context.Locations.Remove(location);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<string>> GetDomainsUsingAsync(int locationId)
    {
        var domains = await _context.Domains
            .Where(d => d.LocationId == locationId)
            .Select(d => new
            {
                d.Name,
                Type = d is Town ? "Town" : d is District ? "District" : d is Structure ? "Structure" : "Domain"
            })
            .ToListAsync();
        return domains.Select(d => $"{d.Type} '{d.Name}'").ToList();
    }

    public async Task<PagedResult<Location>> SearchAsync(PagedQuery query)
    {
        var queryable = _context.Locations.AsQueryable();

        // Apply search term filter
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchLower = query.SearchTerm.ToLower();
            queryable = queryable.Where(l => 
                l.Name!.ToLower().Contains(searchLower) || 
                l.World!.ToLower().Contains(searchLower));
        }

        // Apply filters from dictionary
        if (query.Filters != null)
        {
            if (query.Filters.TryGetValue("world", out var world))
            {
                queryable = queryable.Where(l => l.World == world);
            }

            if (query.Filters.TryGetValue("name", out var name))
            {
                queryable = queryable.Where(l => l.Name!.Contains(name));
            }
        }

        // Apply sorting
        queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

        // Get total count before paging
        var totalCount = await queryable.CountAsync();

        // Apply paging
        var items = await queryable
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return new PagedResult<Location>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        };
    }

    private IQueryable<Location> ApplySorting(IQueryable<Location> queryable, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return queryable.OrderBy(l => l.Name);
        }

        return sortBy.ToLower() switch
        {
            "name" => sortDescending ? queryable.OrderByDescending(l => l.Name) : queryable.OrderBy(l => l.Name),
            "id" => sortDescending ? queryable.OrderByDescending(l => l.Id) : queryable.OrderBy(l => l.Id),
            "world" => sortDescending ? queryable.OrderByDescending(l => l.World) : queryable.OrderBy(l => l.World),
            "x" => sortDescending ? queryable.OrderByDescending(l => l.X) : queryable.OrderBy(l => l.X),
            "y" => sortDescending ? queryable.OrderByDescending(l => l.Y) : queryable.OrderBy(l => l.Y),
            "z" => sortDescending ? queryable.OrderByDescending(l => l.Z) : queryable.OrderBy(l => l.Z),
            _ => queryable.OrderBy(l => l.Name)
        };
    }
}

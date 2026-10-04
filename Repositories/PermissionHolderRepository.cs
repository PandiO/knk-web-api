using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories;

/// <summary>
/// Read-only union over the concrete User and PermissionGroup tables. PermissionHolder itself has
/// no display-name column, so a picker cannot meaningfully query the TPT base table alone.
/// </summary>
public class PermissionHolderRepository : IPermissionHolderRepository
{
    private readonly KnKDbContext _context;

    public PermissionHolderRepository(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<PermissionHolderListDto?> GetByIdAsync(int id)
    {
        if (id <= 0) return null;
        return await HolderRows().SingleOrDefaultAsync(holder => holder.Id == id);
    }

    public async Task<PagedResult<PermissionHolderListDto>> SearchAsync(PagedQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var holders = HolderRows();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var search = query.SearchTerm.Trim().ToLower();
            holders = holders.Where(holder => holder.Name.ToLower().Contains(search));
        }

        if (query.Filters != null
            && query.Filters.TryGetValue("holderType", out var holderType)
            && !string.IsNullOrWhiteSpace(holderType))
        {
            holders = holders.Where(holder => holder.HolderType == holderType);
        }

        holders = query.SortBy?.ToLowerInvariant() switch
        {
            "id" => query.SortDescending
                ? holders.OrderByDescending(holder => holder.Id)
                : holders.OrderBy(holder => holder.Id),
            "holdertype" or "type" => query.SortDescending
                ? holders.OrderByDescending(holder => holder.HolderType).ThenByDescending(holder => holder.Name)
                : holders.OrderBy(holder => holder.HolderType).ThenBy(holder => holder.Name),
            _ => query.SortDescending
                ? holders.OrderByDescending(holder => holder.Name).ThenByDescending(holder => holder.Id)
                : holders.OrderBy(holder => holder.Name).ThenBy(holder => holder.Id)
        };

        var totalCount = await holders.CountAsync();
        var items = await holders.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<PermissionHolderListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    private IQueryable<PermissionHolderListDto> HolderRows()
    {
        var users = _context.Users.AsNoTracking().Select(user => new PermissionHolderListDto
        {
            Id = user.Id,
            Name = user.Username,
            HolderType = "User"
        });
        var groups = _context.PermissionGroups.AsNoTracking().Select(group => new PermissionHolderListDto
        {
            Id = group.Id,
            Name = group.Name,
            HolderType = "PermissionGroup"
        });
        return users.Concat(groups);
    }
}

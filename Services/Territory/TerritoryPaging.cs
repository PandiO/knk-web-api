using System;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Territory
{
    internal static class TerritoryPaging
    {
        public const int MaxPageSize = 200;

        public static async Task<PagedResultDto<T>> PageAsync<T>(IQueryable<T> query, PagedQueryDto q)
        {
            int size = Math.Clamp(q.PageSize, 1, MaxPageSize);
            int number = Math.Max(1, q.PageNumber);
            int total = await query.CountAsync();
            var items = await query.Skip((number - 1) * size).Take(size).ToListAsync();
            return new PagedResultDto<T> { Items = items, TotalCount = total, PageNumber = number, PageSize = size };
        }

        public static string? Term(PagedQueryDto q) =>
            string.IsNullOrWhiteSpace(q.SearchTerm) ? null : q.SearchTerm.Trim().ToLower();

        public static int? IntFilter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && int.TryParse(v, out var i) ? i : null;
    }
}

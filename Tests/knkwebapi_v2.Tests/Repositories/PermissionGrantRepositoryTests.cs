using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>
/// PandiO/knk-workspace#408: the dashboard's PermissionGrant table searches, pages and sorts through
/// <see cref="PermissionGrantRepository.SearchAsync"/>.
/// </summary>
public class PermissionGrantRepositoryTests
{
    private readonly string _dbName = $"PermissionGrants_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        db.Users.Add(new User { Id = 1, Username = "Alice" });
        db.PermissionGroups.Add(new PermissionGroup { Id = 2, Name = "Staff" });
        db.PermissionGrants.AddRange(
            new PermissionGrant { Id = 10, HolderId = 2, Node = "knk.gate.open", Value = true, ExpiresAt = new DateTime(2027, 3, 1) },
            new PermissionGrant { Id = 11, HolderId = 1, Node = "knk.admin.world", Value = false },
            new PermissionGrant { Id = 12, HolderId = 2, Node = "knk.admin.content", Value = true, ExpiresAt = new DateTime(2027, 1, 1) },
            new PermissionGrant { Id = 13, HolderId = 1, Node = "knk.fly", Value = true });
        await db.SaveChangesAsync();
    }

    private async Task<int[]> IdsAsync(PagedQuery query)
    {
        await using var db = NewContext();
        var result = await new PermissionGrantRepository(db).SearchAsync(query);
        return result.Items.Select(g => g.Id).ToArray();
    }

    [Fact]
    public async Task WithoutASortColumn_SortsByNode()
    {
        await SeedAsync();
        Assert.Equal(new[] { 12, 11, 13, 10 }, await IdsAsync(new PagedQuery { PageSize = 10 }));
        Assert.Equal(new[] { 10, 13, 11, 12 }, await IdsAsync(new PagedQuery { PageSize = 10, SortDescending = true }));
    }

    [Theory]
    [InlineData("id", false, new[] { 10, 11, 12, 13 })]
    [InlineData("id", true, new[] { 13, 12, 11, 10 })]
    [InlineData("holderId", false, new[] { 11, 13, 12, 10 })]
    [InlineData("value", false, new[] { 11, 12, 13, 10 })]
    [InlineData("value", true, new[] { 12, 13, 10, 11 })]
    [InlineData("expiresAt", true, new[] { 10, 12, 11, 13 })]
    [InlineData("node", true, new[] { 10, 13, 11, 12 })]
    [InlineData("unknownColumn", false, new[] { 12, 11, 13, 10 })]
    public async Task SortsByTheColumnTheTableAsksFor(string sortBy, bool descending, int[] expected)
    {
        await SeedAsync();
        Assert.Equal(expected, await IdsAsync(new PagedQuery { PageSize = 10, SortBy = sortBy, SortDescending = descending }));
    }

    [Fact]
    public async Task SearchTerm_MatchesTheNode_AndPagingKeepsTheTotal()
    {
        await SeedAsync();
        await using var db = NewContext();

        var result = await new PermissionGrantRepository(db).SearchAsync(
            new PagedQuery { SearchTerm = "ADMIN", PageNumber = 2, PageSize = 1 });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal("knk.admin.world", Assert.Single(result.Items).Node);
    }
}

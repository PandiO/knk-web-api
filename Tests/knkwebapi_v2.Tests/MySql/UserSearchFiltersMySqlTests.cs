using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// The moderation search (PandiO/knk-workspace#565) on a real MySQL: the dashless UUID match (REPLACE in SQL), the
/// state filters and the new sort keys translate and return what the InMemory tests expect.
/// </summary>
[Trait("Category", "requires-mysql")]
public class UserSearchFiltersMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public UserSearchFiltersMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    [MySqlFact]
    public async Task UuidWithoutDashes_StateFilters_AndSortKeys()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var uuid = Guid.NewGuid().ToString();
        await using (var ctx = _db.NewContext())
        {
            ctx.Users.AddRange(
                new User { Username = "a" + suffix, Uuid = uuid, IsOnline = true, LastSeenAt = new DateTime(2026, 11, 14) },
                new User { Username = "b" + suffix, IsFrozen = true, LastSeenAt = new DateTime(2026, 11, 15) },
                new User { Username = "c" + suffix, TransferLockedAt = new DateTime(2026, 11, 1) });
            await ctx.SaveChangesAsync();
        }

        async Task<List<string>> Search(Dictionary<string, string>? filters = null, string? sortBy = null, bool desc = false, string? term = null)
        {
            await using var ctx = _db.NewContext();
            var result = await new UserRepository(ctx).SearchAsync(new PagedQuery
            {
                PageSize = 50, SearchTerm = term ?? suffix, Filters = filters, SortBy = sortBy, SortDescending = desc
            });
            return result.Items.Select(u => u.Username[..1]).ToList();
        }

        var dashless = uuid.Replace("-", "").ToUpperInvariant();
        Assert.Equal(new[] { "a" }, await Search(new Dictionary<string, string> { ["uuid"] = dashless }));
        Assert.Equal(new[] { "a" }, await Search(term: dashless));
        Assert.Equal(new[] { "a" }, await Search(new Dictionary<string, string> { ["online"] = "true" }));
        Assert.Equal(new[] { "b" }, await Search(new Dictionary<string, string> { ["frozen"] = "true" }));
        Assert.Equal(new[] { "c" }, await Search(new Dictionary<string, string> { ["paymentsLocked"] = "true" }));
        Assert.Equal(new[] { "a", "b" }, await Search(new Dictionary<string, string> { ["paymentsLocked"] = "false" }));
        Assert.Equal(new[] { "b", "a", "c" }, await Search(sortBy: "lastseenat", desc: true));
        // Balances only change through the ledger here (its triggers keep a direct insert's XP at 0), so the XP sort
        // shows its tie-break: the id, oldest first. The InMemory tests cover the XP order itself.
        Assert.Equal(new[] { "a", "b", "c" }, await Search(sortBy: "experiencepoints", desc: true));
    }
}

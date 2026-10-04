using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories;

public class PermissionHolderRepositoryTests
{
    private readonly string _dbName = $"PermissionHolders_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        db.Users.AddRange(
            new User { Id = 1, Username = "Alice" },
            new User { Id = 2, Username = "Zelda" });
        db.PermissionGroups.AddRange(
            new PermissionGroup { Id = 3, Name = "Default" },
            new PermissionGroup { Id = 4, Name = "Royal" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Search_CombinesUsersAndGroupsByDisplayName()
    {
        await SeedAsync();
        await using var db = NewContext();

        var result = await new PermissionHolderRepository(db).SearchAsync(new PagedQuery { PageSize = 10 });

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(new[] { "Alice", "Default", "Royal", "Zelda" }, result.Items.Select(item => item.Name));
        Assert.Equal(new[] { "User", "PermissionGroup", "PermissionGroup", "User" },
            result.Items.Select(item => item.HolderType));
    }

    [Fact]
    public async Task SearchTerm_MatchesBothConcreteTypes()
    {
        await SeedAsync();
        await using var db = NewContext();

        var result = await new PermissionHolderRepository(db).SearchAsync(
            new PagedQuery { SearchTerm = "al", PageSize = 10 });

        Assert.Equal(new[] { "Alice", "Royal" }, result.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task HolderTypeFilterAndPaging_AreAppliedToTheUnion()
    {
        await SeedAsync();
        await using var db = NewContext();

        var result = await new PermissionHolderRepository(db).SearchAsync(new PagedQuery
        {
            PageNumber = 2,
            PageSize = 1,
            Filters = new Dictionary<string, string> { ["holderType"] = "PermissionGroup" }
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal("Royal", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task GetById_ReturnsTheConcreteDisplayNameAndType()
    {
        await SeedAsync();
        await using var db = NewContext();
        var repository = new PermissionHolderRepository(db);

        var user = await repository.GetByIdAsync(1);
        var group = await repository.GetByIdAsync(4);

        Assert.Equal(("Alice", "User"), (user?.Name, user?.HolderType));
        Assert.Equal(("Royal", "PermissionGroup"), (group?.Name, group?.HolderType));
        Assert.Null(await repository.GetByIdAsync(999));
    }
}

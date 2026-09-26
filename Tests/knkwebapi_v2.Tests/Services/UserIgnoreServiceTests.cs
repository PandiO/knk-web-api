using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-18 Phase 2: the ignore list's rules (docs/specs/private-messages/DESIGN.md §3.1/§3.2) -
/// idempotent add and delete, no self-ignore, the 100-entry limit, unignorable staff, and rows
/// going away with either user. Real repositories over the in-memory provider; only permission
/// resolution is mocked.
/// </summary>
public class UserIgnoreServiceTests
{
    private readonly string _dbName = $"UserIgnores_{Guid.NewGuid()}";
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly HashSet<int> _unignorable = new();

    public UserIgnoreServiceTests()
    {
        _permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((int userId, string node) => new PermissionCheckResponseDto
            {
                UserId = userId,
                Node = node,
                Result = node == UserIgnoreService.UnignorableNode && _unignorable.Contains(userId)
                    ? PermissionResolutionResult.Granted
                    : PermissionResolutionResult.Undeclared
            });

        using var db = NewContext();
        db.Users.AddRange(
            new User { Id = 1, Username = "alice", Uuid = "00000000-0000-0000-0000-000000000001" },
            new User { Id = 2, Username = "bob", Uuid = "00000000-0000-0000-0000-000000000002" },
            new User { Id = 3, Username = "staff" });
        db.SaveChanges();
    }

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private UserIgnoreService NewService(KnKDbContext db) =>
        new(new UserIgnoreRepository(db), new UserRepository(db), _permissions.Object);

    private async Task<UserIgnoreAddResult> AddAsync(int userId, int ignoredUserId)
    {
        await using var db = NewContext();
        return await NewService(db).AddAsync(userId, ignoredUserId);
    }

    private async Task<List<UserIgnoreDto>?> GetAsync(int userId)
    {
        await using var db = NewContext();
        return await NewService(db).GetAsync(userId);
    }

    [Fact]
    public async Task Add_ThenGet_ListsTheIgnoredPlayerWithNameUuidAndDate()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        Assert.Equal(UserIgnoreAddResult.Ignored, await AddAsync(1, 2));

        var list = await GetAsync(1);
        var entry = Assert.Single(list!);
        Assert.Equal((2, "bob", "00000000-0000-0000-0000-000000000002"), (entry.IgnoredUserId, entry.IgnoredUsername, entry.IgnoredUuid));
        Assert.True(entry.CreatedAt >= before);
        Assert.Equal(DateTimeKind.Utc, entry.CreatedAt.Kind);
        Assert.Empty((await GetAsync(2))!);
    }

    [Fact]
    public async Task Add_IsIdempotent()
    {
        Assert.Equal(UserIgnoreAddResult.Ignored, await AddAsync(1, 2));
        Assert.Equal(UserIgnoreAddResult.Ignored, await AddAsync(1, 2));

        Assert.Single((await GetAsync(1))!);
    }

    [Fact]
    public async Task Add_Self_IsRefused()
    {
        Assert.Equal(UserIgnoreAddResult.SelfIgnore, await AddAsync(1, 1));
        Assert.Empty((await GetAsync(1))!);
    }

    [Fact]
    public async Task Add_UnknownUserOnEitherSide_IsNotFound()
    {
        Assert.Equal(UserIgnoreAddResult.UserNotFound, await AddAsync(1, 99));
        Assert.Equal(UserIgnoreAddResult.UserNotFound, await AddAsync(99, 1));
        Assert.Null(await GetAsync(99));
    }

    [Fact]
    public async Task Add_UnignorableStaff_IsRefused()
    {
        _unignorable.Add(3);

        Assert.Equal(UserIgnoreAddResult.CannotIgnoreStaff, await AddAsync(1, 3));
        Assert.Empty((await GetAsync(1))!);
        _permissions.Verify(p => p.CheckAsync(3, UserIgnoreService.UnignorableNode), Times.Once);
    }

    [Fact]
    public async Task Add_PastTheLimit_IsRefused()
    {
        await using (var db = NewContext())
        {
            for (var id = 100; id < 100 + UserIgnoreService.MaxIgnoresPerUser; id++)
            {
                db.Users.Add(new User { Id = id, Username = "filler" + id });
                db.UserIgnores.Add(new UserIgnore { UserId = 1, IgnoredUserId = id });
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal(UserIgnoreAddResult.IgnoreLimitReached, await AddAsync(1, 2));
        Assert.Equal(UserIgnoreService.MaxIgnoresPerUser, (await GetAsync(1))!.Count);
        // Already on the list still answers "ignored" at the limit (idempotent PUT).
        Assert.Equal(UserIgnoreAddResult.Ignored, await AddAsync(1, 100));
    }

    [Fact]
    public async Task Remove_IsIdempotent()
    {
        await AddAsync(1, 2);

        await using (var db = NewContext())
        {
            await NewService(db).RemoveAsync(1, 2);
            await NewService(db).RemoveAsync(1, 2);
            await NewService(db).RemoveAsync(1, 99);
        }

        Assert.Empty((await GetAsync(1))!);
    }

    [Fact]
    public async Task DeletingEitherUser_RemovesTheirIgnoreRows()
    {
        await AddAsync(1, 2);
        await AddAsync(2, 1);
        await AddAsync(3, 1);

        await using (var db = NewContext())
        {
            // The in-memory provider only cascades to tracked rows - load them, as MySQL would
            // cascade on its own (FKs configured below).
            await db.UserIgnores.LoadAsync();
            await new UserRepository(db).DeleteUserAsync(2);
        }

        await using (var db = NewContext())
        {
            var rows = await db.UserIgnores.ToListAsync();
            Assert.Equal(new[] { (3, 1) }, rows.Select(r => (r.UserId, r.IgnoredUserId)).ToArray());

            var foreignKeys = db.Model.FindEntityType(typeof(UserIgnore))!.GetForeignKeys().ToList();
            Assert.Equal(2, foreignKeys.Count);
            Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior));
            Assert.Contains(db.Model.FindEntityType(typeof(UserIgnore))!.GetIndexes(),
                ix => ix.IsUnique && ix.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId", "IgnoredUserId" }));
        }
    }
}

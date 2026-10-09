using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-59 on a real MySQL: the unique (HolderId, Node) index from the
/// UniquePermissionGrantHolderNode migration, and the grant service's upsert/conflict rules
/// against it. The column's utf8mb4_general_ci collation makes nodes differing only in case
/// the same node.
/// </summary>
[Trait("Category", "requires-mysql")]
public class PermissionGrantUniquenessMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public PermissionGrantUniquenessMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static PermissionGrantService Service(KnKDbContext ctx)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<PermissionGrantDto>(It.IsAny<PermissionGrant>()))
            .Returns((PermissionGrant g) => new PermissionGrantDto { Id = g.Id, HolderId = g.HolderId, Node = g.Node, Value = g.Value, ExpiresAt = g.ExpiresAt });
        return new PermissionGrantService(new PermissionGrantRepository(ctx), mapper.Object, new UserRepository(ctx), new Mock<IAuditLogService>().Object);
    }

    private async Task<int> SeedUserAsync()
    {
        await using var ctx = _db.NewContext();
        var user = new User { Username = "g" + Guid.NewGuid().ToString("N")[..10] };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    [MySqlFact]
    public async Task Create_SameNodeTwice_KeepsOneRowWithTheLatestValue()
    {
        var userId = await SeedUserAsync();

        await using (var ctx = _db.NewContext())
        {
            await Service(ctx).CreateAsync(new PermissionGrantDto { HolderId = userId, Node = "knk.gate.open", Value = true });
        }
        await using (var ctx = _db.NewContext())
        {
            await Service(ctx).CreateAsync(new PermissionGrantDto { HolderId = userId, Node = "KNK.Gate.Open", Value = false, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        }

        await using var check = _db.NewContext();
        var rows = await check.PermissionGrants.Where(g => g.HolderId == userId).ToListAsync();
        var row = Assert.Single(rows);
        Assert.False(row.Value);
        Assert.NotNull(row.ExpiresAt);
    }

    [MySqlFact]
    public async Task Update_OntoATakenNode_ThrowsConflictAndKeepsBothRows()
    {
        var userId = await SeedUserAsync();
        int moveId;
        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            await service.CreateAsync(new PermissionGrantDto { HolderId = userId, Node = "knk.gate.open", Value = true });
            moveId = (await service.CreateAsync(new PermissionGrantDto { HolderId = userId, Node = "knk.fly", Value = true })).Id!.Value;
        }

        await using (var ctx = _db.NewContext())
        {
            await Assert.ThrowsAsync<PermissionGrantConflictException>(() =>
                Service(ctx).UpdateAsync(moveId, new PermissionGrantDto { HolderId = userId, Node = "knk.gate.open", Value = false }));
        }

        await using var check = _db.NewContext();
        var nodes = await check.PermissionGrants.Where(g => g.HolderId == userId).Select(g => g.Node).OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "knk.fly", "knk.gate.open" }, nodes);
    }

    [MySqlFact]
    public async Task UniqueIndex_RejectsADuplicateRowWrittenDirectly()
    {
        var userId = await SeedUserAsync();
        await using var ctx = _db.NewContext();
        ctx.PermissionGrants.Add(new PermissionGrant { HolderId = userId, Node = "knk.fly", Value = true });
        await ctx.SaveChangesAsync();

        ctx.PermissionGrants.Add(new PermissionGrant { HolderId = userId, Node = "knk.fly", Value = false });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}

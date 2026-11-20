using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// SeedNavigateDefaultNode on a real MySQL (PandiO/knk-workspace#845): a migrations-only database has the Default group,
/// so it gets <c>knk.navigate</c>; an in-house deny on the group (or a parent) is kept and no grant is added next to
/// it; Down leaves the grant; running Up again adds no duplicate; <c>knk.navigate.any</c> is not seeded.
/// </summary>
[Trait("Category", "requires-mysql")]
public class SeedNavigateDefaultNodeMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261120101407_AddPermissionGroupDefaultRouteMode";
    private const string Node = "knk.navigate";
    private readonly MySqlTestDatabase _db;

    public SeedNavigateDefaultNodeMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static Task<int> DefaultId(KnKDbContext ctx) =>
        ctx.PermissionGroups.AsNoTracking().Where(g => g.Name == "Default" && g.ParentGroupId == null).Select(g => g.Id).SingleAsync();

    private static Task<int> Count(KnKDbContext ctx, int holderId, string node, bool value = true) =>
        ctx.PermissionGrants.AsNoTracking().CountAsync(g => g.HolderId == holderId && g.Node == node && g.Value == value);

    [MySqlFact]
    public async Task GivesDefaultTheNode_KeepsAnInHouseDeny_AndIsIdempotent()
    {
        int defaultGroup;

        await using (var ctx = _db.NewContext())
        {
            defaultGroup = await DefaultId(ctx);
            Assert.Equal(1, await Count(ctx, defaultGroup, Node));
            Assert.False(await ctx.PermissionGrants.AnyAsync(g => g.Node == "knk.navigate.any"));

            // Roll back: Down keeps the grant.
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            Assert.Equal(1, await Count(ctx, defaultGroup, Node));

            // An admin denied the node on the group before the seed ran: Up must leave it a deny.
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE permission_grants SET Value = 0 WHERE HolderId = {defaultGroup} AND Node = {Node};");
            await migrator.MigrateAsync();
            Assert.Equal(0, await Count(ctx, defaultGroup, Node));
            Assert.Equal(1, await Count(ctx, defaultGroup, Node, value: false));

            // A wildcard deny (knk.navigate.*) also decides the bare node.
            await migrator.MigrateAsync(Previous);
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE permission_grants SET Node = 'knk.navigate.*' WHERE HolderId = {defaultGroup} AND Node = {Node};");
            await migrator.MigrateAsync();
            Assert.Equal(0, await Count(ctx, defaultGroup, Node));

            // Without the deny, Up grants it once, and again after a rollback adds no duplicate.
            await migrator.MigrateAsync(Previous);
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM permission_grants WHERE HolderId = {defaultGroup} AND Node = 'knk.navigate.*';");
            await migrator.MigrateAsync();
            await migrator.MigrateAsync(Previous);
            await migrator.MigrateAsync();
        }

        await using var check = _db.NewContext();
        Assert.Equal(1, await Count(check, defaultGroup, Node));
        Assert.Equal(0, await check.PermissionGrants.AsNoTracking().CountAsync(g => g.HolderId == defaultGroup && g.Node == Node && g.ExpiresAt != null));
    }
}

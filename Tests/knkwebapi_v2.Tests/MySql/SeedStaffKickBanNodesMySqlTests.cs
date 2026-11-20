using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// SeedStaffKickBanNodes on a real MySQL (PandiO/knk-workspace#565): a migrations-only database has no Staff group, so
/// nothing is added; a Staff group gets knk.kick and knk.ban, except one a parent group denies; running it again after
/// a rollback adds no duplicate; Down leaves the grants.
/// </summary>
[Trait("Category", "requires-mysql")]
public class SeedStaffKickBanNodesMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261115092735_AddAuditLogSubject";
    private readonly MySqlTestDatabase _db;

    public SeedStaffKickBanNodesMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static Task<int> Count(KnKDbContext ctx, int holderId, string node, bool value = true) =>
        ctx.PermissionGrants.AsNoTracking().CountAsync(g => g.HolderId == holderId && g.Node == node && g.Value == value);

    [MySqlFact]
    public async Task GivesStaffBothNodes_RespectsAParentsDeny_AndIsIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int staff, parent;

        await using (var ctx = _db.NewContext())
        {
            Assert.False(await ctx.PermissionGroups.AnyAsync(g => g.Name == "Staff")); // migrations only: nothing to seed
            Assert.False(await ctx.PermissionGrants.AnyAsync(g => g.Node == "knk.kick" || g.Node == "knk.ban"));

            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);

            // Plain SQL: the schema is the older one here (see PermissionGroupRows).
            parent = await PermissionGroupRows.InsertAsync(ctx, "Rules" + suffix, 35, null,
                new PermissionGrant { Node = "knk.ban", Value = false });
            staff = await PermissionGroupRows.InsertAsync(ctx, "Staff", 40, parent);

            await migrator.MigrateAsync();
        }

        await using (var check = _db.NewContext())
        {
            Assert.Equal(1, await Count(check, staff, "knk.kick"));
            Assert.Equal(0, await Count(check, staff, "knk.ban")); // the parent's deny decides it
            Assert.Equal(1, await Count(check, parent, "knk.ban", value: false));

            // Roll back (Down keeps the grant), drop the parent, and run Up again.
            var migrator = check.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            Assert.Equal(1, await Count(check, staff, "knk.kick"));
            await check.Database.ExecuteSqlRawAsync($"UPDATE permission_groups SET ParentGroupId = NULL WHERE Id = {staff};");
            await migrator.MigrateAsync();
        }

        await using var after = _db.NewContext();
        Assert.Equal(1, await Count(after, staff, "knk.kick")); // no duplicate
        Assert.Equal(1, await Count(after, staff, "knk.ban"));
    }
}

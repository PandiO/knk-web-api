using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// SeedStaffFreezeNodes on a real MySQL (PandiO/knk-workspace#367): a migrations-only database has no Staff group, so
/// nothing is added; a Staff group gets both nodes, except one a parent group denies; running it again after a rollback
/// adds no duplicate; Down leaves the grants.
/// </summary>
[Trait("Category", "requires-mysql")]
public class SeedStaffFreezeNodesMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261109101501_AddConfigurationDrafts";
    private readonly MySqlTestDatabase _db;

    public SeedStaffFreezeNodesMySqlTests(MySqlTestDatabase db)
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
            Assert.False(await ctx.PermissionGrants.AnyAsync(g => g.Node == "knk.freeze" || g.Node == "knk.unfreeze"));

            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);

            var parentGroup = new PermissionGroup
            {
                Name = "Rules" + suffix, Weight = 35,
                Grants = new List<PermissionGrant> { new() { Node = "knk.unfreeze", Value = false } },
            };
            ctx.PermissionGroups.Add(parentGroup);
            await ctx.SaveChangesAsync();
            var staffGroup = new PermissionGroup { Name = "Staff", Weight = 40, ParentGroupId = parentGroup.Id };
            ctx.PermissionGroups.Add(staffGroup);
            await ctx.SaveChangesAsync();
            (staff, parent) = (staffGroup.Id, parentGroup.Id);

            await migrator.MigrateAsync();
        }

        await using (var check = _db.NewContext())
        {
            Assert.Equal(1, await Count(check, staff, "knk.freeze"));
            Assert.Equal(0, await Count(check, staff, "knk.unfreeze")); // the parent's deny decides it
            Assert.Equal(1, await Count(check, parent, "knk.unfreeze", value: false));

            // Roll back (Down keeps the grant), drop the parent, and run Up again.
            var migrator = check.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            Assert.Equal(1, await Count(check, staff, "knk.freeze"));
            await check.Database.ExecuteSqlRawAsync($"UPDATE permission_groups SET ParentGroupId = NULL WHERE Id = {staff};");
            await migrator.MigrateAsync();
        }

        await using var after = _db.NewContext();
        Assert.Equal(1, await Count(after, staff, "knk.freeze")); // no duplicate
        Assert.Equal(1, await Count(after, staff, "knk.unfreeze"));
        Assert.Equal(0, await after.PermissionGrants.AsNoTracking().CountAsync(g => g.HolderId == staff && g.ExpiresAt != null));
    }
}

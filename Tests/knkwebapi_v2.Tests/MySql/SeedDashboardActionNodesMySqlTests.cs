using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// SeedDashboardActionNodes on a real MySQL (the recursive chain checks run on the server, PandiO/knk-workspace#280):
/// a migrations-only database gives Admin the three nodes; the alpha shape (Admin with knk.admin.*) adds none; explicit
/// area holders get delete and export but the Staff group doesn't; denies, wildcards and expired grants are respected;
/// running Up twice adds nothing; Down takes back only its own.
/// </summary>
[Trait("Category", "requires-mysql")]
public class SeedDashboardActionNodesMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261108162526_AddKitClaimDelivery";
    private readonly MySqlTestDatabase _db;

    public SeedDashboardActionNodesMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static async Task<HashSet<string>> NodesOf(KnKDbContext ctx, int holderId) =>
        (await ctx.PermissionGrants.AsNoTracking().Where(g => g.HolderId == holderId && g.Value).Select(g => g.Node).ToListAsync()).ToHashSet();

    private static async Task<int> GroupId(KnKDbContext ctx, string name) =>
        await ctx.PermissionGroups.AsNoTracking().Where(g => g.Name == name).Select(g => g.Id).SingleAsync();

    private static PermissionGrant Grant(string node, bool value = true, DateTime? expires = null) => new() { Node = node, Value = value, ExpiresAt = expires };

    [MySqlFact]
    public async Task GrantsAdminAndTheAreaHolders_ButNotStaff_AndRespectsWhatTheChainDecides()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int staff, builders, deniers, wildDeny, expired, plain, builder, deniedUser, member;

        await using (var ctx = _db.NewContext())
        {
            // Migrations only: Admin has no wildcard, so it gets all three.
            var admin = await GroupId(ctx, "Admin");
            Assert.Superset(new HashSet<string> { "knk.admin.delete", "knk.admin.delete.bulk", "knk.admin.export" }, await NodesOf(ctx, admin));

            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            Assert.DoesNotContain("knk.admin.delete", await NodesOf(ctx, admin));

            // The alpha shape: Admin holds knk.admin.*.
            ctx.PermissionGrants.Add(new PermissionGrant { HolderId = admin, Node = "knk.admin.*", Value = true });

            var groups = new[]
            {
                new PermissionGroup { Name = "Staff", Weight = 25, Grants = new List<PermissionGrant> { Grant("knk.kit.manage"), Grant("knk.admin.user.manage") } },
                new PermissionGroup { Name = "Builders" + suffix, Weight = 30, Grants = new List<PermissionGrant> { Grant("knk.admin.world") } },
                new PermissionGroup { Name = "Deniers" + suffix, Weight = 31, Grants = new List<PermissionGrant> { Grant("knk.admin.content"), Grant("knk.admin.delete", false) } },
                new PermissionGroup { Name = "WildDeny" + suffix, Weight = 32, Grants = new List<PermissionGrant> { Grant("knk.admin.road"), Grant("knk.admin.delete.*", false) } },
                new PermissionGroup { Name = "Expired" + suffix, Weight = 33, Grants = new List<PermissionGrant> { Grant("knk.admin.road", expires: DateTime.UtcNow.AddDays(-1)) } },
                new PermissionGroup { Name = "Plain" + suffix, Weight = 34, Grants = new List<PermissionGrant> { Grant("knk.freeze"), Grant("knk.admin.export", false) } },
            };
            ctx.PermissionGroups.AddRange(groups);
            await ctx.SaveChangesAsync();
            (staff, builders, deniers, wildDeny, expired, plain) = (groups[0].Id, groups[1].Id, groups[2].Id, groups[3].Id, groups[4].Id, groups[5].Id);

            var users = new[]
            {
                new User { Username = "b" + suffix, Grants = new List<PermissionGrant> { Grant("knk.admin.world") } },
                new User { Username = "d" + suffix, Grants = new List<PermissionGrant> { Grant("knk.siege.admin.manage") } },
                new User { Username = "m" + suffix },
            };
            ctx.Users.AddRange(users);
            await ctx.SaveChangesAsync();
            (builder, deniedUser, member) = (users[0].Id, users[1].Id, users[2].Id);
            // The second user is in a group that denies export; the third only inherits an area node from Builders.
            ctx.UserPermissionGroups.Add(new UserPermissionGroup { UserId = deniedUser, PermissionGroupId = plain });
            ctx.UserPermissionGroups.Add(new UserPermissionGroup { UserId = member, PermissionGroupId = builders });
            await ctx.SaveChangesAsync();

            await migrator.MigrateAsync();
            await migrator.MigrateAsync(); // up to date: runs nothing twice
        }

        await using var check = _db.NewContext();
        var adminNodes = await NodesOf(check, await GroupId(check, "Admin"));
        Assert.DoesNotContain("knk.admin.delete", adminNodes); // covered by knk.admin.*
        Assert.DoesNotContain("knk.admin.delete.bulk", adminNodes);

        var staffNodes = await NodesOf(check, staff);
        Assert.DoesNotContain("knk.admin.delete", staffNodes); // developer 2026-11-08
        Assert.DoesNotContain("knk.admin.export", staffNodes);

        Assert.Superset(new HashSet<string> { "knk.admin.delete", "knk.admin.export" }, await NodesOf(check, builders));
        Assert.DoesNotContain("knk.admin.delete.bulk", await NodesOf(check, builders));

        var denierNodes = await NodesOf(check, deniers);
        Assert.Contains("knk.admin.export", denierNodes);
        Assert.Single(check.PermissionGrants.AsNoTracking().Where(g => g.HolderId == deniers && g.Node == "knk.admin.delete")); // still the deny
        Assert.DoesNotContain("knk.admin.delete", await NodesOf(check, wildDeny)); // knk.admin.delete.* decides it
        Assert.Contains("knk.admin.export", await NodesOf(check, wildDeny));
        Assert.DoesNotContain("knk.admin.export", await NodesOf(check, expired));
        Assert.DoesNotContain("knk.admin.delete", await NodesOf(check, plain)); // no area node

        Assert.Superset(new HashSet<string> { "knk.admin.delete", "knk.admin.export" }, await NodesOf(check, builder));
        var deniedUserNodes = await NodesOf(check, deniedUser);
        Assert.Contains("knk.admin.delete", deniedUserNodes);
        Assert.DoesNotContain("knk.admin.export", deniedUserNodes); // their group denies it
        Assert.Empty(await NodesOf(check, member)); // inherits Builders' grants, holds no area node itself

        // Down takes back the backfill and Admin's grants, and keeps every deny.
        await check.GetService<IMigrator>().MigrateAsync(Previous);
        await using var after = _db.NewContext();
        Assert.DoesNotContain("knk.admin.delete", await NodesOf(after, builders));
        Assert.DoesNotContain("knk.admin.export", await NodesOf(after, builder));
        Assert.Single(after.PermissionGrants.AsNoTracking().Where(g => g.HolderId == deniers && g.Node == "knk.admin.delete" && !g.Value));
        Assert.Single(after.PermissionGrants.AsNoTracking().Where(g => g.HolderId == plain && g.Node == "knk.admin.export" && !g.Value));
        await after.GetService<IMigrator>().MigrateAsync();
    }
}

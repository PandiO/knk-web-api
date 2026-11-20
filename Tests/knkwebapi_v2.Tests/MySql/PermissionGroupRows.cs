using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Inserts a permission group and its grants with plain SQL, for the seed migration tests that roll the schema back to
/// an older migration first. EF would write every column of today's <see cref="PermissionGroup"/>, and a column added
/// later (such as <c>DefaultRouteMode</c>, PandiO/knk-workspace#852) doesn't exist there yet. Only the columns of the
/// first <c>permission_groups</c> table are written; the later ones have database defaults or are nullable.
/// </summary>
internal static class PermissionGroupRows
{
    public static async Task<int> InsertAsync(KnKDbContext ctx, string name, int weight, int? parentGroupId = null,
        params PermissionGrant[] grants)
    {
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO permission_holders (ChatPrefix, ChatSuffix) VALUES (NULL, NULL);
               INSERT INTO permission_groups (Id, Name, Weight, IsPremiumTier, SalaryMultiplier, ParentGroupId)
               VALUES (LAST_INSERT_ID(), {name}, {weight}, 0, 1.0, {parentGroupId});");
        var id = await ctx.Database.SqlQuery<int>($"SELECT Id AS Value FROM permission_groups WHERE Name = {name}").SingleAsync();
        foreach (var grant in grants)
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) VALUES ({id}, {grant.Node}, {grant.Value}, {grant.ExpiresAt});");
        }
        return id;
    }
}

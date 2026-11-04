using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using knkwebapi_v2.Properties;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Chat hashtag tokens (PandiO/knk-workspace#233): rank seeds for the plugin's new nodes, the same way as
    /// <see cref="SeedCommandPermissionNodes"/> (its tiers and its <see cref="SeedCommandPermissionNodes.GrantSql"/>: a
    /// group is skipped when it is missing, or when it or a group in its parent chain already has a grant or deny for
    /// the node or a wildcard covering it).
    /// <list type="bullet">
    /// <item><c>knk.chat.token.*</c>: every token (<c>knk.chat.token.hand</c>, <c>.kills</c>, …) for everyone (the
    /// Default group, decision 2026-11-04). Staff can take one away from a group with a deny grant.</item>
    /// <item><c>knk.chat.tokens.bypass</c>: skips the Game Settings limits (max tokens per message, statistics-token
    /// cooldown); Staff, Moderator and Admin. Not under <c>knk.chat.token.*</c>, so the Default seed doesn't give it.</item>
    /// </list>
    /// Down removes only these grants (value 1, no expiry). Data only, no schema change.
    /// </summary>
    [DbContext(typeof(KnKDbContext))]
    [Migration("20261104130000_SeedChatTokenNodes")]
    public partial class SeedChatTokenNodes : Migration
    {
        /// <summary>Node → tier (<see cref="SeedCommandPermissionNodes.Tiers"/>).</summary>
        public static readonly (string Node, string Tier)[] NewGrants =
        {
            ("knk.chat.token.*", "Default"),
            ("knk.chat.tokens.bypass", "Staff"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (node, tier) in NewGrants)
            {
                foreach (var group in SeedCommandPermissionNodes.Tiers[tier])
                {
                    migrationBuilder.Sql(SeedCommandPermissionNodes.GrantSql(group, node));
                }
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (node, tier) in NewGrants)
            {
                foreach (var group in SeedCommandPermissionNodes.Tiers[tier])
                {
                    migrationBuilder.Sql(
                        "DELETE g FROM permission_grants g INNER JOIN permission_groups pg ON pg.Id = g.HolderId " +
                        $"WHERE pg.Name = '{group}' AND g.Node = '{node}' AND g.Value = 1 AND g.ExpiresAt IS NULL;");
                }
            }
        }
    }
}

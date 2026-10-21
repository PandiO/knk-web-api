using knkwebapi_v2.Attributes;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-212: AddInvitations is additive (two new tables, four nullable users columns) and grandfathers
/// every active, non-deleted account as admitted after the column exists (decision I3), so the
/// admission gate (KNG-214) never stops an existing player. SeedInvitationStaffNode grants
/// knk.admin.invites to Admin, idempotently.
/// </summary>
public class AddInvitationsMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddInvitations().UpOperations;

    [Fact]
    public void AddsFourNullableUsersColumns_AndTheTwoTables()
    {
        var columns = Up().OfType<AddColumnOperation>().Where(c => c.Table == "users").ToList();

        Assert.Equal(new[] { "AdmittedAt", "EmailVerifiedAt", "InvitedByUserId", "InvitedViaInvitationId" },
            columns.Select(c => c.Name).OrderBy(n => n));
        Assert.All(columns, c => Assert.True(c.IsNullable));
        Assert.Equal(new[] { "invitation_redemptions", "invitations" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n));
        var invitations = Up().OfType<CreateTableOperation>().Single(t => t.Name == "invitations");
        Assert.Contains(invitations.Columns, c => c.Name == "CodeHash" && c.MaxLength == 64 && !c.IsNullable);
        Assert.DoesNotContain(invitations.Columns, c => c.Name is "Code" or "Token");
    }

    [Fact]
    public void GrandfathersActiveAccounts_AfterTheColumnExists()
    {
        var operations = Up().ToList();
        var columnIndex = operations.FindIndex(o => o is AddColumnOperation { Name: "AdmittedAt" });
        var backfillIndex = operations.FindIndex(o => o is SqlOperation s && s.Sql == AddInvitations.GrandfatherSql);

        Assert.True(columnIndex >= 0);
        Assert.True(backfillIndex > columnIndex, "the backfill must run after the column exists");
        Assert.Equal("UPDATE users SET AdmittedAt = CreatedAt WHERE IsActive = 1 AND DeletedAt IS NULL AND AdmittedAt IS NULL;",
            AddInvitations.GrandfatherSql);
    }

    [Fact]
    public void HasAUniqueCodeHash_AndDropsNothing()
    {
        var operations = Up();
        Assert.Contains(operations.OfType<CreateIndexOperation>(), i => i.Table == "invitations" && i.IsUnique && i.Columns.SequenceEqual(new[] { "CodeHash" }));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), i => i.Table == "invitations" && i.Columns.SequenceEqual(new[] { "Kind", "ExpiresAt" }));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), i => i.Table == "invitations" && i.Columns.SequenceEqual(new[] { "Email" }));
        Assert.DoesNotContain(operations, o => o is DropColumnOperation or DropTableOperation or DropIndexOperation or AlterColumnOperation);
        Assert.All(operations.OfType<SqlOperation>(), s => Assert.StartsWith("UPDATE users SET AdmittedAt", s.Sql));
    }

    [Fact]
    public void SeedGrantsTheInvitesNodeToAdmin_Idempotently_AfterAddInvitations()
    {
        var up = Assert.Single(new SeedInvitationStaffNode().UpOperations.Cast<SqlOperation>());

        Assert.Equal(StaffPermissions.ManageInvites, SeedInvitationStaffNode.Node);
        Assert.Contains("pg.Name = 'Admin'", up.Sql);
        Assert.Contains("'knk.admin.invites'", up.Sql);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM permission_grants g", up.Sql);
        Assert.Single(new SeedInvitationStaffNode().DownOperations);
        Assert.True(string.CompareOrdinal("20261021120500_SeedInvitationStaffNode", "20261021120136_AddInvitations") > 0);
    }
}

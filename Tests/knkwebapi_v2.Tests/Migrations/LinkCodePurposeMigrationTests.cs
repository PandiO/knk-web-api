using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-208: the LinkCodePurpose migration is additive (three nullable-or-defaulted columns and an
/// index) and backfills Purpose=PasswordReset for the 64-character reset-token hashes before the
/// repository starts filtering on Purpose, so a pending reset link survives the deploy.
/// </summary>
public class LinkCodePurposeMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new knkwebapi_v2.Migrations.LinkCodePurpose().UpOperations;

    [Fact]
    public void AddsPurposeMinecraftUuidAndMinecraftNameToLinkcodes()
    {
        var columns = Up().OfType<AddColumnOperation>().Where(c => c.Table == "linkcodes").ToList();

        var purpose = Assert.Single(columns, c => c.Name == "Purpose");
        Assert.False(purpose.IsNullable);
        Assert.Equal(0, purpose.DefaultValue);
        Assert.True(Assert.Single(columns, c => c.Name == "MinecraftUuid").IsNullable);
        Assert.True(Assert.Single(columns, c => c.Name == "MinecraftName").IsNullable);
        Assert.Equal(36, Assert.Single(columns, c => c.Name == "MinecraftUuid").MaxLength);
        Assert.Equal(16, Assert.Single(columns, c => c.Name == "MinecraftName").MaxLength);
    }

    [Fact]
    public void BackfillsResetTokensByLength_AfterAddingTheColumn()
    {
        var operations = Up();
        var purposeIndex = operations.ToList().FindIndex(o => o is AddColumnOperation { Name: "Purpose" });
        var backfillIndex = operations.ToList().FindIndex(o => o is SqlOperation s && s.Sql.Contains("CHAR_LENGTH(Code) = 64"));

        Assert.True(purposeIndex >= 0);
        Assert.True(backfillIndex > purposeIndex, "the backfill must run after the column exists");
        var backfill = (SqlOperation)operations[backfillIndex];
        Assert.Contains("UPDATE linkcodes SET Purpose = 1", backfill.Sql);
    }

    [Fact]
    public void AddsTheLookupIndex_AndDropsNothing()
    {
        var operations = Up();
        var index = Assert.Single(operations.OfType<CreateIndexOperation>());
        Assert.Equal(new[] { "Purpose", "Status", "ExpiresAt" }, index.Columns);
        Assert.DoesNotContain(operations, o => o is DropColumnOperation or DropTableOperation or DropIndexOperation);
    }
}

using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// PandiO/knk-workspace#50 (developer decision 2026-11-15): one additive migration for the generic audit subject.
/// Nullable SubjectType/SubjectId, an index for a subject's history, TargetUserId nullable; nothing dropped or rewritten.
/// </summary>
public class AddAuditLogSubjectMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddAuditLogSubject().UpOperations;

    [Fact]
    public void Up_AddsNullableSubjectColumns()
    {
        var columns = Up().OfType<AddColumnOperation>().ToDictionary(c => c.Name);

        Assert.Equal(new[] { "SubjectId", "SubjectType" }, columns.Keys.OrderBy(k => k));
        Assert.All(columns.Values, c => Assert.Equal("audit_log_entries", c.Table));
        Assert.All(columns.Values, c => Assert.True(c.IsNullable));
        Assert.Equal(64, columns["SubjectType"].MaxLength);
    }

    [Fact]
    public void Up_MakesTheTargetOptional_WithoutTouchingItsType()
    {
        var alter = Assert.Single(Up().OfType<AlterColumnOperation>());

        Assert.Equal(("audit_log_entries", "TargetUserId", "int"), (alter.Table, alter.Name, alter.ColumnType));
        Assert.True(alter.IsNullable);
        Assert.False(alter.OldColumn.IsNullable);
    }

    [Fact]
    public void Up_IndexesTheSubjectsHistory()
    {
        var index = Assert.Single(Up().OfType<CreateIndexOperation>());

        Assert.Equal("audit_log_entries", index.Table);
        Assert.Equal(new[] { "SubjectType", "SubjectId", "Timestamp" }, index.Columns);
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Up_IsAdditive()
    {
        Assert.Empty(Up().Where(o => o is DropColumnOperation or DropTableOperation or DropIndexOperation or SqlOperation));
        Assert.Equal(4, Up().Count);
    }
}

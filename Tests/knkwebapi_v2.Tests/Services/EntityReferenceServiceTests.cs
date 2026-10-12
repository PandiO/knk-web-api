using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-162: the "in use" check before the web app deletes what a cancelled form's child forms
/// created. Plain foreign keys and many-to-many join rows both count; entities deleted in the same
/// run (the ignore list) don't.
/// </summary>
public class EntityReferenceServiceTests : IDisposable
{
    private readonly KnKDbContext _ctx =
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public EntityReferenceServiceTests()
    {
        _ctx.Towns.Add(new Town { Id = 1, Name = "Rivia", Description = "", WgRegionId = "town_rivia" });
        var oldQuarter = new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "d_oq", TownId = 1 };
        var oostwijk = new District { Id = 5, Name = "Oostwijk", Description = "", WgRegionId = "d_ow", TownId = 1 };
        _ctx.Districts.AddRange(oldQuarter, oostwijk);
        // Gatestraat was made in a Street form together with Oostwijk; Main is an existing street.
        _ctx.Streets.Add(new Street { Id = 7, Name = "Gatestraat", Districts = { oostwijk } });
        _ctx.Streets.Add(new Street { Id = 1, Name = "Main", Districts = { oldQuarter } });
        _ctx.Structures.Add(new Structure { Id = 3, Name = "Smithy", Description = "", WgRegionId = "s_smithy", StreetId = 1, DistrictId = 2 });
        _ctx.SaveChanges();
    }

    public void Dispose() => _ctx.Dispose();

    private EntityReferenceService Service() => new(_ctx);

    [Fact]
    public async Task ReferencedByAPlainForeignKeyAndALink_IsInUse()
    {
        var result = await Service().GetReferencesAsync("District", "2");

        Assert.NotNull(result);
        Assert.True(result!.InUse);
        Assert.Contains(result.References, r => r.ReferencedBy == "Structure.DistrictId" && r.Count == 1);
        Assert.Contains(result.References, r => r.ReferencedBy == "Street (link)" && r.Count == 1);
    }

    [Fact]
    public async Task EntityTypeNameIsCaseInsensitive_AndTheResultNamesTheClrType()
    {
        var result = await Service().GetReferencesAsync("district", "2");

        Assert.Equal("District", result!.EntityTypeName);
    }

    [Fact]
    public async Task ALinkToAnEntityDeletedInTheSameRun_DoesNotCount()
    {
        // Gatestraat is deleted first (created last); Oostwijk is still pending in the same run.
        var street = await Service().GetReferencesAsync("Street", "7", new[] { "District:5" });
        Assert.False(street!.InUse);

        // Without the ignore list the link to Oostwijk counts.
        var streetAlone = await Service().GetReferencesAsync("Street", "7");
        Assert.True(streetAlone!.InUse);
        Assert.Contains(streetAlone.References, r => r.ReferencedBy == "District (link)");
    }

    [Fact]
    public async Task IgnoreAcceptsCommaSeparatedEntries_AndKeepsOtherReferences()
    {
        var result = await Service().GetReferencesAsync("District", "2", new[] { "Street:1, Town:1" });

        // The link to Main is ignored; the Smithy still uses the district.
        Assert.True(result!.InUse);
        Assert.Single(result.References);
        Assert.Equal("Structure.DistrictId", result.References[0].ReferencedBy);
    }

    [Fact]
    public async Task UnreferencedEntity_IsNotInUse()
    {
        _ctx.Districts.Add(new District { Id = 9, Name = "Empty", Description = "", WgRegionId = "d_empty", TownId = 1 });
        _ctx.SaveChanges();

        var result = await Service().GetReferencesAsync("District", "9");

        Assert.False(result!.InUse);
        Assert.Empty(result.References);
    }

    [Theory]
    [InlineData("NoSuchEntity", "1")]
    [InlineData("District", "not-a-number")]
    [InlineData("", "1")]
    public async Task UnknownTypeOrBadId_ReturnsNull(string type, string id)
    {
        Assert.Null(await Service().GetReferencesAsync(type, id));
    }
}

using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#991: when a domain's drawn region gets its final name, the plugin is told the domain's own type
/// name, the one the startup repair reads (DomainMappingProfile: GetType().Name). A new Crown land, Shop, Warehouse or
/// Production structure region was sent as nothing or as "Structure", so it was set up with the wrong kind until the
/// next repair. The finalize also drops the stored validation results about the domain under every type name along its
/// chain (a Shop form stores them as "Shop").
/// </summary>
public class DomainRegionTypeAtCreationTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? Type, string? Parent)> _renames = new();
    private readonly DomainRegionNameFinalizer _finalizer;

    public DomainRegionTypeAtCreationTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?>((o, n, t, p, _) => _renames.Add((o, n, t, p)))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?, string?>((o, n, t, p, _, _) => _renames.Add((o, n, t, p)))
            .ReturnsAsync(true);
        _finalizer = new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);
    }

    public void Dispose() => _context.Dispose();

    /// <summary>Every concrete Domain subtype in the API.</summary>
    public static IEnumerable<object[]> DomainSubtypes() =>
        typeof(Domain).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t != typeof(Domain) && typeof(Domain).IsAssignableFrom(t))
            .OrderBy(t => t.Name)
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(DomainSubtypes))]
    public void DomainTypeOf_IsTheNameTheRepairReads_ForEveryDomainSubtype(Type type)
    {
        var domain = (Domain)Activator.CreateInstance(type)!;

        // A new subtype needs its own arm in DomainRegionNameFinalizer.DomainTypeOf, or its region is set up as
        // its base type at creation while the repair (the type's class name) sets it up differently.
        Assert.Equal(type.Name, DomainRegionNameFinalizer.DomainTypeOf(domain));
    }

    [Fact]
    public void DomainTypeOf_ABareDomain_SendsNoType()
    {
        // As "Domain" the plugin would set it up as a plain structure; without a type it is left to the repair.
        Assert.Null(DomainRegionNameFinalizer.DomainTypeOf(new Domain()));
    }

    [Fact]
    public void TypeNamesOf_IsTheChainDownToDomain()
    {
        Assert.Equal(new[] { "Shop", "ProductionStructure", "Structure", "Domain" }, DomainRegionNameFinalizer.TypeNamesOf(new Shop()));
        Assert.Equal(new[] { "CrownLand", "Domain" }, DomainRegionNameFinalizer.TypeNamesOf(new CrownLand()));
        Assert.Equal(new[] { "Domain" }, DomainRegionNameFinalizer.TypeNamesOf(new Domain()));
    }

    private async Task SeedTownAndDistrictAsync()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "domain_1", WorldName = "world" });
        _context.Districts.Add(new District { Id = 10, Name = "Market", Description = "d", WgRegionId = "domain_10", TownId = 1, WorldName = "world" });
        await _context.SaveChangesAsync();
    }

    public static IEnumerable<object[]> StructureSubtypes() => new[]
    {
        new object[] { new Shop { Id = 20, Name = "Bakery", Description = "s" }, "Shop" },
        new object[] { new Warehouse { Id = 20, Name = "Store", Description = "w" }, "Warehouse" },
        new object[] { new ProductionStructure { Id = 20, Name = "Mill", Description = "p" }, "ProductionStructure" },
    };

    [Theory]
    [MemberData(nameof(StructureSubtypes))]
    public async Task ANewStructureSubtype_IsRenamedWithItsOwnType_UnderItsDistrict(Structure structure, string type)
    {
        await SeedTownAndDistrictAsync();
        structure.WgRegionId = "tempregion_worldtask_7";
        structure.DistrictId = 10;
        structure.TownId = 1;
        structure.WorldName = "world";
        _context.Add(structure);
        await _context.SaveChangesAsync();

        Assert.True(await _finalizer.FinalizeAsync(structure));

        Assert.Equal(("tempregion_worldtask_7", "domain_20", (string?)type, (string?)"domain_10"), Assert.Single(_renames));
    }

    [Fact]
    public async Task ANewCrownLand_IsRenamedAsCrownLand_WithoutAParent()
    {
        var crownLand = new CrownLand { Id = 30, Name = "Royal forest", Description = "c", WgRegionId = "tempregion_worldtask_8", WorldName = "world", KingdomId = 1, Range = 80 };
        _context.CrownLands.Add(crownLand);
        await _context.SaveChangesAsync();

        Assert.True(await _finalizer.FinalizeAsync(crownLand));

        Assert.Equal(("tempregion_worldtask_8", "domain_30", (string?)"CrownLand", (string?)null), Assert.Single(_renames));
    }

    [Fact]
    public async Task AShopsStoredValidationResults_AreDropped_UnderEveryTypeName()
    {
        await SeedTownAndDistrictAsync();
        var shop = new Shop { Id = 20, Name = "Bakery", Description = "s", WgRegionId = "tempregion_worldtask_7", DistrictId = 10, TownId = 1, WorldName = "world" };
        _context.Add(shop);
        _context.FieldValidationRunLogs.AddRange(
            Log(subjectType: "Shop", subjectId: 20),                         // the Shop form's own results
            Log(subjectType: "Location", subjectId: 5, dependency: "Structure:20"),         // a rule that read it as a structure
            Log(subjectType: "Location", subjectId: 6, dependency: "ProductionStructure:20"),
            Log(subjectType: "Shop", subjectId: 21),                         // another shop: kept
            Log(subjectType: "Location", subjectId: 7, dependency: "Town:20")); // another domain with the same id: kept
        await _context.SaveChangesAsync();

        Assert.True(await _finalizer.FinalizeAsync(shop));

        var left = await _context.FieldValidationRunLogs.AsNoTracking().OrderBy(r => r.SubjectId).ToListAsync();
        Assert.Equal(new long[] { 7, 21 }, left.Select(r => r.SubjectId));
    }

    private static FieldValidationRunLog Log(string subjectType, long subjectId, string? dependency = null) => new()
    {
        RuleId = 1,
        RuleHash = "r",
        SubjectType = subjectType,
        SubjectId = subjectId,
        InputHash = "i",
        DependencyKey = dependency,
        IsValid = true,
        Message = "ok",
        ExecutedAt = DateTime.UtcNow
    };
}

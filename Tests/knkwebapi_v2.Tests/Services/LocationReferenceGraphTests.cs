using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.LocationRetention;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-80: "no relation" comes from the EF Core model, not a hand-written list. These tests fail
/// when a foreign key to Location exists that the orphan check does not honour: every FK in the
/// real model is exercised generically, and a probe context adds new FKs of each shape (optional,
/// required, many-to-many) to prove a newly mapped LocationId is picked up without code changes.
/// </summary>
public class LocationReferenceGraphTests
{
    private static DbContextOptions<KnKDbContext> Options(string name) =>
        new DbContextOptionsBuilder<KnKDbContext>()
            // The generic rows below only set the FK; InMemory must not insist on the rest.
            .UseInMemoryDatabase(name, b => b.EnableNullChecks(false))
            .Options;

    [Fact]
    public void Describe_FindsTheKnownRelations()
    {
        using var context = new KnKDbContext(Options($"graph-describe-{Guid.NewGuid()}"));

        var relations = LocationReferenceGraph.Describe(context.Model).Select(r => r.Describe()).ToList();

        // A floor, not the definition: the check uses whatever the model has. If this fails the
        // metadata walk lost relations it used to see.
        Assert.Contains("Domain.LocationId", relations);
        foreach (var column in new[] { "AnchorPointId", "OpenAnchorPointId", "ReferencePoint1Id", "ReferencePoint2Id", "HingeAxisId",
                     "LeftDoorSeedBlockId", "RightDoorSeedBlockId", "InfoDisplayLocationId" })
        {
            Assert.Contains($"GateDoor.{column}", relations);
        }
        Assert.Contains("SiegeScenario.HubLocationId", relations);
        Assert.Contains("SiegeSpawnpoint.LocationId", relations);
        Assert.Contains("SiegeObjective.LocationId", relations);
        Assert.Contains(LocationReferenceGraph.Describe(context.Model), r => r.IsJoinTable && r.Table == "gate_structure_guard_spawn_locations");
    }

    /// <summary>
    /// For every FK to Location in the model, a Location referenced only through that FK is not an
    /// orphan, and the same Location unreferenced is. A new FK is covered by this test the moment
    /// it is mapped; if the check could not anti-join it, this fails.
    /// </summary>
    [Fact]
    public async Task EveryForeignKeyInTheModel_KeepsItsLocationFromBeingAnOrphan()
    {
        var name = $"graph-every-fk-{Guid.NewGuid()}";
        await using var context = new KnKDbContext(Options(name));
        var foreignKeys = LocationReferenceGraph.ForeignKeysTo(context.Model);
        Assert.NotEmpty(foreignKeys);

        var unreferenced = new Location { Name = null };
        context.Locations.Add(unreferenced);
        var referencedBy = new Dictionary<IForeignKey, Location>();
        foreach (var fk in foreignKeys)
        {
            var location = new Location { Name = "Location" };
            context.Locations.Add(location);
            referencedBy[fk] = location;
        }
        await context.SaveChangesAsync();

        var nextKey = 100_000;
        foreach (var (fk, location) in referencedBy)
        {
            AddReferencingRow(context, fk, location.Id, ref nextKey);
        }
        await context.SaveChangesAsync();

        var orphanIds = await context.Locations.Where(LocationReferenceGraph.IsOrphan(context, createdBefore: null)).Select(l => l.Id).ToListAsync();

        Assert.Contains(unreferenced.Id, orphanIds);
        foreach (var (fk, location) in referencedBy)
        {
            Assert.False(orphanIds.Contains(location.Id), $"Location referenced by {fk.DeclaringEntityType.ShortName()}.{fk.Properties[0].Name} was flagged as an orphan.");
        }
    }

    [Fact]
    public async Task NewForeignKeysOfEveryShape_ArePickedUpFromTheModel()
    {
        await using var context = new ProbeContext(Options($"graph-probe-{Guid.NewGuid()}"));
        var relations = LocationReferenceGraph.Describe(context.Model).Select(r => r.Describe()).ToList();
        Assert.Contains("ProbeOptionalHolder.SpotId", relations);
        Assert.Contains("ProbeRequiredHolder.RequiredSpotId", relations);
        Assert.Contains(LocationReferenceGraph.Describe(context.Model), r => r.IsJoinTable && r.Table == "probe_bag_spots");

        var free = new Location();
        var optional = new Location();
        var required = new Location();
        var bagged = new Location();
        context.Locations.AddRange(free, optional, required, bagged);
        await context.SaveChangesAsync();
        context.Add(new ProbeOptionalHolder { SpotId = optional.Id });
        context.Add(new ProbeRequiredHolder { RequiredSpotId = required.Id });
        context.Add(new ProbeBag { Spots = new List<Location> { bagged } });
        await context.SaveChangesAsync();

        var orphanIds = await context.Locations.Where(LocationReferenceGraph.IsOrphan(context, createdBefore: null)).Select(l => l.Id).ToListAsync();

        Assert.Equal(new[] { free.Id }, orphanIds);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Location", true)]
    [InlineData("Spawn of Kardenna", false)]
    [InlineData("Location 2", false)]
    public async Task OnlyTheDefaultName_CountsAsUnnamed(string? name, bool orphan)
    {
        await using var context = new KnKDbContext(Options($"graph-name-{Guid.NewGuid()}"));
        var location = new Location { Name = name };
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var found = await context.Locations.Where(LocationReferenceGraph.IsOrphan(context, createdBefore: null)).AnyAsync(l => l.Id == location.Id);

        Assert.Equal(orphan, found);
    }

    [Fact]
    public async Task GracePeriod_SkipsRecentLocations_AndTreatsAMissingCreatedAtAsOld()
    {
        await using var context = new KnKDbContext(Options($"graph-grace-{Guid.NewGuid()}"));
        var now = DateTime.UtcNow;
        var fresh = new Location { CreatedAt = now.AddDays(-1) };
        var old = new Location { CreatedAt = now.AddDays(-30) };
        var legacy = new Location { CreatedAt = null };
        context.Locations.AddRange(fresh, old, legacy);
        await context.SaveChangesAsync();

        var orphanIds = await context.Locations.Where(LocationReferenceGraph.IsOrphan(context, now.AddDays(-7))).Select(l => l.Id).ToListAsync();

        Assert.DoesNotContain(fresh.Id, orphanIds);
        Assert.Contains(old.Id, orphanIds);
        Assert.Contains(legacy.Id, orphanIds);
    }

    /// <summary>Adds one row of the FK's dependent type whose FK is <paramref name="locationId"/>.</summary>
    private static void AddReferencingRow(KnKDbContext context, IForeignKey fk, int locationId, ref int nextKey)
    {
        var dependent = fk.DeclaringEntityType;
        var concrete = dependent.GetDerivedTypesInclusive().First(t => !t.ClrType.IsAbstract);

        object row;
        if (concrete.HasSharedClrType)
        {
            row = new Dictionary<string, object>();
            context.Set<Dictionary<string, object>>(concrete.Name).Add((Dictionary<string, object>)row);
        }
        else
        {
            row = Activator.CreateInstance(concrete.ClrType, nonPublic: true)!;
            context.Add(row);
        }

        var entry = context.Entry(row);
        foreach (var key in concrete.FindPrimaryKey()!.Properties.Where(p => p.ValueGenerated == ValueGenerated.Never))
        {
            entry.Property(key.Name).CurrentValue = Convert.ChangeType(nextKey++, Nullable.GetUnderlyingType(key.ClrType) ?? key.ClrType);
        }
        var fkProperty = fk.Properties[0];
        entry.Property(fkProperty.Name).CurrentValue = Convert.ChangeType(locationId, Nullable.GetUnderlyingType(fkProperty.ClrType) ?? fkProperty.ClrType);
    }

    // ===== Probe model: new FKs nobody listed anywhere =====

    private sealed class ProbeOptionalHolder
    {
        public int Id { get; set; }
        public int? SpotId { get; set; }
        public Location? Spot { get; set; }
    }

    private sealed class ProbeRequiredHolder
    {
        public int Id { get; set; }
        public int RequiredSpotId { get; set; }
    }

    private sealed class ProbeBag
    {
        public int Id { get; set; }
        public ICollection<Location> Spots { get; set; } = new List<Location>();
    }

    private sealed class ProbeContext : KnKDbContext
    {
        public ProbeContext(DbContextOptions<KnKDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ProbeOptionalHolder>(e =>
            {
                e.ToTable("probe_optional_holders");
                e.HasOne(h => h.Spot).WithMany().HasForeignKey(h => h.SpotId);
            });
            modelBuilder.Entity<ProbeRequiredHolder>(e =>
            {
                e.ToTable("probe_required_holders");
                e.HasOne<Location>().WithMany().HasForeignKey(h => h.RequiredSpotId).IsRequired();
            });
            modelBuilder.Entity<ProbeBag>(e =>
            {
                e.ToTable("probe_bags");
                e.HasMany(b => b.Spots).WithMany().UsingEntity<Dictionary<string, object>>(
                    "probe_bag_spots",
                    r => r.HasOne<Location>().WithMany().HasForeignKey("LocationId"),
                    l => l.HasOne<ProbeBag>().WithMany().HasForeignKey("ProbeBagId"));
            });
        }
    }
}

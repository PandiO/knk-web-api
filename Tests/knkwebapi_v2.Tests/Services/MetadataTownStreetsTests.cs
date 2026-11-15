using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#409: Town metadata keeps Streets as a field (display configurations and paths still see it) and
/// lists the relationship as derived and read-only, with the reason the web app shows as the hint.
/// </summary>
public class MetadataTownStreetsTests
{
    private static MetadataService Service()
    {
        var services = new ServiceCollection();
        var name = "metadata-town-streets-" + Guid.NewGuid();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        return new MetadataService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public void Town_ListsStreets_AsADerivedRelationship_WithTheReason()
    {
        var town = Service().GetEntityMetadata("Town")!;

        var streets = Assert.Single(town.Relationships!, r => r.Property == "streets");
        Assert.Equal("derived", streets.Storage);
        Assert.Equal("collection", streets.Kind);
        Assert.Equal("Street", streets.RelatedType);
        Assert.False(streets.CanLink);
        Assert.False(streets.CanUnlink);
        Assert.Equal("Streets are managed on the district: open a district to add or remove them.", streets.LinkReason);
        Assert.Equal(streets.LinkReason, streets.UnlinkReason);
        Assert.Equal(new[] { "knk.admin.world", "knk.admin.content" }, streets.Nodes);

        // Districts stays an ordinary relationship.
        Assert.Equal("children", Assert.Single(town.Relationships!, r => r.Property == "districts").Storage);
    }

    [Fact]
    public void Town_KeepsStreets_AsARelatedStreetCollectionField()
    {
        var field = Assert.Single(Service().GetEntityMetadata("Town")!.Fields, f => f.FieldName == "Streets");

        Assert.True(field.IsRelatedEntity);
        Assert.Equal("Street", field.RelatedEntityType);
        Assert.Equal("List", field.FieldType);
    }
}

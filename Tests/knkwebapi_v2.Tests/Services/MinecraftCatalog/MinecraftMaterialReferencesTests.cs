using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>
/// KNG-200 §8: a merge must move every reference. This pins the referrer list to the EF model, so a new
/// foreign key to MinecraftMaterialRef fails here until MinecraftMaterialReferences knows it.
/// </summary>
public class MinecraftMaterialReferencesTests
{
    [Fact]
    public void ReferrerList_MatchesEveryForeignKeyToTheMaterialTable()
    {
        using var context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var inModel = context.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(MinecraftMaterialRef))
            .Select(fk => $"{fk.DeclaringEntityType.ClrType.Name}.{string.Join(",", fk.Properties.Select(p => p.Name))}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var known = MinecraftMaterialReferences.All.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(inModel, known);
        Assert.Equal(8, known.Count);
    }
}

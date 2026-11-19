using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Tests.Services.ItemIdentity;

/// <summary>In-memory database for the item identity tests (#141).</summary>
internal sealed class ItemIdentityTestDb
{
    private readonly string _name = Guid.NewGuid().ToString();

    public KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_name).Options);

    public async Task SeedAsync(params object[] rows)
    {
        await using var context = NewContext();
        context.AddRange(rows);
        await context.SaveChangesAsync();
    }

    public static MinecraftMaterialRef Item(int id, string key, bool isItem = true,
        MinecraftCatalogStatus status = MinecraftCatalogStatus.Active, int? maxStack = 64, string? name = null) => new()
    {
        Id = id, NamespaceKey = key, Category = "ITEM", IsItem = isItem, CatalogStatus = status, MaxStackSize = maxStack,
        DisplayName = name,
    };
}

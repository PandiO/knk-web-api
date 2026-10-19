using System.Text.Json.Nodes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Every bundled seed in the repo's Data/DisplayConfigurationSeeds/ ("knk-displayconfig/1") must pass the import
/// dry run on an empty database and import cleanly. The only warnings allowed are the metadata mismatches of
/// elements the seed marks metadataCheck "warn" (they follow the read DTO on purpose).
/// </summary>
public class DisplayConfigurationSeedFilesTests
{
    private static string SeedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "knkwebapi_v2.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Data", "DisplayConfigurationSeeds");
    }

    private static DisplayConfigurationPortabilityService NewService(out ServiceProvider provider)
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        provider = services.BuildServiceProvider();
        return new DisplayConfigurationPortabilityService(
            provider.GetRequiredService<KnKDbContext>(),
            new MetadataService(provider.GetRequiredService<IServiceScopeFactory>()),
            new DisplayConfigurationSeedOptions { Directory = SeedDirectory() });
    }

    [Fact]
    public async Task EverySeed_IsReadable_AndHasAUniqueConfigurationGuid()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var seeds = await service.ListSeedsAsync();
            Assert.NotEmpty(seeds);
            Assert.All(seeds, s => Assert.True(s.Error == null, $"{s.Name}: {s.Error}"));
            Assert.All(seeds, s => Assert.EndsWith(".display", s.Name));
            Assert.Equal(seeds.Count, seeds.Select(s => s.ConfigurationGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public async Task EverySeed_PassesTheDryRun_AndImports_AsThePublishedDefault()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var db = provider.GetRequiredService<KnKDbContext>();
            foreach (var seed in await service.ListSeedsAsync())
            {
                var dry = (await service.ImportSeedAsync(seed.Name, dryRun: true, makeDefault: false, asDraft: false))!;
                Assert.True(dry.Status == FormConfigurationImportStatus.DryRunPassed, $"{seed.Name}: {string.Join(" | ", dry.Errors)}");
                Assert.Equal(0, await db.DisplayConfigurations.CountAsync(c => c.ConfigurationGuid == Guid.Parse(seed.ConfigurationGuid!)));

                var result = (await service.ImportSeedAsync(seed.Name, dryRun: false, makeDefault: false, asDraft: false))!;
                Assert.True(result.Status == FormConfigurationImportStatus.Imported, $"{seed.Name}: {result.Status} {string.Join(" | ", result.Errors)}");
                Assert.Equal(seed.SectionCount, result.SectionCount);
                Assert.Equal(seed.FieldCount, result.FieldCount);

                // Empty database: every seed is its entity's only configuration, so the display page finds it.
                var stored = await db.DisplayConfigurations.AsNoTracking().SingleAsync(c => c.Id == int.Parse(result.DisplayConfigurationId!));
                Assert.True(stored.IsDefault, $"{seed.Name} was not made default on an empty database");
                Assert.False(stored.IsDraft, $"{seed.Name} was imported as a draft");
            }

            var listed = await service.ListSeedsAsync();
            Assert.All(listed, s => Assert.True(s.Imported, $"{s.Name} is not listed as imported"));
        }
    }

    [Fact]
    public async Task EverySeed_WarnsOnlyWhereItSaysMetadataCheckWarn()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            foreach (var seed in await service.ListSeedsAsync())
            {
                var result = (await service.ImportSeedAsync(seed.Name, dryRun: true, makeDefault: false, asDraft: false))!;
                var unexpected = result.Warnings.Where(w => !w.EndsWith(DisplayConfigurationPortabilityService.WarnMarker, StringComparison.Ordinal)).ToList();
                Assert.True(unexpected.Count == 0, $"{seed.Name}: {string.Join(" | ", unexpected)}");
            }
        }
    }

    [Fact]
    public async Task EverySeed_IsImportedOnlyOnce()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            foreach (var seed in await service.ListSeedsAsync())
            {
                var first = (await service.ImportSeedAsync(seed.Name, dryRun: false, makeDefault: false, asDraft: false))!;
                var again = (await service.ImportSeedAsync(seed.Name, dryRun: false, makeDefault: true, asDraft: false))!;
                Assert.Equal(FormConfigurationImportStatus.AlreadyImported, again.Status);
                Assert.Equal(first.DisplayConfigurationId, again.DisplayConfigurationId);
            }
            Assert.Equal((await service.ListSeedsAsync()).Count, await provider.GetRequiredService<KnKDbContext>().DisplayConfigurations.CountAsync());
        }
    }

    [Fact]
    public async Task EverySeed_ExportsBackToTheSameFile()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            foreach (var seed in await service.ListSeedsAsync())
            {
                var result = (await service.ImportSeedAsync(seed.Name, dryRun: false, makeDefault: false, asDraft: false))!;
                var exported = (await service.ExportAsync(int.Parse(result.DisplayConfigurationId!)))!;

                var expected = Comparable(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(SeedDirectory(), seed.Name + ".json")))!);
                var actual = Comparable(JsonNode.Parse(DisplayConfigurationPortabilityService.Serialize(exported))!);
                Assert.True(JsonNode.DeepEquals(expected, actual),
                    $"{seed.Name}: the export differs from the seed.{Environment.NewLine}Seed:{Environment.NewLine}{expected.ToJsonString()}{Environment.NewLine}Export:{Environment.NewLine}{actual.ToJsonString()}");
            }
        }
    }

    /// <summary>The seed without what the database does not keep: notes and the import-only metadataCheck.</summary>
    private static JsonNode Comparable(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("notes");
            obj.Remove("metadataCheck");
            foreach (var (_, child) in obj.ToList())
                if (child != null) Comparable(child);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child != null) Comparable(child);
        }
        return node;
    }

    [Fact]
    public async Task MakeDefault_ReplacesTheEntitysDefault_AndAsDraft_KeepsItOffThePage()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var db = provider.GetRequiredService<KnKDbContext>();
            var old = new DisplayConfiguration { Name = "User - old", EntityTypeName = "User", IsDefault = true, IsDraft = false };
            db.DisplayConfigurations.Add(old);
            await db.SaveChangesAsync();

            var kept = (await service.ImportSeedAsync("User.display", dryRun: true, makeDefault: false, asDraft: true))!;
            Assert.False(kept.IsDefault);
            Assert.True(kept.IsDraft);

            var replaced = (await service.ImportSeedAsync("User.display", dryRun: false, makeDefault: true, asDraft: true))!;
            Assert.Equal(FormConfigurationImportStatus.Imported, replaced.Status);
            Assert.True(replaced.IsDefault);
            Assert.Equal(old.Id.ToString(), replaced.ReplacedDefaultId);

            db.ChangeTracker.Clear();
            Assert.False((await db.DisplayConfigurations.SingleAsync(c => c.Id == old.Id)).IsDefault);
            var imported = await db.DisplayConfigurations.SingleAsync(c => c.Id == int.Parse(replaced.DisplayConfigurationId!));
            Assert.True(imported.IsDefault);
            Assert.True(imported.IsDraft);
        }
    }
}

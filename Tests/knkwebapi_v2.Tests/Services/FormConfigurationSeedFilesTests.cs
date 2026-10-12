using knkwebapi_v2.Dtos;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-138/KNG-139: every bundled seed in the repo's Data/FormConfigurationSeeds/ must pass the
/// import dry run on an empty database, and import cleanly - seeds that refer to other seeds
/// (join entries, child forms, parent-form rule dependencies) in dependency order.
/// </summary>
public class FormConfigurationSeedFilesTests
{
    private static string SeedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "knkwebapi_v2.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Data", "FormConfigurationSeeds");
    }

    private static FormConfigurationPortabilityService NewService(out ServiceProvider provider)
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<KnKDbContext>();
        return new FormConfigurationPortabilityService(
            context,
            new FormConfigurationRepository(context),
            new FormTemplateValidationService(),
            new MetadataService(provider.GetRequiredService<IServiceScopeFactory>()),
            new FormConfigurationSeedOptions { Directory = SeedDirectory() });
    }

    [Fact]
    public void SeedFolder_Exists()
    {
        Assert.True(Directory.Exists(SeedDirectory()));
    }

    [Fact]
    public async Task EverySeed_IsReadable_AndHasAUniqueConfigurationGuid()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var seeds = await service.ListSeedsAsync();
            Assert.All(seeds, s => Assert.True(s.Error == null, $"{s.Name}: {s.Error}"));
            Assert.Equal(seeds.Count, seeds.Select(s => s.ConfigurationGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public async Task EverySeed_PassesTheDryRun_AndImports_InDependencyOrder()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var remaining = (await service.ListSeedsAsync()).Select(s => s.Name).ToList();
            var lastErrors = new Dictionary<string, string>();

            // Import whatever resolves; repeat while something new got in (join entries/children before parents).
            bool progress = true;
            while (remaining.Count > 0 && progress)
            {
                progress = false;
                foreach (var name in remaining.ToList())
                {
                    var dry = (await service.ImportSeedAsync(name, dryRun: true, makeDefault: false))!;
                    if (dry.Status != FormConfigurationImportStatus.DryRunPassed)
                    {
                        lastErrors[name] = string.Join(" | ", dry.Errors);
                        continue;
                    }

                    var imported = (await service.ImportSeedAsync(name, dryRun: false, makeDefault: false))!;
                    Assert.True(imported.Status == FormConfigurationImportStatus.Imported, $"{name}: dry run passed but import said {imported.Status}: {string.Join(" | ", imported.Errors)}");
                    remaining.Remove(name);
                    progress = true;
                }
            }

            Assert.True(remaining.Count == 0,
                "Seeds that never passed the dry run: " + string.Join("; ", remaining.Select(n => $"{n}: {lastErrors.GetValueOrDefault(n)}")));
        }
    }
}

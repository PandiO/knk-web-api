using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#71: the display builder's draft, Publish and Duplicate. Detail pages keep showing the published
/// rows while the builder works on a saved copy; Entity setup's older Publish of a draft still works.
/// </summary>
public class DisplayConfigurationDraftServiceTests : IDisposable
{
    private static readonly IMapper Mapper = new MapperConfiguration(cfg => cfg.AddProfile<DisplayMappingProfile>()).CreateMapper();
    private readonly List<ServiceProvider> _providers = new();

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
    }

    private sealed record Db(KnKDbContext Context, DisplayConfigurationService Configs, DisplayConfigurationPortabilityService Portability, DisplayConfigurationDraftService Drafts);

    private Db NewDb()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var context = provider.GetRequiredService<KnKDbContext>();
        var repo = new DisplayConfigurationRepository(context);
        var configs = new DisplayConfigurationService(repo, Mapper);
        var portability = new DisplayConfigurationPortabilityService(context,
            new MetadataService(provider.GetRequiredService<IServiceScopeFactory>()),
            new DisplayConfigurationSeedOptions { Directory = Path.Combine(Path.GetTempPath(), "knk-no-display-seeds-" + Guid.NewGuid().ToString("N")) });
        return new Db(context, configs, portability, new DisplayConfigurationDraftService(context, repo, configs, portability, Mapper));
    }

    private static PortableDisplayConfigurationDto File(string name) => new()
    {
        Format = PortableDisplayConfigurationDto.CurrentFormat,
        ConfigurationGuid = Guid.NewGuid().ToString(),
        EntityTypeName = "Category",
        Name = name,
        Sections = new()
        {
            new PortableDisplaySectionDto
            {
                SectionGuid = Guid.NewGuid().ToString(),
                SectionName = "General",
                Fields = new() { new PortableDisplayFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "Name", Label = "Name", FieldType = "String" } }
            }
        }
    };

    /// <summary>A published display configuration as it exists today (an import, published, the default).</summary>
    private static async Task<int> PublishedAsync(Db db, string name = "Category page")
    {
        var result = await db.Portability.ImportAsync(File(name), dryRun: false, makeDefault: true, asDraft: false);
        Assert.Equal(FormConfigurationImportStatus.Imported, result.Status);
        return int.Parse(result.DisplayConfigurationId!);
    }

    private static DisplayConfigurationDto NewDraft(string name, bool isDefault) => new()
    {
        EntityTypeName = "Category",
        Name = name,
        IsDefault = isDefault,
        Sections = new()
        {
            new DisplaySectionDto
            {
                Id = "temp-1",
                SectionGuid = Guid.NewGuid().ToString(),
                SectionName = "Overview",
                Fields = new() { new DisplayFieldDto { Id = "temp-2", FieldGuid = Guid.NewGuid().ToString(), FieldName = "Name", Label = "Name", FieldType = "String" } }
            }
        }
    };

    [Fact]
    public async Task SaveDraft_KeepsThePublishedVersion_ForDetailPages()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Sections[0].SectionName = "Summary";

        var state = await db.Drafts.SaveDraftAsync(id, draft);

        Assert.True(state.IsPublished);
        Assert.True(state.HasUnpublishedChanges);
        Assert.Equal("Summary", state.Configuration.Sections[0].SectionName);
        var page = await db.Configs.GetDefaultByEntityTypeNameAsync("Category", includeDrafts: false);
        Assert.Equal("General", page!.Sections.Single().SectionName);
        Assert.True(page.HasUnpublishedChanges);
    }

    [Fact]
    public async Task Publish_AppliesTheCopy_AndKeepsTheDefault()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Sections[0].SectionName = "Summary";
        await db.Drafts.SaveDraftAsync(id, draft);

        var published = await db.Drafts.PublishAsync(id);

        Assert.Equal("Summary", published.Sections.Single().SectionName);
        Assert.True(published.IsDefault);
        Assert.False(published.IsDraft);
        Assert.False(published.HasUnpublishedChanges);
        Assert.NotNull(published.PublishedAt);
    }

    [Fact]
    public async Task NewDraft_IsNotADetailPage_UntilPublished()
    {
        var db = NewDb();
        var oldId = await PublishedAsync(db, "Old page");

        var state = await db.Drafts.CreateDraftAsync(NewDraft("New page", isDefault: true));
        var row = await db.Context.DisplayConfigurations.Include(c => c.Sections).AsNoTracking()
            .SingleAsync(c => c.Id == int.Parse(state.Configuration.Id!));

        Assert.False(state.IsPublished);
        Assert.True(state.Configuration.IsDefault); // the request, applied on Publish
        Assert.Equal("Old page", state.CurrentDefaultName);
        Assert.True(row.IsDraft);
        Assert.False(row.IsDefault);
        Assert.Empty(row.Sections);
        Assert.Equal(oldId.ToString(), (await db.Configs.GetDefaultByEntityTypeNameAsync("Category"))!.Id);
        // Publishing a brand-new copy (new sections) runs on MySQL: ConfigurationDraftsMySqlTests.
    }

    [Fact]
    public async Task CopyPublishedAsDefault_ReplacesTheOldDefault()
    {
        var db = NewDb();
        var oldId = await PublishedAsync(db, "Old page");
        var copy = await db.Drafts.DuplicateAsync(oldId, "New page");
        var id = int.Parse(copy.Configuration.Id!);
        copy.Configuration.IsDefault = true;
        await db.Drafts.SaveDraftAsync(id, copy.Configuration);

        var published = await db.Drafts.PublishAsync(id);

        Assert.True(published.IsDefault);
        Assert.False(published.IsDraft);
        Assert.False((await db.Configs.GetByIdAsync(oldId))!.IsDefault);
        Assert.Equal("New page", (await db.Configs.GetDefaultByEntityTypeNameAsync("Category"))!.Name);
    }

    [Fact]
    public async Task OlderDraft_WithoutACopy_StillPublishes_AsEntitySetupDidBefore()
    {
        var db = NewDb();
        var result = await db.Portability.ImportAsync(File("Draft page"), dryRun: false, makeDefault: false, asDraft: true);
        var id = int.Parse(result.DisplayConfigurationId!);

        var published = await db.Drafts.PublishAsync(id);

        Assert.False(published.IsDraft);
        Assert.Equal("General", published.Sections.Single().SectionName);
    }

    [Fact]
    public async Task Publish_OfAPublishedPageWithoutChanges_IsRefused()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.PublishAsync(id));
        Assert.Contains("no unpublished changes", error.Message);
    }

    [Fact]
    public async Task Discard_GoesBack_AndANewDraftCant()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Sections[0].SectionName = "Summary";
        await db.Drafts.SaveDraftAsync(id, draft);

        Assert.Equal("General", (await db.Drafts.DiscardDraftAsync(id)).Configuration.Sections[0].SectionName);

        var fresh = await db.Drafts.CreateDraftAsync(NewDraft("New page", false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.DiscardDraftAsync(int.Parse(fresh.Configuration.Id!)));
    }

    [Fact]
    public async Task Duplicate_IsANewDraft_NeverTheDefault()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var source = await db.Context.DisplayConfigurations.AsNoTracking().SingleAsync(c => c.Id == id);

        var copy = await db.Drafts.DuplicateAsync(id, null);
        var row = await db.Context.DisplayConfigurations.Include(c => c.Sections).AsNoTracking()
            .SingleAsync(c => c.Id == int.Parse(copy.Configuration.Id!));

        Assert.Equal("Category page (copy)", row.Name);
        Assert.True(row.IsDraft);
        Assert.False(row.IsDefault);
        Assert.NotEqual(source.ConfigurationGuid, row.ConfigurationGuid);
        Assert.Equal("General", row.Sections.Single().SectionName);
        Assert.Equal(id.ToString(), (await db.Configs.GetDefaultByEntityTypeNameAsync("Category"))!.Id);
    }

    [Fact]
    public async Task SyncDraftDefault_FollowsEntitySetup()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        await db.Drafts.SaveDraftAsync(id, (await db.Drafts.GetBuilderStateAsync(id))!.Configuration);

        await db.Drafts.SyncDraftDefaultAsync(id, false);

        Assert.False((await db.Drafts.GetBuilderStateAsync(id))!.Configuration.IsDefault);
    }
}

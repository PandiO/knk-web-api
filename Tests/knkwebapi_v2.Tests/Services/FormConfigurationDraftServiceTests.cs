using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#71: the form builder's draft, Publish and Duplicate. The rows stay the published version the
/// form wizard reads until Publish; configurations from before #71 count as published.
/// </summary>
public class FormConfigurationDraftServiceTests : IDisposable
{
    private static readonly IMapper Mapper = new MapperConfiguration(cfg => cfg.AddProfile<FormMappingProfile>()).CreateMapper();
    private readonly List<ServiceProvider> _providers = new();

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
    }

    private sealed record Db(KnKDbContext Context, FormConfigurationService Configs, FormConfigurationDraftService Drafts);

    private Db NewDb()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var context = provider.GetRequiredService<KnKDbContext>();
        var repo = new FormConfigurationRepository(context);
        var metadata = new MetadataService(provider.GetRequiredService<IServiceScopeFactory>());
        var validation = new FormTemplateValidationService();
        var configs = new FormConfigurationService(repo, Mapper, validation, metadata);
        var portability = new FormConfigurationPortabilityService(context, repo, validation, metadata,
            new FormConfigurationSeedOptions { Directory = Path.GetTempPath() });
        return new Db(context, configs, new FormConfigurationDraftService(context, repo, configs, portability, Mapper));
    }

    private static FormFieldDto Field(string name, string label, int order = 0) => new()
    {
        FieldName = name,
        Label = label,
        FieldType = FieldType.String,
        Order = order,
        IsRequired = true
    };

    private static FormConfigurationDto Config(string name, bool isDefault, params string[] stepNames) => new()
    {
        EntityTypeName = "Category",
        ConfigurationName = name,
        IsDefault = isDefault,
        Steps = stepNames.Select((s, i) => new FormStepDto { StepName = s, Order = i, Fields = { Field("Name", "Name") } }).ToList()
    };

    /// <summary>A configuration as it exists today: created the pre-#71 way (published).</summary>
    private static async Task<int> PublishedAsync(Db db, string name = "Category form", bool isDefault = true, params string[] steps) =>
        int.Parse((await db.Configs.CreateAsync(Config(name, isDefault, steps.Length > 0 ? steps : new[] { "General" }))).Id!);

    [Fact]
    public async Task ExistingConfiguration_IsPublished_WithNothingUnpublished()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);

        var state = (await db.Drafts.GetBuilderStateAsync(id))!;

        Assert.True(state.IsPublished);
        Assert.False(state.HasUnpublishedChanges);
        Assert.Equal("General", state.Configuration.Steps.Single().StepName);
        Assert.Equal("General", state.Published!.Steps.Single().StepName);
        Assert.False(state.Configuration.IsDraft);
    }

    [Fact]
    public async Task SaveDraft_KeepsThePublishedRows_ForTheWizard()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Steps[0].StepName = "Basics";
        draft.Steps.Add(new FormStepDto { StepName = "More", Order = 1, Fields = { Field("Name", "Name again") } });

        var state = await db.Drafts.SaveDraftAsync(id, draft);

        Assert.True(state.HasUnpublishedChanges);
        Assert.Equal(new[] { "Basics", "More" }, state.Configuration.Steps.Select(s => s.StepName));
        Assert.Equal("General", state.Published!.Steps.Single().StepName);
        // What the wizard reads: the default and the list, still the published version.
        var wizard = await db.Configs.GetDefaultByEntityTypeNameAsync("Category");
        Assert.Equal("General", wizard.Steps.Single().StepName);
        Assert.True((await db.Configs.GetByIdAsync(id))!.HasUnpublishedChanges);
    }

    [Fact]
    public async Task Publish_AppliesTheCopy_AndClearsIt()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Steps[0].StepName = "Basics";
        draft.Description = "Now with a description";
        await db.Drafts.SaveDraftAsync(id, draft);

        var published = await db.Drafts.PublishAsync(id);

        Assert.Equal("Basics", published.Steps.Single().StepName);
        Assert.True(published.IsDefault);
        Assert.False(published.HasUnpublishedChanges);
        Assert.NotNull(published.PublishedAt);
        var row = await db.Context.FormConfigurations.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Null(row.DraftJson);
        Assert.Equal("Now with a description", row.Description);
        Assert.Equal("Basics", (await db.Configs.GetDefaultByEntityTypeNameAsync("Category")).Steps.Single().StepName);
    }

    [Fact]
    public async Task Publish_RefusedByTheSaveChecks_ChangesNothing()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Steps[0].Fields.Add(Field("NoSuchProperty", "Ghost", 1));
        await db.Drafts.SaveDraftAsync(id, draft);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.PublishAsync(id));

        Assert.StartsWith("Can't publish:", error.Message);
        var state = (await db.Drafts.GetBuilderStateAsync(id))!;
        Assert.True(state.HasUnpublishedChanges);
        Assert.Single(state.Published!.Steps.Single().Fields);
    }

    [Fact]
    public async Task Publish_WithNothingUnpublished_IsRefused()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.PublishAsync(id));
        Assert.Contains("no unpublished changes", error.Message);
    }

    [Fact]
    public async Task CreateDraft_IsNeverOfferedToTheWizard_UntilPublished()
    {
        var db = NewDb();
        var draft = Config("New form", isDefault: true, "Step 1");

        var state = await db.Drafts.CreateDraftAsync(draft);
        var id = int.Parse(state.Configuration.Id!);

        Assert.False(state.IsPublished);
        Assert.Null(state.Published);
        Assert.True(state.Configuration.IsDefault); // the request, applied on Publish
        var row = await db.Context.FormConfigurations.Include(c => c.Steps).AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.True(row.IsDraft);
        Assert.False(row.IsDefault);
        Assert.Empty(row.Steps);
        Assert.Empty(await db.Configs.GetEntityTypeNamesAsync());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => db.Configs.GetDefaultByEntityTypeNameAsync("Category"));
        Assert.True(await db.Drafts.IsNeverPublishedAsync(id));
    }

    [Fact]
    public async Task PublishAsDefault_ReplacesTheEntitysDefault()
    {
        var db = NewDb();
        var oldId = await PublishedAsync(db, "Old form");
        var state = await db.Drafts.CreateDraftAsync(Config("New form", isDefault: true, "Step 1"));
        var id = int.Parse(state.Configuration.Id!);
        Assert.Equal(oldId.ToString(), state.CurrentDefaultId);
        Assert.Equal("Old form", state.CurrentDefaultName);

        var published = await db.Drafts.PublishAsync(id);

        Assert.True(published.IsDefault);
        Assert.False(published.IsDraft);
        Assert.False((await db.Configs.GetByIdAsync(oldId))!.IsDefault);
        Assert.Equal("New form", (await db.Configs.GetDefaultByEntityTypeNameAsync("Category")).ConfigurationName);
        Assert.Equal(new[] { "Category" }, await db.Configs.GetEntityTypeNamesAsync());
    }

    [Fact]
    public async Task PublishWithoutDefault_UnsetsTheDefault_AsTheOldSaveDid()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.IsDefault = false;
        await db.Drafts.SaveDraftAsync(id, draft);

        Assert.False((await db.Drafts.PublishAsync(id)).IsDefault);
    }

    [Fact]
    public async Task Discard_GoesBackToThePublishedVersion()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Steps[0].StepName = "Changed";
        await db.Drafts.SaveDraftAsync(id, draft);

        var state = await db.Drafts.DiscardDraftAsync(id);

        Assert.False(state.HasUnpublishedChanges);
        Assert.Equal("General", state.Configuration.Steps.Single().StepName);
    }

    [Fact]
    public async Task Discard_OfANeverPublishedConfiguration_IsRefused()
    {
        var db = NewDb();
        var state = await db.Drafts.CreateDraftAsync(Config("New form", false, "Step 1"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.DiscardDraftAsync(int.Parse(state.Configuration.Id!)));
        Assert.Contains("never published", error.Message);
    }

    [Fact]
    public async Task SaveDraft_CannotChangeTheEntity_OrDropTheName()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;

        draft.EntityTypeName = "Tag";
        await Assert.ThrowsAsync<ArgumentException>(() => db.Drafts.SaveDraftAsync(id, draft));
        draft.EntityTypeName = "Category";
        draft.ConfigurationName = "  ";
        await Assert.ThrowsAsync<ArgumentException>(() => db.Drafts.SaveDraftAsync(id, draft));
    }

    [Fact]
    public async Task SaveDraft_OfANeverPublishedConfiguration_RenamesItsRow()
    {
        var db = NewDb();
        var state = await db.Drafts.CreateDraftAsync(Config("New form", false, "Step 1"));
        var id = int.Parse(state.Configuration.Id!);
        state.Configuration.ConfigurationName = "Renamed";

        await db.Drafts.SaveDraftAsync(id, state.Configuration);

        Assert.Equal("Renamed", (await db.Context.FormConfigurations.AsNoTracking().SingleAsync(c => c.Id == id)).Name);
    }

    [Fact]
    public async Task Duplicate_CopiesThePublishedVersion_AsANewDraft()
    {
        var db = NewDb();
        var id = await PublishedAsync(db, "Category form", true, "General", "Extra");
        var source = await db.Context.FormConfigurations.Include(c => c.Steps).AsNoTracking().SingleAsync(c => c.Id == id);

        var copy = await db.Drafts.DuplicateAsync(id, null);
        var copyId = int.Parse(copy.Configuration.Id!);

        Assert.Equal("Category form (copy)", copy.Configuration.ConfigurationName);
        Assert.False(copy.IsPublished);
        var row = await db.Context.FormConfigurations.Include(c => c.Steps).AsNoTracking().SingleAsync(c => c.Id == copyId);
        Assert.True(row.IsDraft);
        Assert.False(row.IsDefault);
        Assert.NotEqual(source.ConfigurationGuid, row.ConfigurationGuid);
        Assert.Equal(new[] { "General", "Extra" }, copy.Configuration.Steps.Select(s => s.StepName));
        Assert.Empty(row.Steps.Select(s => s.StepGuid).Intersect(source.Steps.Select(s => s.StepGuid)));
        // The source and the wizard are untouched.
        Assert.True((await db.Configs.GetByIdAsync(id))!.IsDefault);
        Assert.Equal(id.ToString(), (await db.Configs.GetDefaultByEntityTypeNameAsync("Category")).Id);
    }

    [Fact]
    public async Task Duplicate_ThenPublish_GivesASecondWorkingForm()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var copy = await db.Drafts.DuplicateAsync(id, "  Second form ");
        var copyId = int.Parse(copy.Configuration.Id!);
        Assert.Equal("Second form", copy.Configuration.ConfigurationName);

        var published = await db.Drafts.PublishAsync(copyId);

        Assert.False(published.IsDraft);
        Assert.False(published.IsDefault);
        Assert.Equal(2, (await db.Configs.GetAllByEntityTypeNameAsync("Category")).Count());
    }

    [Fact]
    public async Task Duplicate_OfAConfigurationWithoutAPublishedVersion_IsRefused()
    {
        var db = NewDb();
        var state = await db.Drafts.CreateDraftAsync(Config("New form", false, "Step 1"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.DuplicateAsync(int.Parse(state.Configuration.Id!), null));
        Assert.Contains("Publish it first", error.Message);
    }

    [Fact]
    public async Task SyncDraftDefault_FollowsAnEditOutsideTheBuilder()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        await db.Drafts.SaveDraftAsync(id, draft);

        await db.Drafts.SyncDraftDefaultAsync(id, false);

        Assert.False((await db.Drafts.GetBuilderStateAsync(id))!.Configuration.IsDefault);
    }

    [Fact]
    public async Task OpenWizardDrafts_AreCounted_ForThePublishQuestion()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        db.Context.FormSubmissionProgresses.AddRange(
            new FormSubmissionProgress { FormConfigurationId = id, UserId = 1, Status = "InProgress" },
            new FormSubmissionProgress { FormConfigurationId = id, UserId = 1, Status = "Paused" },
            new FormSubmissionProgress { FormConfigurationId = id, UserId = 1, Status = "Completed" });
        await db.Context.SaveChangesAsync();

        Assert.Equal(2, (await db.Drafts.GetBuilderStateAsync(id))!.OpenDraftCount);
    }
}

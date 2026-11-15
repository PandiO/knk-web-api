using System.Reflection;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#291: field validation rules are part of the form builder's draft (keyed by field GUID) and are
/// applied on Publish; the form wizard keeps the published rules until then. The add-from-template endpoints, which
/// wrote straight into the published rows, are gone.
/// </summary>
public class FormConfigurationDraftRulesTests : IDisposable
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

    private static FormFieldDto Field(string label, int order = 0) => new()
    {
        FieldName = "Name",
        Label = label,
        FieldType = FieldType.String,
        Order = order,
        IsRequired = true
    };

    /// <summary>A published configuration with one step and the fields "Town" and "Location".</summary>
    private static async Task<int> PublishedAsync(Db db)
    {
        var config = new FormConfigurationDto
        {
            EntityTypeName = "Category",
            ConfigurationName = "Category form",
            IsDefault = true,
            Steps = { new FormStepDto { StepName = "General", Order = 0, Fields = { Field("Town"), Field("Location", 1) } } }
        };
        return int.Parse((await db.Configs.CreateAsync(config)).Id!);
    }

    private static async Task<FormField> PublishedFieldAsync(Db db, int configId, string label) =>
        await db.Context.FormFields.AsNoTracking().SingleAsync(f => f.FormStep!.FormConfigurationId == configId && f.Label == label);

    private static async Task<int> PublishedRuleAsync(Db db, int fieldId, int? dependsOnId = null, string message = "Outside the town")
    {
        var rule = new FieldValidationRule
        {
            FormFieldId = fieldId,
            DependsOnFieldId = dependsOnId,
            ValidationType = "LocationInsideRegion",
            DependencyPath = dependsOnId.HasValue ? "WgRegionId" : null,
            ConfigJson = "{}",
            ErrorMessage = message,
            IsBlocking = true
        };
        db.Context.FieldValidationRules.Add(rule);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return rule.Id;
    }

    private static Task<List<FieldValidationRule>> RulesInDbAsync(Db db) =>
        db.Context.FieldValidationRules.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

    private static FormDraftValidationRuleDto NewRule(string fieldGuid, string? dependsOnGuid = null, string message = "Draft rule") => new()
    {
        FieldGuid = fieldGuid,
        DependsOnFieldGuid = dependsOnGuid,
        ValidationType = "LocationInsideRegion",
        DependencyPath = dependsOnGuid != null ? "WgRegionId" : null,
        ConfigJson = "{}",
        ErrorMessage = message,
        IsBlocking = true
    };

    [Fact]
    public async Task BuilderState_CarriesThePublishedRules_KeyedByFieldGuid()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var town = await PublishedFieldAsync(db, id, "Town");
        var location = await PublishedFieldAsync(db, id, "Location");
        var ruleId = await PublishedRuleAsync(db, location.Id, town.Id);

        var state = (await db.Drafts.GetBuilderStateAsync(id))!;

        var rule = Assert.Single(state.Configuration.ValidationRules!);
        Assert.Equal(ruleId, rule.Id);
        Assert.Equal(location.FieldGuid.ToString(), rule.FieldGuid);
        Assert.Equal(town.FieldGuid.ToString(), rule.DependsOnFieldGuid);
        Assert.Null(rule.DependsOnFieldId);
        Assert.Equal(ruleId, Assert.Single(state.Published!.ValidationRules!).Id);
    }

    [Fact]
    public async Task SaveDraft_KeepsRuleChangesInTheDraft_TheWizardKeepsThePublishedRules()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var location = await PublishedFieldAsync(db, id, "Location");
        var ruleId = await PublishedRuleAsync(db, location.Id);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.ValidationRules!.Single().ErrorMessage = "Changed in the draft";
        draft.ValidationRules!.Add(NewRule(location.FieldGuid.ToString()));

        var state = await db.Drafts.SaveDraftAsync(id, draft);

        Assert.Equal(2, state.Configuration.ValidationRules!.Count);
        Assert.Equal("Changed in the draft", state.Configuration.ValidationRules[0].ErrorMessage);
        var published = Assert.Single(await RulesInDbAsync(db));
        Assert.Equal(ruleId, published.Id);
        Assert.Equal("Outside the town", published.ErrorMessage);
    }

    [Fact]
    public async Task Publish_AppliesRuleAddsUpdatesAndDeletes_ByFieldGuid()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var town = await PublishedFieldAsync(db, id, "Town");
        var location = await PublishedFieldAsync(db, id, "Location");
        var keptId = await PublishedRuleAsync(db, location.Id, town.Id);
        var droppedId = await PublishedRuleAsync(db, town.Id, message: "Dropped");

        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        // A field that only exists in the draft, with a rule that depends on a published field.
        var spawnGuid = Guid.NewGuid().ToString();
        var spawn = Field("Spawn", 2);
        spawn.FieldGuid = spawnGuid;
        draft.Steps[0].Fields.Add(spawn);
        draft.ValidationRules!.RemoveAll(r => r.Id == droppedId);
        draft.ValidationRules!.Single(r => r.Id == keptId).ErrorMessage = "Updated message";
        draft.ValidationRules!.Add(NewRule(spawnGuid, town.FieldGuid.ToString(), "Spawn outside the town"));
        await db.Drafts.SaveDraftAsync(id, draft);

        await db.Drafts.PublishAsync(id);

        var rules = await RulesInDbAsync(db);
        Assert.Equal(2, rules.Count);
        Assert.DoesNotContain(rules, r => r.Id == droppedId);
        var kept = rules.Single(r => r.Id == keptId);
        Assert.Equal("Updated message", kept.ErrorMessage);
        Assert.Equal(location.Id, kept.FormFieldId);
        Assert.Equal(town.Id, kept.DependsOnFieldId);
        var spawnField = await PublishedFieldAsync(db, id, "Spawn");
        Assert.Equal(spawnGuid, spawnField.FieldGuid.ToString());
        var added = rules.Single(r => r.Id != keptId);
        Assert.Equal(spawnField.Id, added.FormFieldId);
        Assert.Equal(town.Id, added.DependsOnFieldId);
        Assert.Equal("Spawn outside the town", added.ErrorMessage);
        // The builder opens the published rules again (the draft is gone).
        var state = (await db.Drafts.GetBuilderStateAsync(id))!;
        Assert.False(state.HasUnpublishedChanges);
        Assert.Equal(rules.Select(r => r.Id), state.Configuration.ValidationRules!.Select(r => r.Id!.Value));
    }

    [Fact]
    public async Task Publish_OfADraftSavedWithoutRules_KeepsThePublishedRules()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var location = await PublishedFieldAsync(db, id, "Location");
        var ruleId = await PublishedRuleAsync(db, location.Id);
        // A draft saved before #291 (or by an older web app): no validationRules in DraftJson.
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.ValidationRules = null;
        draft.Steps[0].StepName = "Basics";
        await db.Drafts.SaveDraftAsync(id, draft);
        var row = await db.Context.FormConfigurations.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.DoesNotContain("validationRules", row.DraftJson!);

        var state = (await db.Drafts.GetBuilderStateAsync(id))!;
        Assert.Equal(ruleId, Assert.Single(state.Configuration.ValidationRules!).Id);

        await db.Drafts.PublishAsync(id);

        var rule = Assert.Single(await RulesInDbAsync(db));
        Assert.Equal(ruleId, rule.Id);
        Assert.Equal(location.Id, rule.FormFieldId);
        Assert.Equal("Outside the town", rule.ErrorMessage);
    }

    [Fact]
    public async Task PublishedOnlyForm_KeepsItsRules_ThroughAnotherConfigurationsPublish()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var location = await PublishedFieldAsync(db, id, "Location");
        var ruleId = await PublishedRuleAsync(db, location.Id);
        var otherId = int.Parse((await db.Drafts.CreateDraftAsync(new FormConfigurationDto
        {
            EntityTypeName = "Category",
            ConfigurationName = "Second form",
            Steps = { new FormStepDto { StepName = "Only", Order = 0, Fields = { Field("Other") } } },
            ValidationRules = new List<FormDraftValidationRuleDto>()
        })).Configuration.Id!);

        await db.Drafts.PublishAsync(otherId);

        Assert.Equal(ruleId, Assert.Single(await RulesInDbAsync(db)).Id);
        Assert.Equal(ruleId, Assert.Single((await db.Drafts.GetBuilderStateAsync(id))!.Configuration.ValidationRules!).Id);
    }

    [Fact]
    public async Task Discard_DropsTheRuleChanges()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var location = await PublishedFieldAsync(db, id, "Location");
        var ruleId = await PublishedRuleAsync(db, location.Id);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.ValidationRules!.Clear();
        draft.ValidationRules!.Add(NewRule(location.FieldGuid.ToString(), message: "Never published"));
        await db.Drafts.SaveDraftAsync(id, draft);

        var state = await db.Drafts.DiscardDraftAsync(id);

        var rule = Assert.Single(state.Configuration.ValidationRules!);
        Assert.Equal(ruleId, rule.Id);
        Assert.Equal("Outside the town", rule.ErrorMessage);
        Assert.Equal(ruleId, Assert.Single(await RulesInDbAsync(db)).Id);
    }

    [Fact]
    public async Task Publish_RemovesAFieldARuleDependedOn_WhenTheRuleLetGoOfIt()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var town = await PublishedFieldAsync(db, id, "Town");
        var location = await PublishedFieldAsync(db, id, "Location");
        var ruleId = await PublishedRuleAsync(db, location.Id, town.Id);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Steps[0].Fields.RemoveAll(f => f.Label == "Town");
        var rule = draft.ValidationRules!.Single();
        rule.DependsOnFieldGuid = null;
        rule.DependencyPath = null;
        await db.Drafts.SaveDraftAsync(id, draft);

        await db.Drafts.PublishAsync(id);

        var published = Assert.Single(await RulesInDbAsync(db));
        Assert.Equal(ruleId, published.Id);
        Assert.Null(published.DependsOnFieldId);
        Assert.False(await db.Context.FormFields.AnyAsync(f => f.Id == town.Id));
    }

    [Fact]
    public async Task Publish_RefusesARuleWhoseDependencyIsNoLongerInTheForm_AndChangesNothing()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var town = await PublishedFieldAsync(db, id, "Town");
        var location = await PublishedFieldAsync(db, id, "Location");
        await PublishedRuleAsync(db, location.Id, town.Id);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.Steps[0].Fields.RemoveAll(f => f.Label == "Town");
        await db.Drafts.SaveDraftAsync(id, draft);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.PublishAsync(id));

        Assert.Equal("Can't publish: the LocationInsideRegion rule on 'Location' depends on a field that is no longer in the form. Edit or delete the rule.", error.Message);
        Assert.True(await db.Context.FormFields.AnyAsync(f => f.Id == town.Id));
        Assert.Equal(town.Id, Assert.Single(await RulesInDbAsync(db)).DependsOnFieldId);
        Assert.True((await db.Drafts.GetBuilderStateAsync(id))!.HasUnpublishedChanges);
    }

    [Fact]
    public async Task Publish_RefusesARuleOnAFieldThatIsNotInTheForm()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.ValidationRules!.Add(NewRule(Guid.NewGuid().ToString()));
        await db.Drafts.SaveDraftAsync(id, draft);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.PublishAsync(id));

        Assert.Contains("belongs to a field that is no longer in the form", error.Message);
        Assert.Empty(await RulesInDbAsync(db));
    }

    [Fact]
    public async Task Publish_RefusesRulesThatDependOnEachOtherInACircle()
    {
        var db = NewDb();
        var id = await PublishedAsync(db);
        var town = await PublishedFieldAsync(db, id, "Town");
        var location = await PublishedFieldAsync(db, id, "Location");
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        draft.ValidationRules!.Add(NewRule(location.FieldGuid.ToString(), town.FieldGuid.ToString()));
        draft.ValidationRules!.Add(NewRule(town.FieldGuid.ToString(), location.FieldGuid.ToString()));
        await db.Drafts.SaveDraftAsync(id, draft);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Drafts.PublishAsync(id));

        Assert.Contains("depend on each other in a circle", error.Message);
        Assert.Empty(await RulesInDbAsync(db));
    }

    [Fact]
    public async Task Publish_KeepsADependencyOnAnotherFormsField_ById()
    {
        var db = NewDb();
        var parentId = await PublishedAsync(db);
        var parentTown = await PublishedFieldAsync(db, parentId, "Town");
        var id = int.Parse((await db.Drafts.CreateDraftAsync(new FormConfigurationDto
        {
            EntityTypeName = "Category",
            ConfigurationName = "Child form",
            Steps = { new FormStepDto { StepName = "Only", Order = 0, Fields = { Field("Spot") } } }
        })).Configuration.Id!);
        var draft = (await db.Drafts.GetBuilderStateAsync(id))!.Configuration;
        Assert.Empty(draft.ValidationRules!);
        var spotGuid = draft.Steps[0].Fields[0].FieldGuid;
        if (string.IsNullOrEmpty(spotGuid))
        {
            spotGuid = Guid.NewGuid().ToString();
            draft.Steps[0].Fields[0].FieldGuid = spotGuid;
        }
        var rule = NewRule(spotGuid);
        rule.DependsOnFieldId = parentTown.Id;
        rule.DependencyPath = "WgRegionId";
        draft.ValidationRules!.Add(rule);
        await db.Drafts.SaveDraftAsync(id, draft);

        await db.Drafts.PublishAsync(id);

        var spot = await PublishedFieldAsync(db, id, "Spot");
        var published = Assert.Single(await RulesInDbAsync(db));
        Assert.Equal(spot.Id, published.FormFieldId);
        Assert.Equal(parentTown.Id, published.DependsOnFieldId);
        var state = (await db.Drafts.GetBuilderStateAsync(id))!;
        var reopened = Assert.Single(state.Configuration.ValidationRules!);
        Assert.Equal(parentTown.Id, reopened.DependsOnFieldId);
        Assert.Null(reopened.DependsOnFieldGuid);
    }

    [Fact]
    public void AddFromTemplateEndpoints_AreGone()
    {
        var routes = typeof(FormConfigurationsController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => a.Template ?? string.Empty)
            .ToList();

        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, r => r.Contains("add-from-template", StringComparison.OrdinalIgnoreCase));
    }
}

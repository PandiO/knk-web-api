using AutoMapper;
using knkwebapi_v2.Attributes;
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
/// KNG-172: a form configuration that was opened once (a form session and drafts exist) can still
/// be deleted, and a refused delete names only what really stops it. The in-memory provider doesn't
/// enforce Restrict for rows it hasn't loaded, so the delete tests check what the database would
/// otherwise refuse: the sessions are detached and the sub-form drafts removed.
/// </summary>
public class FormConfigurationDeleteTests : IDisposable
{
    private static readonly IMapper Mapper = new MapperConfiguration(cfg => cfg.AddProfile<FormMappingProfile>()).CreateMapper();

    private readonly ServiceProvider _provider;
    private readonly KnKDbContext _ctx;

    public FormConfigurationDeleteTests()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        _provider = services.BuildServiceProvider();
        _ctx = _provider.GetRequiredService<KnKDbContext>();

        _ctx.Users.Add(new User { Id = 1, Username = "pandi" });
        // 10: the configuration being deleted; 20: a sub-form opened from it; 30: another one that uses 10 as a sub-form.
        _ctx.FormConfigurations.AddRange(
            new FormConfiguration { Id = 10, Name = "Gate (imported copy)", EntityTypeName = "GateStructure",
                Steps = { new FormStep { Id = 100, StepName = "Location", Fields = {
                    new FormField { Id = 1000, FieldName = "district", Label = "District" },
                    new FormField { Id = 1001, FieldName = "region", Label = "Region" } } } } },
            new FormConfiguration { Id = 20, Name = "District", EntityTypeName = "District" },
            new FormConfiguration { Id = 30, Name = "Town", EntityTypeName = "Town" });
        // A rule that depends on another field of the same configuration (Restrict in MySQL).
        _ctx.FieldValidationRules.Add(new FieldValidationRule { Id = 1, FormFieldId = 1001, DependsOnFieldId = 1000, ValidationType = "RegionContainment" });
        // A field and a step shown only for some values of a field of the same configuration, like
        // GateDoor's toggles and Rotation step (DisplayCondition.SourceFormFieldId is required, Restrict).
        _ctx.DisplayConditionGroups.AddRange(
            new DisplayConditionGroup { Id = 1, TargetType = DisplayConditionTargetType.FormField, TargetFieldId = 1001,
                Conditions = { new DisplayCondition { Id = 1, SourceFormFieldId = 1000, ValueJson = "true" } } },
            new DisplayConditionGroup { Id = 2, TargetType = DisplayConditionTargetType.FormStep, TargetStepId = 100,
                Conditions = { new DisplayCondition { Id = 2, SourceFormFieldId = 1001, ValueJson = "\"Rotation\"" } } });
        _ctx.WorkflowSessions.AddRange(
            new WorkflowSession { Id = 1, UserId = 1, FormConfigurationId = 10 },
            new WorkflowSession { Id = 2, UserId = 1, FormConfigurationId = 10 },
            new WorkflowSession { Id = 3, UserId = 1, FormConfigurationId = 20 });
        _ctx.FormSubmissionProgresses.AddRange(
            new FormSubmissionProgress { Id = 1, UserId = 1, FormConfigurationId = 10 },
            // A District draft opened from the gate draft, and a draft opened from that one in turn.
            new FormSubmissionProgress { Id = 2, UserId = 1, FormConfigurationId = 20, ParentProgressId = 1 },
            new FormSubmissionProgress { Id = 3, UserId = 1, FormConfigurationId = 20, ParentProgressId = 2 },
            // A District draft of its own: not touched.
            new FormSubmissionProgress { Id = 4, UserId = 1, FormConfigurationId = 20 });
        _ctx.SaveChanges();
        _ctx.ChangeTracker.Clear();
    }

    public void Dispose() => _provider.Dispose();

    private FormConfigurationService Service()
    {
        var metadata = new MetadataService(_provider.GetRequiredService<IServiceScopeFactory>());
        return new FormConfigurationService(new FormConfigurationRepository(_ctx), Mapper, new FormTemplateValidationService(), metadata);
    }

    [Fact]
    public async Task DeletingAnOpenedConfiguration_DetachesItsSessionsAndRemovesItsDrafts()
    {
        await Service().DeleteAsync(10);
        _ctx.ChangeTracker.Clear();

        Assert.False(await _ctx.FormConfigurations.AnyAsync(c => c.Id == 10));
        Assert.False(await _ctx.FormSteps.AnyAsync(s => s.Id == 100));
        Assert.False(await _ctx.FieldValidationRules.AnyAsync());
        Assert.False(await _ctx.DisplayConditionGroups.AnyAsync());
        Assert.False(await _ctx.DisplayConditions.AnyAsync());

        // The sessions keep their history, without the link.
        var sessions = await _ctx.WorkflowSessions.OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(3, sessions.Count);
        Assert.Null(sessions[0].FormConfigurationId);
        Assert.Null(sessions[1].FormConfigurationId);
        Assert.Equal(20, sessions[2].FormConfigurationId);

        // Its draft and the sub-form drafts under it (any depth) are gone; another draft stays.
        var drafts = await _ctx.FormSubmissionProgresses.Select(p => p.Id).ToListAsync();
        Assert.Equal(new[] { 4 }, drafts);
    }

    [Fact]
    public async Task ARefusedDelete_NamesOnlyWhatBlocksIt()
    {
        // Configuration 30 opens 10 as a sub-form: that one really stops the delete.
        _ctx.FormSteps.Add(new FormStep { Id = 300, StepName = "Gate", FormConfigurationId = 30, SubConfigurationId = 10 });
        _ctx.SaveChanges();

        var references = new EntityReferenceService(_ctx);
        var blocking = await references.GetReferencesAsync("FormConfiguration", "10", blockingOnly: true);

        Assert.Equal(
            "This form configuration can't be deleted: it is still used by 1 form step that uses it as a sub-form. Remove or change those first.",
            DeleteConstraintExceptionFilter.DescribeRefusal(blocking));

        // The entity-references endpoint (default) still lists everything, incl. the drafts the web
        // app warns about ("Its 1 saved draft is deleted too.").
        var all = await references.GetReferencesAsync("FormConfiguration", "10");
        Assert.Contains(all!.References, r => r.ReferencedBy == "FormSubmissionProgress.FormConfigurationId" && r.Count == 1);
        Assert.Contains(all.References, r => r.ReferencedBy == "WorkflowSession.FormConfigurationId" && r.Count == 2);
        Assert.Contains(all.References, r => r.ReferencedBy == "FormStep.FormConfigurationId" && r.Count == 1);
    }

    [Fact]
    public async Task WhenNothingBlocks_TheRefusalStaysGeneric()
    {
        var blocking = await new EntityReferenceService(_ctx).GetReferencesAsync("FormConfiguration", "10", blockingOnly: true);

        Assert.NotNull(blocking);
        Assert.Empty(blocking!.References);
        Assert.False(blocking.InUse);
        Assert.Equal("This item is still in use and can't be deleted.", DeleteConstraintExceptionFilter.DescribeRefusal(blocking));
    }

    [Fact]
    public async Task SubFormDrafts_BlockTheirParentDraft()
    {
        // ParentProgressId is Restrict: a draft with sub-form drafts can't go on its own.
        var blocking = await new EntityReferenceService(_ctx).GetReferencesAsync("FormSubmissionProgress", "1", blockingOnly: true);

        Assert.Contains(blocking!.References, r => r.ReferencedBy == "FormSubmissionProgress.ParentProgressId" && r.Count == 1);
    }
}

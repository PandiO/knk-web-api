using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#289: "Show what uses it" for a refused form configuration delete names the form fields and steps
/// that open it as a sub-form, each with the form (and step) it sits in.
/// </summary>
public class ConfigurationReferenceLabelServiceTests : IDisposable
{
    private readonly KnKDbContext _ctx =
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public ConfigurationReferenceLabelServiceTests()
    {
        var address = new FormConfiguration { Id = 4, Name = "Address form", EntityTypeName = "Location" };
        var structure = new FormConfiguration { Id = 1, Name = "Structure form", EntityTypeName = "Structure" };
        var draft = new FormConfiguration { Id = 2, Name = "Town form", EntityTypeName = "Town", IsDraft = true };
        _ctx.FormConfigurations.AddRange(address, structure, draft);

        var location = new FormStep { Id = 10, StepName = "Location", FormConfigurationId = 1 };
        var child = new FormStep { Id = 11, StepName = "Gate", ParentStepId = 10 };
        var townStep = new FormStep { Id = 12, StepName = "Address", FormConfigurationId = 2, SubConfigurationId = 4 };
        var template = new FormStep { Id = 13, StepName = "Address template", IsReusable = true, SubConfigurationId = 4 };
        _ctx.FormSteps.AddRange(location, child, townStep, template);

        _ctx.FormFields.AddRange(
            new FormField { Id = 20, FieldName = "addressId", Label = "Address", FieldType = FieldType.Object, FormStepId = 10, SubConfigurationId = 4 },
            new FormField { Id = 21, FieldName = "gateAddress", Label = "", FieldType = FieldType.Object, FormStepId = 11, SubConfigurationId = 4 },
            new FormField { Id = 22, FieldName = "address", Label = "Address field", FieldType = FieldType.Object, IsReusable = true, SubConfigurationId = 4 });
        _ctx.SaveChanges();
    }

    public void Dispose() => _ctx.Dispose();

    private ConfigurationReferenceLabelService Service() => new(_ctx);

    [Fact]
    public void HandlesFormFieldsAndSteps_Only()
    {
        Assert.True(Service().Handles("FormField"));
        Assert.True(Service().Handles("FormStep"));
        Assert.False(Service().Handles("FormSubmissionProgress"));
        Assert.False(Service().Handles(null));
    }

    [Fact]
    public async Task AField_IsNamedWithItsFormAndStep_AChildStepsFieldWithTheParentsForm()
    {
        var items = await Service().LabelAsync("FormField", new[] { 20, 21 });

        Assert.Equal("Address", items[0].Label);
        Assert.Equal("Field of the form \"Structure form\" (Structure), step \"Location\"", items[0].Detail);
        Assert.Equal("gateAddress", items[1].Label); // no label: the field name
        Assert.Equal("Field of the form \"Structure form\" (Structure), step \"Gate\"", items[1].Detail);
    }

    [Fact]
    public async Task ATemplateField_SaysSo_AndAMissingId_KeepsItsNumber()
    {
        var items = await Service().LabelAsync("FormField", new[] { 22, 99 });

        Assert.Equal("Reusable field template", items[0].Detail);
        Assert.Equal("FormField #99", items[1].Label);
        Assert.Null(items[1].Detail);
    }

    [Fact]
    public async Task AStep_IsNamedWithItsForm_ADraftFormSaysDraft()
    {
        var items = await Service().LabelAsync("FormStep", new[] { 12, 13 });

        Assert.Equal("Address", items[0].Label);
        Assert.Equal("Step of the form \"Town form\" (Town, draft)", items[0].Detail);
        Assert.Equal("Reusable step template", items[1].Detail);
    }

    [Fact]
    public async Task TheReferenceCheck_GivesTheFieldsAndStepsThatBlockAFormConfigurationDelete()
    {
        var groups = await new EntityReferenceService(_ctx).GetReferencingRowsAsync("FormConfiguration", "4", blockingOnly: true);

        var fields = Assert.Single(groups!, g => g.ReferencedBy == "FormField.SubConfigurationId");
        Assert.Equal("FormField", fields.EntityTypeName);
        Assert.Equal(new[] { "20", "21", "22" }, fields.Ids.OrderBy(id => id));
        var steps = Assert.Single(groups!, g => g.ReferencedBy == "FormStep.SubConfigurationId");
        Assert.Equal("FormStep", steps.EntityTypeName);
        Assert.Equal(2, steps.Count);
    }
}

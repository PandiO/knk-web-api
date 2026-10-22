using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Export/import of DisplayConfigurations as "knk-displayconfig/1" files (Data/DisplayConfigurationSeeds/README.md):
/// identity and default rules (I1-I4), structure (S1-S5), element (E1-E6) and metadata (M1-M6) checks, and the
/// export rules for order lists, sub-sections and linked sections.
/// </summary>
public class DisplayConfigurationPortabilityServiceTests : IDisposable
{
    private const string ConfigGuid = "3d0c5a52-6f0e-4b8a-9d7e-1a2b3c4d5e01";
    private const string GeneralGuid = "3d0c5a52-6f0e-4b8a-9d7e-1a2b3c4d5e11";
    private const string SpawnGuid = "3d0c5a52-6f0e-4b8a-9d7e-1a2b3c4d5e12";
    private const string DoorsGuid = "3d0c5a52-6f0e-4b8a-9d7e-1a2b3c4d5e13";

    private readonly List<ServiceProvider> _providers = new();

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
    }

    private (KnKDbContext Db, DisplayConfigurationPortabilityService Service) NewDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var db = provider.GetRequiredService<KnKDbContext>();
        return (db, new DisplayConfigurationPortabilityService(
            db,
            new MetadataService(provider.GetRequiredService<IServiceScopeFactory>()),
            new DisplayConfigurationSeedOptions { Directory = Path.Combine(Path.GetTempPath(), "knk-no-display-seeds-" + Guid.NewGuid().ToString("N")) }));
    }

    private static int _guidCounter;
    private static string NewGuid() => $"5e1d0000-0000-4000-8000-{Interlocked.Increment(ref _guidCounter):D12}";

    private static PortableDisplayFieldDto Field(string? name, string label, string? type = "String", string? rel = null, string? relType = null) => new()
    {
        FieldGuid = NewGuid(),
        FieldName = name,
        Label = label,
        FieldType = type,
        RelatedEntityPropertyName = rel,
        RelatedEntityTypeName = relType
    };

    /// <summary>A clean GateStructure file: a root section, a related single section and a collection.</summary>
    private static PortableDisplayConfigurationDto GateFile() => new()
    {
        Format = PortableDisplayConfigurationDto.CurrentFormat,
        ConfigurationGuid = ConfigGuid,
        EntityTypeName = "GateStructure",
        Name = "GateStructure - Test",
        Description = "A gate.",
        Sections = new()
        {
            new PortableDisplaySectionDto
            {
                SectionGuid = GeneralGuid,
                SectionName = "General",
                Fields = new()
                {
                    new PortableDisplayFieldDto { FieldGuid = NewGuid(), FieldName = "Name", Label = "Name", FieldType = "String", IsEditableInDisplay = true },
                    Field("Description", "Description"),
                    Field("Name", "District", rel: "District", relType: "District"),
                    Field("AllowEntry", "Allow entry", "Boolean")
                }
            },
            new PortableDisplaySectionDto
            {
                SectionGuid = SpawnGuid,
                SectionName = "Spawn point",
                RelatedEntityPropertyName = "Location",
                RelatedEntityTypeName = "Location",
                ActionButtonsConfigJson = "{\"showEditButton\":true}",
                Fields = new() { Field("X", "X", "Decimal"), Field("World", "World") }
            },
            new PortableDisplaySectionDto
            {
                SectionGuid = DoorsGuid,
                SectionName = "Doors",
                RelatedEntityPropertyName = "GateDoors",
                RelatedEntityTypeName = "GateDoor",
                IsCollection = true,
                ActionButtonsConfigJson = "{\"showEditButton\":true}",
                Fields = new()
                {
                    Field("Name", "Door"),
                    new PortableDisplayFieldDto { FieldGuid = NewGuid(), Label = "Health", TemplateText = "${healthCurrent} / ${healthMax}", FieldType = "String" }
                }
            }
        }
    };

    private static PortableDisplaySectionDto Section(PortableDisplayConfigurationDto file, string name) =>
        file.Sections.Single(s => s.SectionName == name);

    private static async Task<DisplayConfigurationImportResultDto> DryRun(DisplayConfigurationPortabilityService service, PortableDisplayConfigurationDto file) =>
        await service.ImportAsync(file, dryRun: true, makeDefault: false, asDraft: false);

    private static void AssertError(DisplayConfigurationImportResultDto result, string expected)
    {
        Assert.Equal(FormConfigurationImportStatus.Invalid, result.Status);
        Assert.True(result.Errors.Any(e => e.Contains(expected, StringComparison.Ordinal)),
            $"Expected an error containing '{expected}', got: {string.Join(" | ", result.Errors)}");
    }

    private static void AssertWarning(DisplayConfigurationImportResultDto result, string expected)
    {
        Assert.True(result.Warnings.Any(w => w.Contains(expected, StringComparison.Ordinal)),
            $"Expected a warning containing '{expected}', got: {string.Join(" | ", result.Warnings)} (errors: {string.Join(" | ", result.Errors)})");
    }

    // ------------------------------------------------------------------ import: identity, default, draft

    [Fact]
    public async Task Import_BuildsTheConfiguration_InFileOrder_PublishedAndDefault()
    {
        var (db, service) = NewDb();
        var file = GateFile();

        var result = await service.ImportAsync(file, dryRun: false, makeDefault: false, asDraft: false);

        Assert.True(result.Status == FormConfigurationImportStatus.Imported, string.Join(" | ", result.Errors));
        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.SectionCount);
        Assert.Equal(8, result.FieldCount);
        Assert.True(result.IsDefault);
        Assert.False(result.IsDraft);

        db.ChangeTracker.Clear();
        var stored = await db.DisplayConfigurations
            .Include(c => c.Sections).ThenInclude(s => s.Fields)
            .SingleAsync(c => c.Id == int.Parse(result.DisplayConfigurationId!));
        Assert.True(stored.IsDefault);
        Assert.False(stored.IsDraft);
        Assert.Equal(Guid.Parse(ConfigGuid), stored.ConfigurationGuid);
        Assert.Equal(new[] { GeneralGuid, SpawnGuid, DoorsGuid }, JsonSerializer.Deserialize<string[]>(stored.SectionOrderJson));

        var general = stored.Sections.Single(s => s.SectionName == "General");
        Assert.Equal(Section(file, "General").Fields.Select(f => f.FieldGuid).ToArray(), JsonSerializer.Deserialize<string[]>(general.FieldOrderJson));
        Assert.True(general.Fields.Single(f => f.Label == "Name").IsEditableInDisplay);
        Assert.Equal("{}", general.ActionButtonsConfigJson);
        Assert.Equal("{\"showEditButton\":true}", stored.Sections.Single(s => s.SectionName == "Spawn point").ActionButtonsConfigJson);
        Assert.True(stored.Sections.Single(s => s.SectionName == "Doors").IsCollection);
    }

    [Fact]
    public async Task DryRun_WritesNothing()
    {
        var (db, service) = NewDb();

        var result = await DryRun(service, GateFile());

        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, result.Status);
        Assert.True(result.DryRun);
        Assert.Null(result.DisplayConfigurationId);
        Assert.Equal(0, await db.DisplayConfigurations.CountAsync());
        Assert.Equal(0, await db.DisplaySections.CountAsync());
    }

    [Fact]
    public async Task Import_NeverOverwrites_ByConfigurationGuid()
    {
        var (db, service) = NewDb();
        var first = await service.ImportAsync(GateFile(), dryRun: false, makeDefault: false, asDraft: false);

        var changed = GateFile();
        changed.Name = "GateStructure - Changed";
        changed.Sections.RemoveAt(2);
        var again = await service.ImportAsync(changed, dryRun: false, makeDefault: true, asDraft: true);

        Assert.Equal(FormConfigurationImportStatus.AlreadyImported, again.Status);
        Assert.Equal(first.DisplayConfigurationId, again.DisplayConfigurationId);
        db.ChangeTracker.Clear();
        var stored = await db.DisplayConfigurations.Include(c => c.Sections).SingleAsync();
        Assert.Equal("GateStructure - Test", stored.Name);
        Assert.Equal(3, stored.Sections.Count);
        Assert.False(stored.IsDraft);
    }

    [Fact]
    public async Task Import_KeepsTheExistingDefault_UnlessMakeDefault()
    {
        var (db, service) = NewDb();
        var old = new DisplayConfiguration { Name = "Old", EntityTypeName = "GateStructure", IsDefault = true, IsDraft = false };
        db.DisplayConfigurations.Add(old);
        await db.SaveChangesAsync();

        var dry = await DryRun(service, GateFile());
        Assert.False(dry.IsDefault);
        Assert.Contains("'Old' stays the default", dry.Message);

        var imported = await service.ImportAsync(GateFile(), dryRun: false, makeDefault: false, asDraft: true);
        Assert.Equal(FormConfigurationImportStatus.Imported, imported.Status);
        Assert.False(imported.IsDefault);
        Assert.Null(imported.ReplacedDefaultId);
        db.ChangeTracker.Clear();
        Assert.True((await db.DisplayConfigurations.SingleAsync(c => c.Id == old.Id)).IsDefault);
        var stored = await db.DisplayConfigurations.SingleAsync(c => c.Id == int.Parse(imported.DisplayConfigurationId!));
        Assert.False(stored.IsDefault);
        Assert.True(stored.IsDraft);
    }

    // ------------------------------------------------------------------ structure

    [Fact]
    public async Task SubSections_AreStoredUnderTheirCollection_AndExportedInline()
    {
        var (db, service) = NewDb();
        var file = GateFile();
        var doors = Section(file, "Doors");
        var template = new PortableDisplaySectionDto { SectionGuid = NewGuid(), SectionName = "Door card", Fields = doors.Fields };
        doors.Fields = new();
        doors.SubSections = new() { template };

        var result = await service.ImportAsync(file, dryRun: false, makeDefault: false, asDraft: false);

        Assert.True(result.Status == FormConfigurationImportStatus.Imported, string.Join(" | ", result.Errors));
        Assert.Equal(4, result.SectionCount);
        db.ChangeTracker.Clear();
        var stored = await db.DisplaySections.SingleAsync(s => s.SectionName == "Door card");
        var parent = await db.DisplaySections.SingleAsync(s => s.SectionName == "Doors");
        Assert.Null(stored.DisplayConfigurationId);
        Assert.Equal(parent.Id, stored.ParentSectionId);
        Assert.Equal(int.Parse(result.DisplayConfigurationId!), parent.DisplayConfigurationId);
        Assert.Equal(3, JsonSerializer.Deserialize<string[]>((await db.DisplayConfigurations.SingleAsync()).SectionOrderJson)!.Length);

        var exported = (await service.ExportAsync(int.Parse(result.DisplayConfigurationId!)))!;
        var exportedDoors = exported.Sections.Single(s => s.SectionName == "Doors");
        Assert.Empty(exportedDoors.Fields);
        Assert.Equal("Door card", Assert.Single(exportedDoors.SubSections!).SectionName);
        Assert.Equal(template.Fields.Select(f => f.FieldGuid), exportedDoors.SubSections![0].Fields.Select(f => f.FieldGuid));
    }

    [Fact]
    public async Task Structure_RequiresFormatGuidEntityNameAndASection()
    {
        var (_, service) = NewDb();
        var result = await DryRun(service, new PortableDisplayConfigurationDto { Format = "knk-formconfig/1", ConfigurationGuid = "nope" });

        AssertError(result, "Unknown format 'knk-formconfig/1'");
        AssertError(result, "configurationGuid is missing or not a GUID");
        AssertError(result, "entityTypeName is required");
        AssertError(result, "name is required");
        AssertError(result, "At least one section is required");
    }

    [Fact]
    public async Task Structure_GuidsMustBeUniqueInTheFile_AndMissingOnesAreGenerated()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "Spawn point").Fields[1].FieldGuid = Section(file, "Spawn point").Fields[0].FieldGuid;
        Section(file, "Doors").SectionGuid = GeneralGuid;
        Section(file, "General").Fields[1].FieldGuid = null;

        var result = await DryRun(service, file);

        AssertError(result, "fieldGuid " + Section(file, "Spawn point").Fields[0].FieldGuid + " is used twice");
        AssertError(result, $"sectionGuid {GeneralGuid} is used twice");
        AssertWarning(result, "fieldGuid is missing; a new GUID was generated");
    }

    [Fact]
    public async Task Structure_SubSectionsNeedACollection_AndOnlyOneLevel()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "General").SubSections = new() { new PortableDisplaySectionDto { SectionGuid = NewGuid(), SectionName = "Nested" } };
        var card = new PortableDisplaySectionDto
        {
            SectionGuid = NewGuid(),
            SectionName = "Card",
            Fields = new() { Field("Name", "Door") },
            SubSections = new() { new PortableDisplaySectionDto { SectionGuid = NewGuid(), SectionName = "Too deep" } }
        };
        Section(file, "Doors").SubSections = new() { card, new PortableDisplaySectionDto { SectionGuid = NewGuid(), SectionName = "Second" } };

        var result = await DryRun(service, file);

        AssertError(result, "Section 'General' has subSections but is not a collection");
        AssertError(result, "sub-section 'Card' has sub-sections of its own");
        AssertWarning(result, "Section 'Doors' has fields and subSections");
    }

    [Fact]
    public async Task Structure_DisplayConditionsAreRejected()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "General").Fields[0].DisplayConditionGroups = new() { new PortableDisplayConditionGroupDto() };

        AssertError(await DryRun(service, file), "display conditions are not supported by the model yet");
    }

    [Fact]
    public async Task Structure_ModelLengthsAreChecked()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "General").Fields[1].Label = new string('x', 201);
        Section(file, "Spawn point").Description = new string('x', 501);

        var result = await DryRun(service, file);

        AssertError(result, "label is longer than 200 characters");
        AssertError(result, "Section 'Spawn point': description is longer than 500 characters");
    }

    // ------------------------------------------------------------------ elements

    [Fact]
    public async Task Elements_FieldsNeedANameOrTemplate_AndRelatedNamesComeInPairs()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "General").Fields.Add(Field(null, "Nothing"));
        Section(file, "General").Fields.Add(Field("Name", "Street", rel: "Street"));
        Section(file, "Spawn point").RelatedEntityPropertyName = null;

        var result = await DryRun(service, file);

        AssertError(result, "field 'Nothing' needs a fieldName or a templateText");
        AssertError(result, "field 'Street': relatedEntityPropertyName needs a relatedEntityTypeName");
        AssertError(result, "Section 'Spawn point': relatedEntityTypeName must be empty without a relatedEntityPropertyName");
    }

    [Fact]
    public async Task Elements_ActionButtons()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "General").ActionButtonsConfigJson = "{\"showEditButton\":true,\"showFancyButton\":true}";
        Section(file, "Doors").ActionButtonsConfigJson = "{\"showAddButton\":true,\"showViewButton\":\"yes\"}";

        var result = await DryRun(service, file);
        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, result.Status);
        AssertWarning(result, "Section 'General': action buttons are only shown on sections with a relatedEntityTypeName");
        AssertWarning(result, "key 'showFancyButton' is not one the page reads");
        AssertWarning(result, "showAddButton is hidden on the page until relationship actions exist (KNG-148)");
        AssertWarning(result, "'showViewButton' should be true or false");

        Section(file, "Spawn point").ActionButtonsConfigJson = "[true]";
        AssertError(await DryRun(service, file), "actionButtonsConfigJson must be a JSON object");
    }

    [Fact]
    public async Task Elements_TemplateText()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        var doors = Section(file, "Doors");
        doors.Fields.Add(new PortableDisplayFieldDto { FieldGuid = NewGuid(), Label = "Open", TemplateText = "${healthCurrent" });
        doors.Fields.Add(new PortableDisplayFieldDto { FieldGuid = NewGuid(), Label = "Bad operand", TemplateText = "${healthMax + }" });
        doors.Fields.Add(new PortableDisplayFieldDto { FieldGuid = NewGuid(), Label = "Bad path", TemplateText = "${health-max!}" });

        var result = await DryRun(service, file);
        AssertError(result, "field 'Open': templateText has a '${' that is never closed");
        AssertError(result, "field 'Bad operand': templateText '${healthMax +}' has an operator without a value");
        AssertError(result, "field 'Bad path': templateText '${health-max!}': 'max!' is not a property path or a number");

        var warnings = GateFile();
        Section(warnings, "Doors").Fields.Add(new PortableDisplayFieldDto { FieldGuid = NewGuid(), Label = "Pascal", TemplateText = "${HealthMax}" });
        Section(warnings, "Doors").Fields.Add(new PortableDisplayFieldDto { FieldGuid = NewGuid(), Label = "Unknown", TemplateText = "${hitPoints * 2}" });
        var passed = await DryRun(service, warnings);
        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, passed.Status);
        AssertWarning(passed, "path 'HealthMax' starts 'HealthMax' with a capital");
        AssertWarning(passed, "field 'Unknown': GateDoor has no property 'hitPoints'. (templateText)");
    }

    [Fact]
    public async Task Elements_FieldTypes()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "General").Fields.Add(Field("Location", "Spawn", "Object"));
        Section(file, "General").Fields.Add(Field("AllowExit", "Allow exit", "Integer"));
        Section(file, "General").Fields.Add(Field("WgRegionId", "Region", "Text"));

        var result = await DryRun(service, file);

        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, result.Status);
        AssertWarning(result, "field 'Spawn': Location is a related object, which the page shows as [object Object]");
        AssertWarning(result, "field 'Allow exit': fieldType 'Integer' but AllowExit is Boolean in the model");
        AssertWarning(result, "field 'Region': fieldType 'Text' is not one the page formats");
    }

    [Fact]
    public async Task Elements_HotEditOnlyOnSimpleRootFields()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "Spawn point").Fields[1].IsEditableInDisplay = true;
        var district = Section(file, "General").Fields.Single(f => f.Label == "District");
        district.IsEditableInDisplay = true;
        var description = Section(file, "General").Fields.Single(f => f.Label == "Description");
        description.IsEditableInDisplay = true;
        description.FieldType = "Enum";

        var result = await DryRun(service, file);

        AssertError(result, "Section 'Spawn point', field 'World' can't be isEditableInDisplay: it is not in a top-level section without relatedEntityPropertyName");
        AssertError(result, "field 'District' can't be isEditableInDisplay: it has a relatedEntityPropertyName");
        AssertError(result, "field 'Description' can't be isEditableInDisplay: its fieldType is not String, Integer, Number or Boolean");
    }

    [Fact]
    public async Task Elements_HotEditOfAComputedProperty_IsAWarning()
    {
        var (_, service) = NewDb();
        var file = new PortableDisplayConfigurationDto
        {
            Format = PortableDisplayConfigurationDto.CurrentFormat,
            ConfigurationGuid = NewGuid(),
            EntityTypeName = "User",
            Name = "User - Test",
            Sections = new()
            {
                new PortableDisplaySectionDto
                {
                    SectionGuid = NewGuid(),
                    SectionName = "Account",
                    Fields = new() { new PortableDisplayFieldDto { FieldGuid = NewGuid(), FieldName = "IsFullAccount", Label = "Web account", FieldType = "Boolean", IsEditableInDisplay = true } }
                }
            }
        };

        var result = await DryRun(service, file);

        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, result.Status);
        AssertWarning(result, "User.IsFullAccount is computed; the save would be ignored");
    }

    // ------------------------------------------------------------------ metadata

    [Fact]
    public async Task Metadata_UnknownEntity_IsAnError()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        file.EntityTypeName = "Castle";

        AssertError(await DryRun(service, file), "entityTypeName 'Castle' is not an entity the API has metadata for");
    }

    [Fact]
    public async Task Metadata_Drift_IsAnError_UnlessTheElementSaysWarn()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "General").Fields.Add(Field("GateType", "Gate type", "Enum"));
        Section(file, "Spawn point").Fields.Add(Field("Name", "Street", rel: "Street", relType: "Street"));

        var result = await DryRun(service, file);
        AssertError(result, "field 'Gate type': GateStructure has no property 'GateType'.");
        // In a related single section a field with its own relatedEntityPropertyName reads from the root entity.
        Assert.DoesNotContain(result.Errors, e => e.Contains("field 'Street'"));

        Section(file, "General").Fields.Single(f => f.Label == "Gate type").MetadataCheck = "warn";
        var warned = await DryRun(service, file);
        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, warned.Status);
        AssertWarning(warned, "field 'Gate type': GateStructure has no property 'GateType'." + DisplayConfigurationPortabilityService.WarnMarker);
    }

    [Fact]
    public async Task Metadata_RelatedSections_MustMatchTheModel()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "Spawn point").IsCollection = true;
        Section(file, "Doors").RelatedEntityTypeName = "GateBlockSnapshot";
        Section(file, "General").Fields.Add(Field("Name", "Icon", rel: "IconMaterialRefId", relType: "MinecraftMaterialRef"));
        Section(file, "General").Fields.Add(Field("Name", "Door", rel: "GateDoors", relType: "GateDoor"));

        var result = await DryRun(service, file);

        AssertError(result, "Section 'Spawn point': isCollection is true but GateStructure.Location is a single Location");
        AssertError(result, "Section 'Doors': relatedEntityTypeName is 'GateBlockSnapshot' but GateStructure.GateDoors is a list of GateDoor");
        AssertError(result, "field 'Icon': GateStructure.IconMaterialRefId is the MinecraftMaterialRef id (Integer), not the related object");
        AssertError(result, "field 'Door': GateStructure.GateDoors is a list; use a section with isCollection true");
    }

    [Fact]
    public async Task Metadata_CollectionFields_ResolveOnTheItemType_AndDottedPathsFollowNavigations()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "Doors").Fields.Add(Field("Name", "Gate", rel: "GateStructure", relType: "GateStructure"));
        Section(file, "Doors").Fields.Add(Field("WgRegionId", "Region"));
        Section(file, "General").Fields.Add(Field("District.Name", "District (path)"));
        Section(file, "General").Fields.Add(Field("District.Nope", "Broken path"));

        var result = await DryRun(service, file);

        Assert.DoesNotContain(result.Errors, e => e.Contains("field 'Gate'") || e.Contains("District (path)"));
        AssertError(result, "Section 'Doors', field 'Region': GateDoor has no property 'WgRegionId'.");
        AssertError(result, "field 'Broken path': District has no property 'Nope' (in 'District.Nope').");
    }

    [Fact]
    public async Task Metadata_InvalidMetadataCheckValue_IsAnError()
    {
        var (_, service) = NewDb();
        var file = GateFile();
        Section(file, "General").MetadataCheck = "maybe";

        AssertError(await DryRun(service, file), "metadataCheck 'maybe' is not \"error\" or \"warn\"");
    }

    // ------------------------------------------------------------------ export

    [Fact]
    public async Task Export_FollowsTheOrderLists_AndCopiesLinkedSections()
    {
        var (db, service) = NewDb();
        var templateName = new DisplayField { FieldGuid = Guid.NewGuid(), FieldName = "Name", Label = "Name", FieldType = "String" };
        var templateDescription = new DisplayField { FieldGuid = Guid.NewGuid(), FieldName = "Description", Label = "Description", FieldType = "String" };
        var reusable = new DisplaySection
        {
            SectionName = "Reusable general",
            Description = "From the template.",
            IsReusable = true,
            Fields = new() { templateName, templateDescription },
            FieldOrderJson = JsonSerializer.Serialize(new[] { templateName.FieldGuid, templateDescription.FieldGuid })
        };
        db.DisplaySections.Add(reusable);
        await db.SaveChangesAsync();

        var entry = new DisplayField { FieldGuid = Guid.NewGuid(), FieldName = "AllowEntry", Label = "Entry", FieldType = "Boolean" };
        var exit = new DisplayField { FieldGuid = Guid.NewGuid(), FieldName = "AllowExit", Label = "Exit", FieldType = "Boolean" };
        var hiddenField = new DisplayField { FieldGuid = Guid.NewGuid(), FieldName = "WgRegionId", Label = "Region", FieldType = "String" };
        var access = new DisplaySection
        {
            SectionName = "Access",
            Fields = new() { entry, exit, hiddenField },
            FieldOrderJson = JsonSerializer.Serialize(new[] { exit.FieldGuid, entry.FieldGuid })
        };
        var linked = new DisplaySection
        {
            SectionName = "Reusable general",
            SourceSectionId = reusable.Id,
            IsLinkedToSource = true,
            FieldOrderJson = reusable.FieldOrderJson
        };
        var hidden = new DisplaySection { SectionName = "Hidden" };
        var config = new DisplayConfiguration
        {
            Name = "Structure - Old",
            EntityTypeName = "Structure",
            IsDefault = true,
            IsDraft = true,
            Sections = new() { access, linked, hidden }
        };
        config.SectionOrderJson = JsonSerializer.Serialize(new[] { linked.SectionGuid, access.SectionGuid, Guid.NewGuid() });
        db.DisplayConfigurations.Add(config);
        await db.SaveChangesAsync();

        var file = (await service.ExportAsync(config.Id))!;

        Assert.Equal(PortableDisplayConfigurationDto.CurrentFormat, file.Format);
        Assert.Equal(config.ConfigurationGuid.ToString(), file.ConfigurationGuid);
        Assert.Equal(new[] { "Reusable general", "Access" }, file.Sections.Select(s => s.SectionName));
        var exportedLinked = file.Sections[0];
        Assert.Equal(linked.SectionGuid.ToString(), exportedLinked.SectionGuid);
        Assert.Equal("From the template.", exportedLinked.Description);
        Assert.Equal(new[] { "Name", "Description" }, exportedLinked.Fields.Select(f => f.FieldName));
        Assert.Equal(new[] { "AllowExit", "AllowEntry" }, file.Sections[1].Fields.Select(f => f.FieldName));
        Assert.Null(file.Sections[1].ActionButtonsConfigJson);

        Assert.Contains(file.Notes!, n => n.StartsWith($"Exported from display configuration {config.Id} (draft, default)"));
        Assert.Contains(file.Notes!, n => n.Contains("is linked to reusable section"));
        Assert.Contains(file.Notes!, n => n.Contains("Section 'Hidden' is not in the configuration's section order, so it is never shown; left out."));
        Assert.Contains(file.Notes!, n => n.Contains("field 'Region' is not in Section 'Access''s field order"));
        Assert.Contains(file.Notes!, n => n.Contains("which does not exist; ignored"));

        // The export imports into another database as a published copy.
        var (_, other) = NewDb();
        var json = DisplayConfigurationPortabilityService.Serialize(file);
        Assert.DoesNotContain("\"isCollection\"", json);
        Assert.DoesNotContain("\"subSections\"", json);
        var result = await other.ImportJsonAsync(json, dryRun: false, makeDefault: false, asDraft: false);
        Assert.True(result.Status == FormConfigurationImportStatus.Imported, string.Join(" | ", result.Errors));
        Assert.Empty(result.Warnings);
        Assert.False(result.IsDraft);
        Assert.Equal(4, result.FieldCount);
    }

    [Fact]
    public async Task Export_UnknownId_IsNull()
    {
        var (_, service) = NewDb();
        Assert.Null(await service.ExportAsync(42));
    }

    [Fact]
    public async Task ImportJson_UnreadableFile_IsInvalid()
    {
        var (_, service) = NewDb();
        var result = await service.ImportJsonAsync("{\"format\": ", dryRun: true, makeDefault: false, asDraft: false);
        AssertError(result, "Not a readable knk-displayconfig/1 file");
    }

    [Fact]
    public async Task ImportSeed_UnknownOrUnsafeName_IsNull()
    {
        var (_, service) = NewDb();
        Assert.Null(await service.ImportSeedAsync("Nope.display", true, false, false));
        Assert.Null(await service.ImportSeedAsync("../secrets", true, false, false));
    }
}

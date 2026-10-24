using System.Text.Json;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationResultLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-269: ValidationService answers a rule from the log of its last run for the same subject while the
/// rule and the inputs the validator reads are unchanged; a region rule's stored result expires; Force
/// and a changed input run the rule again and replace the row; "couldn't check" is never stored.
/// </summary>
public class ValidationResultLogTests : IDisposable
{
    private static readonly ValidationSubject District17 = new("District", 17);

    private readonly Mock<IFieldValidationRuleRepository> _rules = new();
    private readonly Mock<IPlaceholderResolutionService> _placeholders = new();
    private readonly Mock<IValidationMethod> _validator = new();
    private readonly FieldValidationRule _rule;
    private readonly KnKDbContext _context;
    private readonly ValidationResultLogOptions _options = new();
    private Town _town = new() { Id = 1, Name = "Cinix", Description = "d", WgRegionId = "cinix", WorldName = "hub" };

    public ValidationResultLogTests()
    {
        _rule = new FieldValidationRule
        {
            Id = 1,
            FormFieldId = 10,
            ValidationType = "LocationInsideRegion",
            DependsOnFieldId = 3,
            DependsOnField = new FormField { Id = 3, FieldName = "TownId", Label = "Town", FieldType = FieldType.Object, ObjectType = "Town" },
            ConfigJson = """{ "regionPropertyPath": "WgRegionId" }""",
            ErrorMessage = "Outside {townName}",
            SuccessMessage = "Inside",
            IsBlocking = true
        };
        _rules.Setup(r => r.GetByFormFieldIdAsync(10)).ReturnsAsync(() => new List<FieldValidationRule> { _rule });
        _placeholders.Setup(p => p.LoadEntityByIdAsync("Town", It.IsAny<object>())).ReturnsAsync(() => _town);
        _validator.SetupGet(v => v.ValidationType).Returns("LocationInsideRegion");
        _validator.Setup(v => v.DependencyPropertiesRead(It.IsAny<string?>())).Returns(new[] { "Id", "WgRegionId", "Name", "WorldName" });
        _validator.Setup(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()))
            .ReturnsAsync(() => new ValidationMethodResult { IsValid = true, Message = "Inside", Placeholders = new() { ["townName"] = "Cinix" } });

        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _context.Dispose();

    private EfValidationResultLog Log() => new(_context, Options.Create(_options), NullLogger<EfValidationResultLog>.Instance);

    private ValidationService Service() => new(_rules.Object, new[] { _validator.Object }, _placeholders.Object, Log(), Options.Create(_options));

    private static Dictionary<string, object> Context(string townJson = """{ "id": 1, "name": "Cinix" }""") =>
        new() { ["TownId"] = JsonDocument.Parse(townJson).RootElement };

    private void VerifyRuns(int times) =>
        _validator.Verify(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()), Times.Exactly(times));

    // ------------------------------------------------------------------ hits and misses

    [Fact]
    public async Task TheSecondRunWithTheSameInputs_IsAnsweredFromTheLog()
    {
        var service = Service();
        var first = await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        var second = await service.ValidateFieldAsync(10, 1000, null, Context(), District17);

        VerifyRuns(1);
        Assert.False(first.Metadata!.FromCache);
        Assert.True(second.Metadata!.FromCache);
        Assert.Equal((first.IsValid, first.IsBlocking, first.Message), (second.IsValid, second.IsBlocking, second.Message));
        Assert.Equal("Cinix", second.Placeholders!["townName"]);
        Assert.Equal(1, await _context.FieldValidationRunLogs.CountAsync());
        Assert.Equal("Town:1", (await _context.FieldValidationRunLogs.SingleAsync()).DependencyKey);
    }

    [Fact]
    public async Task WithoutASubject_EveryCallRuns_AndNothingIsStored()
    {
        var service = Service();
        await service.ValidateFieldAsync(10, 1000, null, Context());
        await service.ValidateFieldAsync(10, 1000, null, Context());

        VerifyRuns(2);
        Assert.Equal(0, await _context.FieldValidationRunLogs.CountAsync());
    }

    [Fact]
    public async Task AChangedFieldValue_RunsAgain_AndReplacesTheRow()
    {
        var service = Service();
        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        var changed = await service.ValidateFieldAsync(10, 1001, null, Context(), District17);

        VerifyRuns(2);
        Assert.False(changed.Metadata!.FromCache);
        Assert.Equal(1, await _context.FieldValidationRunLogs.CountAsync());
    }

    [Fact]
    public async Task AChangedDependencyPropertyTheValidatorReads_RunsAgain_ButAnUnreadOneDoesNot()
    {
        var service = Service();
        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);

        _town = new Town { Id = 1, Name = "Cinix", Description = "renamed description", WgRegionId = "cinix", WorldName = "hub" };
        var sameRegion = await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        Assert.True(sameRegion.Metadata!.FromCache);

        _town = new Town { Id = 1, Name = "Cinix", Description = "d", WgRegionId = "cinix_v2", WorldName = "hub" };
        var redrawn = await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        Assert.False(redrawn.Metadata!.FromCache);
        VerifyRuns(2);
    }

    [Fact]
    public async Task AChangedRule_RunsAgain()
    {
        var service = Service();
        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);

        _rule.ConfigJson = """{ "regionPropertyPath": "ParentRegionId" }""";
        Assert.False((await service.ValidateFieldAsync(10, 1000, null, Context(), District17)).Metadata!.FromCache);

        _rule.ErrorMessage = "Not inside {townName} any more";
        Assert.False((await service.ValidateFieldAsync(10, 1000, null, Context(), District17)).Metadata!.FromCache);
        VerifyRuns(3);
    }

    [Fact]
    public async Task Force_RunsAgain_AndReplacesTheRow()
    {
        var service = Service();
        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        var before = (await _context.FieldValidationRunLogs.AsNoTracking().SingleAsync()).ExecutedAt;
        await Task.Delay(5);

        var forced = await service.ValidateFieldAsync(10, 1000, null, Context(), District17, force: true);

        VerifyRuns(2);
        Assert.False(forced.Metadata!.FromCache);
        Assert.True((await _context.FieldValidationRunLogs.AsNoTracking().SingleAsync()).ExecutedAt > before);
    }

    [Fact]
    public async Task ARegionRulesStoredResult_ExpiresAfterTheTtl_APureRulesDoesNot()
    {
        _validator.SetupGet(v => v.ReadsExternalState).Returns(true);
        var service = Service();
        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        var row = await _context.FieldValidationRunLogs.SingleAsync();
        row.ExecutedAt = DateTime.UtcNow.AddHours(-25);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        Assert.False((await service.ValidateFieldAsync(10, 1000, null, Context(), District17)).Metadata!.FromCache);
        VerifyRuns(2);

        // The same old row is current for a validator whose verdict depends on its inputs only.
        _validator.SetupGet(v => v.ReadsExternalState).Returns(false);
        row = await _context.FieldValidationRunLogs.SingleAsync();
        row.ExecutedAt = DateTime.UtcNow.AddDays(-20);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        Assert.True((await Service().ValidateFieldAsync(10, 1000, null, Context(), District17)).Metadata!.FromCache);
        VerifyRuns(2);
    }

    [Fact]
    public async Task ACheckThatCouldNotRun_IsNotStored()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()))
            .ReturnsAsync(new ValidationMethodResult { IsValid = false, Message = "Plugin offline", Metadata = new() { ["failureReason"] = "PluginUnreachable" } });
        var service = Service();

        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);

        VerifyRuns(2);
        Assert.Equal(0, await _context.FieldValidationRunLogs.CountAsync());
    }

    [Fact]
    public async Task AStoredFailure_IsReusedToo()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()))
            .ReturnsAsync(new ValidationMethodResult { IsValid = false, Message = "outside", Placeholders = new() { ["townName"] = "Cinix" } });
        var service = Service();

        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        var again = await service.ValidateFieldAsync(10, 1000, null, Context(), District17);

        VerifyRuns(1);
        Assert.False(again.IsValid);
        Assert.True(again.IsBlocking);
        Assert.Equal("Outside {townName}", again.Message);
        Assert.True(again.Metadata!.FromCache);
    }

    [Fact]
    public async Task TwoSubjects_KeepSeparateRows_AndDisabledLogRunsEveryTime()
    {
        var service = Service();
        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);
        await service.ValidateFieldAsync(10, 1000, null, Context(), new ValidationSubject("District", 18));
        await service.ValidateFieldAsync(10, 1000, null, Context(), new ValidationSubject(ValidationSubject.DraftType, 17));
        VerifyRuns(3);
        Assert.Equal(3, await _context.FieldValidationRunLogs.CountAsync());

        _options.Enabled = false;
        await Service().ValidateFieldAsync(10, 1000, null, Context(), District17);
        VerifyRuns(4);
    }

    // The log is an optimisation. Seen live 2026-10-24: with the migration not applied, every rule
    // failed with "Table 'FieldValidationRunLogs' doesn't exist" and the form could not be left.
    [Fact]
    public async Task AnUnavailableLog_NeverBreaksValidation()
    {
        var broken = new Mock<IValidationResultLog>();
        broken.Setup(l => l.FindAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>())).ThrowsAsync(new InvalidOperationException("Table 'FieldValidationRunLogs' doesn't exist"));
        broken.Setup(l => l.StoreAsync(It.IsAny<FieldValidationRunLog>())).ThrowsAsync(new InvalidOperationException("Table 'FieldValidationRunLogs' doesn't exist"));
        var service = new ValidationService(_rules.Object, new[] { _validator.Object }, _placeholders.Object, broken.Object, Options.Create(_options));

        var result = await service.ValidateFieldAsync(10, 1000, null, Context(), District17);

        VerifyRuns(1);
        Assert.True(result.IsValid);
        Assert.Equal("Inside", result.Message);
        Assert.False(result.Metadata!.FromCache);
        broken.Verify(l => l.StoreAsync(It.IsAny<FieldValidationRunLog>()), Times.Once);
    }

    // ------------------------------------------------------------------ request DTOs and batch

    [Fact]
    public async Task TheRequestDto_CarriesSubjectAndForce()
    {
        var service = Service();
        var request = new ValidateFieldRequestDto { FieldId = 10, FieldValue = 1000, FormContextData = Context(), SubjectType = "District", SubjectId = 17 };
        await service.ValidateFieldAsync(request);
        Assert.True((await service.ValidateFieldAsync(request)).Metadata!.FromCache);

        request.Force = true;
        Assert.False((await service.ValidateFieldAsync(request)).Metadata!.FromCache);
        VerifyRuns(2);
    }

    [Fact]
    public async Task ABatch_ValidatesEveryFieldOnce_AndIsAnsweredFromTheLogNextTime()
    {
        _rules.Setup(r => r.GetByFormFieldIdAsync(11)).ReturnsAsync(new List<FieldValidationRule>
        {
            new() { Id = 2, FormFieldId = 11, ValidationType = "LocationInsideRegion", DependsOnFieldId = 3, DependsOnField = _rule.DependsOnField, IsBlocking = false, ErrorMessage = "Outside" }
        });
        var service = Service();
        var request = new ValidateFieldsBatchRequestDto
        {
            Fields = { new() { FieldId = 10, FieldValue = 1000 }, new() { FieldId = 11, FieldValue = 2000 }, new() { FieldId = 12, FieldValue = 3 } },
            FormContextData = Context(),
            SubjectType = "District",
            SubjectId = 17
        };

        var first = await service.ValidateFieldsAsync(request);
        var second = await service.ValidateFieldsAsync(request);

        VerifyRuns(2);
        Assert.Equal(new[] { 10, 11, 12 }, first.Results.Keys.OrderBy(k => k));
        Assert.True(first.Results[12].IsValid); // no rules
        Assert.True(second.Results[10].Metadata!.FromCache);
        Assert.True(second.Results[11].Metadata!.FromCache);
        Assert.Equal(2, await _context.FieldValidationRunLogs.CountAsync());
    }

    // ------------------------------------------------------------------ invalidation and hashing

    [Fact]
    public async Task InvalidateEntity_DropsTheSubjectsRows_AndTheRowsThatReadItAsADependency()
    {
        var service = Service();
        await service.ValidateFieldAsync(10, 1000, null, Context(), District17);                             // reads Town 1
        await service.ValidateFieldAsync(10, 1000, null, Context("""{ "id": 2 }"""), new ValidationSubject("District", 18)); // reads Town 2
        await service.ValidateFieldAsync(10, 5, null, Context("""{ "id": 2 }"""), new ValidationSubject("Town", 1));         // about Town 1

        Assert.Equal(2, await Log().InvalidateEntityAsync("Town", 1));
        var left = await _context.FieldValidationRunLogs.SingleAsync();
        Assert.Equal(("District", 18L), (left.SubjectType, left.SubjectId));

        Assert.Equal(1, await Log().PurgeOlderThanAsync(DateTime.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public void CanonicalJson_IgnoresKeyOrderKeyCaseAndNumberFormat()
    {
        var a = JsonDocument.Parse("""{ "b": 1, "A": { "y": [1, 2], "x": "v" }, "n": null }""").RootElement;
        var b = JsonDocument.Parse("""{ "a": { "x": "v", "y": [1, 2.0] }, "N": null, "B": 1.0 }""").RootElement;

        Assert.Equal(ValidationInputHasher.Canonical(a), ValidationInputHasher.Canonical(b));
        Assert.Equal("""{"a":{"x":"v","y":[1,2]},"b":1,"n":null}""", ValidationInputHasher.Canonical(a));
        Assert.NotEqual(ValidationInputHasher.Canonical(a), ValidationInputHasher.Canonical(JsonDocument.Parse("""{ "b": 2 }""").RootElement));
    }

    [Fact]
    public void InputHash_IsTheSameForFormJsonAndALoadedRecord_ReducedToWhatIsRead()
    {
        var fromForm = JsonDocument.Parse("""{ "id": 1, "name": "Cinix", "wgRegionId": "cinix", "worldName": "hub" }""").RootElement;
        var loaded = new Town { Id = 1, Name = "Cinix", Description = "ignored", WgRegionId = "cinix", WorldName = "hub" };
        var read = new[] { "Id", "WgRegionId", "Name", "WorldName" };

        Assert.Equal(
            ValidationInputHasher.InputHash(1000, ValidationInputHasher.ReduceDependency(fromForm, read)),
            ValidationInputHasher.InputHash(1000, ValidationInputHasher.ReduceDependency(loaded, read)));
        Assert.Equal("cinix", ValidationInputHasher.ExtractPropertyValue(new Dictionary<string, object?> { ["Town"] = loaded }, "town.wgregionid"));
    }

    [Fact]
    public void RuleHash_ChangesWithBehaviourAndMessages_NotWithConfigFormatting()
    {
        var baseline = ValidationInputHasher.RuleHash(_rule);
        var reformatted = new FieldValidationRule { ValidationType = _rule.ValidationType, DependsOnFieldId = 3, ConfigJson = """{"regionPropertyPath":   "WgRegionId"}""", ErrorMessage = _rule.ErrorMessage, SuccessMessage = "Inside", IsBlocking = true };
        Assert.Equal(baseline, ValidationInputHasher.RuleHash(reformatted));

        reformatted.IsBlocking = false;
        Assert.NotEqual(baseline, ValidationInputHasher.RuleHash(reformatted));
    }

    [Fact]
    public void Migration_CreatesOneRowPerRuleAndSubject_DeletedWithTheRule()
    {
        var up = new knkwebapi_v2.Migrations.AddFieldValidationRunLogs().UpOperations;
        var table = Assert.Single(up.OfType<CreateTableOperation>());
        var key = Assert.Single(up.OfType<CreateIndexOperation>().Where(i => i.IsUnique));

        Assert.Equal("FieldValidationRunLogs", table.Name);
        Assert.Equal(("FieldValidationRules", ReferentialAction.Cascade), (table.ForeignKeys.Single().PrincipalTable, table.ForeignKeys.Single().OnDelete));
        Assert.Equal(new[] { "RuleId", "SubjectType", "SubjectId" }, key.Columns);
    }
}

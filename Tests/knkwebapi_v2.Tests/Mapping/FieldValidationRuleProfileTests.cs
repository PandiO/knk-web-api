using System.Text.Json;
using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using Xunit;

namespace knkwebapi_v2.Tests.Mapping;

public class FieldValidationRuleProfileTests
{
    private readonly IMapper _mapper = new MapperConfiguration(configuration =>
        configuration.AddProfile<FieldValidationRuleProfile>()).CreateMapper();

    // Smoke test F5.2: the rule builder's PUT carries dependencyPath, but the update DTO had no such
    // property, so a changed path was dropped while e.g. "Block step progression" was saved.
    [Fact]
    public void AnUpdate_SavesTheDependencyPath()
    {
        var existing = new FieldValidationRule
        {
            Id = 1, FormFieldId = 10, ValidationType = "LocationInsideRegion", DependsOnFieldId = 3,
            DependencyPath = "Town.WgRegionId", IsBlocking = true
        };
        var dto = JsonSerializer.Deserialize<UpdateFieldValidationRuleDto>(
            """{ "validationType": "LocationInsideRegion", "dependsOnFieldId": 3, "dependencyPath": "SiegeScenario.Town", "configJson": "{}", "errorMessage": "Outside", "isBlocking": false }""")!;

        _mapper.Map(dto, existing);

        existing.DependencyPath.Should().Be("SiegeScenario.Town");
        existing.IsBlocking.Should().BeFalse();
        existing.FormFieldId.Should().Be(10);
    }
}

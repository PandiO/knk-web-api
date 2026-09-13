using System.Reflection;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

public class MetadataServiceDefaultValueTests
{
    [Fact]
    public void GateRegionData_HaveExplicitEmptyDefaults_WithoutMakingNameOptional()
    {
        var service = new MetadataService(Mock.Of<IServiceScopeFactory>());
        var getFieldMetadata = typeof(MetadataService).GetMethod(
            "GetFieldMetadata",
            BindingFlags.Instance | BindingFlags.NonPublic);

        getFieldMetadata.Should().NotBeNull();
        var fields = getFieldMetadata!.Invoke(service, new object[] { typeof(GateDoor) })
            .Should().BeAssignableTo<List<FieldMetadataDto>>().Subject;

        fields.Single(field => field.FieldName == nameof(GateDoor.ClosedRegionData))
            .Should().Match<FieldMetadataDto>(field => field.HasDefaultValue && field.DefaultValue == string.Empty);
        fields.Single(field => field.FieldName == nameof(GateDoor.OpenedRegionData))
            .Should().Match<FieldMetadataDto>(field => field.HasDefaultValue && field.DefaultValue == string.Empty);
        fields.Single(field => field.FieldName == nameof(GateDoor.Name)).HasDefaultValue
            .Should().BeFalse();

        var validationResult = new FormTemplateValidationService().ValidateField(
            new FormField
            {
                FieldName = nameof(GateDoor.ClosedRegionData),
                Label = "Closed Region Data",
                FieldType = FieldType.String,
                Required = false
            },
            new EntityMetadataDto
            {
                EntityName = nameof(GateDoor),
                Fields = fields
            });

        validationResult.IsCompatible.Should().BeTrue();
        validationResult.Issues.Should().BeEmpty();
    }
}
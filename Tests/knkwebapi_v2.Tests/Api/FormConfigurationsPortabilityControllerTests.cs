using System.Reflection;
using System.Text.Json;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// KNG-138: the export/import/seed endpoints of FormConfigurationsController - status codes per
/// import outcome, and the knk.admin.content rule on the writes.
/// </summary>
[Trait("Category", "API")]
public class FormConfigurationsPortabilityControllerTests
{
    private readonly Mock<IFormConfigurationPortabilityService> _portability = new();
    private readonly FormConfigurationsController _controller;

    public FormConfigurationsPortabilityControllerTests()
    {
        _controller = new FormConfigurationsController(null!, null!, null!, null!, null!, null!, null!, _portability.Object);
    }

    private static JsonElement Body(string json = "{\"format\": \"knk-formconfig/1\"}") => JsonDocument.Parse(json).RootElement;

    private void ImportReturns(string status, string? id = null) =>
        _portability.Setup(p => p.ImportJsonAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(new FormConfigurationImportResultDto { Status = status, FormConfigurationId = id });

    [Fact]
    public async Task Import_Imported_Is201WithTheResult()
    {
        ImportReturns(FormConfigurationImportStatus.Imported, "17");

        var result = Assert.IsType<CreatedAtActionResult>(await _controller.Import(Body(), dryRun: false, makeDefault: true));

        Assert.Equal(17, result.RouteValues!["id"]);
        Assert.Equal("17", Assert.IsType<FormConfigurationImportResultDto>(result.Value).FormConfigurationId);
        _portability.Verify(p => p.ImportJsonAsync(It.Is<string>(s => s.Contains("knk-formconfig/1")), false, true));
    }

    [Theory]
    [InlineData(FormConfigurationImportStatus.DryRunPassed, typeof(OkObjectResult))]
    [InlineData(FormConfigurationImportStatus.AlreadyImported, typeof(ConflictObjectResult))]
    [InlineData(FormConfigurationImportStatus.Invalid, typeof(BadRequestObjectResult))]
    public async Task Import_OtherOutcomes_CarryTheResultBody(string status, Type expected)
    {
        ImportReturns(status);

        var result = await _controller.Import(Body(), dryRun: true, makeDefault: false);

        Assert.IsType(expected, result);
        Assert.Equal(status, Assert.IsType<FormConfigurationImportResultDto>(((ObjectResult)result).Value).Status);
    }

    [Fact]
    public async Task Import_NonObjectBody_Is400()
    {
        Assert.IsType<BadRequestObjectResult>(await _controller.Import(Body("[1]")));
    }

    [Fact]
    public async Task Import_ConcurrentDuplicate_Is409()
    {
        _portability.Setup(p => p.ImportJsonAsync(It.IsAny<string>(), false, false)).ThrowsAsync(new DbUpdateException("duplicate"));

        Assert.IsType<ConflictObjectResult>(await _controller.Import(Body()));
    }

    [Fact]
    public async Task ImportSeed_UnknownName_Is404()
    {
        _portability.Setup(p => p.ImportSeedAsync("Nope", true, false)).ReturnsAsync((FormConfigurationImportResultDto?)null);

        Assert.IsType<NotFoundObjectResult>(await _controller.ImportSeed("Nope", dryRun: true));
    }

    [Fact]
    public async Task Export_WritesThePortableJson_Or404()
    {
        _portability.Setup(p => p.ExportAsync(9)).ReturnsAsync(new PortableFormConfigurationDto
        {
            Format = PortableFormConfigurationDto.CurrentFormat,
            ConfigurationGuid = "6f1c2f0e-1d7a-4c55-9a7e-0b8f1d2c3a01",
            EntityTypeName = "GateStructure",
            Name = "Gate Structure"
        });

        var content = Assert.IsType<ContentResult>(await _controller.Export(9));
        Assert.Equal("application/json", content.ContentType);
        Assert.Contains("\"format\": \"knk-formconfig/1\"", content.Content);
        Assert.IsType<NotFoundResult>(await _controller.Export(10));
    }

    [Fact]
    public void Writes_AreBehindKnkAdminContent()
    {
        var rule = typeof(FormConfigurationsController).GetCustomAttribute<RequireServiceOrPermissionForWritesAttribute>();
        Assert.NotNull(rule);
        Assert.Equal(StaffPermissions.ManageContent, rule!.Node);

        foreach (var name in new[] { nameof(FormConfigurationsController.Import), nameof(FormConfigurationsController.ImportSeed) })
        {
            var action = typeof(FormConfigurationsController).GetMethod(name)!;
            Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        }
        Assert.NotNull(typeof(FormConfigurationsController).GetMethod(nameof(FormConfigurationsController.Export))!.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(typeof(FormConfigurationsController).GetMethod(nameof(FormConfigurationsController.GetSeeds))!.GetCustomAttribute<HttpGetAttribute>());
    }
}

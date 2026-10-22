using System.Reflection;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// The knk-displayconfig/1 export/import/seed endpoints of DisplayConfigurationsController - status codes per
/// import outcome, the query flags, and the knk.admin.content rule on the writes.
/// </summary>
[Trait("Category", "API")]
public class DisplayConfigurationsPortabilityControllerTests
{
    private readonly Mock<IDisplayConfigurationPortabilityService> _portability = new();
    private readonly DisplayConfigurationsController _controller;

    public DisplayConfigurationsPortabilityControllerTests()
    {
        _controller = new DisplayConfigurationsController(null!, _portability.Object, NullLogger<DisplayConfigurationsController>.Instance);
    }

    private static JsonElement Body(string json = "{\"format\": \"knk-displayconfig/1\"}") => JsonDocument.Parse(json).RootElement;

    private void ImportReturns(string status, string? id = null) =>
        _portability.Setup(p => p.ImportJsonAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(new DisplayConfigurationImportResultDto { Status = status, DisplayConfigurationId = id });

    [Fact]
    public async Task Import_Imported_Is201WithTheResult_AndPassesTheFlags()
    {
        ImportReturns(FormConfigurationImportStatus.Imported, "17");

        var result = Assert.IsType<CreatedResult>(await _controller.Import(Body(), dryRun: false, makeDefault: true, asDraft: true));

        Assert.Equal("/api/displayconfigurations/17", result.Location);
        Assert.Equal("17", Assert.IsType<DisplayConfigurationImportResultDto>(result.Value).DisplayConfigurationId);
        _portability.Verify(p => p.ImportJsonAsync(It.Is<string>(s => s.Contains("knk-displayconfig/1")), false, true, true));
    }

    [Theory]
    [InlineData(FormConfigurationImportStatus.DryRunPassed, typeof(OkObjectResult))]
    [InlineData(FormConfigurationImportStatus.AlreadyImported, typeof(ConflictObjectResult))]
    [InlineData(FormConfigurationImportStatus.Invalid, typeof(BadRequestObjectResult))]
    public async Task Import_OtherOutcomes_CarryTheResultBody(string status, Type expected)
    {
        ImportReturns(status);

        var result = await _controller.Import(Body(), dryRun: true);

        Assert.IsType(expected, result);
        Assert.Equal(status, Assert.IsType<DisplayConfigurationImportResultDto>(((ObjectResult)result).Value).Status);
    }

    [Fact]
    public async Task Import_NonObjectBody_Is400()
    {
        Assert.IsType<BadRequestObjectResult>(await _controller.Import(Body("[1]")));
    }

    [Fact]
    public async Task Import_ConcurrentDuplicate_Is409()
    {
        _portability.Setup(p => p.ImportJsonAsync(It.IsAny<string>(), false, false, false)).ThrowsAsync(new DbUpdateException("duplicate"));

        Assert.IsType<ConflictObjectResult>(await _controller.Import(Body()));
    }

    [Fact]
    public async Task ImportSeed_PassesTheFlags_AndUnknownNameIs404()
    {
        _portability.Setup(p => p.ImportSeedAsync("User.display", true, true, false))
            .ReturnsAsync(new DisplayConfigurationImportResultDto { Status = FormConfigurationImportStatus.DryRunPassed });
        _portability.Setup(p => p.ImportSeedAsync("Nope", true, false, false)).ReturnsAsync((DisplayConfigurationImportResultDto?)null);

        Assert.IsType<OkObjectResult>(await _controller.ImportSeed("User.display", dryRun: true, makeDefault: true));
        Assert.IsType<NotFoundObjectResult>(await _controller.ImportSeed("Nope", dryRun: true));
    }

    [Fact]
    public async Task Export_WritesThePortableJson_Or404()
    {
        _portability.Setup(p => p.ExportAsync(9)).ReturnsAsync(new PortableDisplayConfigurationDto
        {
            Format = PortableDisplayConfigurationDto.CurrentFormat,
            ConfigurationGuid = "6f1c2f0e-1d7a-4c55-9a7e-0b8f1d2c3a01",
            EntityTypeName = "GateStructure",
            Name = "Gate Structure",
            Sections = new() { new PortableDisplaySectionDto { SectionName = "General" } }
        });

        var content = Assert.IsType<ContentResult>(await _controller.Export(9));
        Assert.Equal("application/json", content.ContentType);
        Assert.Contains("\"format\": \"knk-displayconfig/1\"", content.Content);
        Assert.DoesNotContain("\"isCollection\"", content.Content);
        Assert.IsType<NotFoundObjectResult>(await _controller.Export(10));
    }

    [Fact]
    public void Writes_AreBehindKnkAdminContent()
    {
        var rule = typeof(DisplayConfigurationsController).GetCustomAttribute<RequireServiceOrPermissionForWritesAttribute>();
        Assert.NotNull(rule);
        Assert.Equal(StaffPermissions.ManageContent, rule!.Node);

        foreach (var name in new[] { nameof(DisplayConfigurationsController.Import), nameof(DisplayConfigurationsController.ImportSeed) })
        {
            var action = typeof(DisplayConfigurationsController).GetMethod(name)!;
            Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        }
        Assert.NotNull(typeof(DisplayConfigurationsController).GetMethod(nameof(DisplayConfigurationsController.Export))!.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(typeof(DisplayConfigurationsController).GetMethod(nameof(DisplayConfigurationsController.GetSeeds))!.GetCustomAttribute<HttpGetAttribute>());
    }
}

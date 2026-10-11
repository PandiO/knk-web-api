using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Api;

/// <summary>KNG-59: the generic grant routes keep one row per (holder, node).</summary>
[Trait("Category", "API")]
public class PermissionGrantsControllerTests
{
    private readonly Mock<IPermissionGrantService> _mockService = new();
    private readonly PermissionGrantsController _controller;

    public PermissionGrantsControllerTests()
    {
        // As the plugin (key): the escalation guard (closed-alpha WP3) only checks web users.
        _controller = new PermissionGrantsController(_mockService.Object, new Mock<IPermissionEscalationGuard>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.Plugin() }
        };
    }

    [Fact]
    public async Task Update_OntoATakenNode_Returns409WithTheExistingGrant()
    {
        _mockService
            .Setup(s => s.UpdateAsync(5, It.IsAny<PermissionGrantDto>(), It.IsAny<int?>()))
            .ThrowsAsync(new PermissionGrantConflictException(6, "Holder 1 already has a grant for node 'knk.gate.open' (grant 6)."));

        var result = await _controller.Update(5, new PermissionGrantDto { HolderId = 1, Node = "knk.gate.open", Value = true });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsTheUpsertedRow()
    {
        var upserted = new PermissionGrantDto { Id = 5, HolderId = 1, Node = "knk.gate.open", Value = false };
        _mockService.Setup(s => s.CreateAsync(It.IsAny<PermissionGrantDto>(), It.IsAny<int?>())).ReturnsAsync(upserted);

        var result = await _controller.Create(new PermissionGrantDto { HolderId = 1, Node = "knk.gate.open", Value = false });

        var created = Assert.IsType<CreatedAtRouteResult>(result);
        Assert.Same(upserted, created.Value);
    }
}

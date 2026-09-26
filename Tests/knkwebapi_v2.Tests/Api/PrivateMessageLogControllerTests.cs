using Microsoft.AspNetCore.Mvc;
using Moq;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// KNG-18 Phase 3: api/private-message-log (DESIGN.md §3.2) - the batch write is game-server
/// only, the read needs the plugin key or knk.pmlog.read, and the viewer is passed on for the
/// audit entry.
/// </summary>
[Trait("Category", "API")]
public class PrivateMessageLogControllerTests
{
    private const string ReadNode = "knk.pmlog.read";
    private readonly Mock<IPrivateMessageLogService> _service = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private PrivateMessageLogController Controller(Microsoft.AspNetCore.Http.HttpContext http) =>
        new(_service.Object) { ControllerContext = new ControllerContext { HttpContext = http } };

    private void Holds(int userId, bool granted) =>
        _permissions.Setup(p => p.CheckAsync(userId, ReadNode)).ReturnsAsync(new PermissionCheckResponseDto
        {
            UserId = userId,
            Node = ReadNode,
            Result = granted ? PermissionResolutionResult.Granted : PermissionResolutionResult.Denied
        });

    private Task<int?> Gates(string action, Microsoft.AspNetCore.Http.HttpContext http) =>
        ServiceAuthTestHelper.RunGates(typeof(PrivateMessageLogController), action, http, _permissions.Object);

    // ===== POST batch: game server only =====

    [Fact]
    public async Task Batch_Anonymous_Is401()
    {
        Assert.Equal(401, await Gates(nameof(PrivateMessageLogController.AddBatch), ServiceAuthTestHelper.Anonymous()));
    }

    [Fact]
    public async Task Batch_WebUser_Is403_EvenWithTheReadNode()
    {
        Holds(5, granted: true);
        Assert.Equal(403, await Gates(nameof(PrivateMessageLogController.AddBatch), ServiceAuthTestHelper.WebUser(5)));
    }

    [Fact]
    public async Task Batch_PluginKey_Passes()
    {
        Assert.Null(await Gates(nameof(PrivateMessageLogController.AddBatch), ServiceAuthTestHelper.Plugin()));
    }

    [Fact]
    public async Task Batch_ReturnsTheCounts_Or400ForAMalformedBatch()
    {
        var entries = new List<CreatePrivateMessageLogEntryDto>();
        _service.Setup(s => s.AddBatchAsync(entries)).ReturnsAsync(new PrivateMessageLogBatchResultDto { Accepted = 3, Duplicates = 1 });
        var ok = await Controller(ServiceAuthTestHelper.Plugin()).AddBatch(entries);
        Assert.Equal(3, Assert.IsType<PrivateMessageLogBatchResultDto>(Assert.IsType<OkObjectResult>(ok.Result).Value).Accepted);

        _service.Setup(s => s.AddBatchAsync(entries)).ThrowsAsync(new ArgumentException("too many"));
        var bad = await Controller(ServiceAuthTestHelper.Plugin()).AddBatch(entries);
        Assert.IsType<BadRequestObjectResult>(bad.Result);
    }

    // ===== GET: plugin key or knk.pmlog.read =====

    [Fact]
    public async Task Search_Anonymous_Is401()
    {
        Assert.Equal(401, await Gates(nameof(PrivateMessageLogController.Search), ServiceAuthTestHelper.Anonymous()));
    }

    [Fact]
    public async Task Search_WebUserWithoutTheNode_Is403()
    {
        Holds(5, granted: false);
        Assert.Equal(403, await Gates(nameof(PrivateMessageLogController.Search), ServiceAuthTestHelper.WebUser(5)));
    }

    [Fact]
    public async Task Search_WebUserWithTheNode_Passes_AndIsTheAuditedViewer()
    {
        Holds(5, granted: true);
        var http = ServiceAuthTestHelper.WebUser(5);
        Assert.Null(await Gates(nameof(PrivateMessageLogController.Search), http));

        _service.Setup(s => s.SearchAsync(It.IsAny<PrivateMessageLogQueryDto>(), 5))
            .ReturnsAsync(new PagedResultDto<PrivateMessageLogEntryDto>());
        var result = await Controller(http).Search(participantUserId: 1, otherUserId: 2, from: null, to: null, pageNumber: 2, pageSize: 10);

        Assert.IsType<OkObjectResult>(result.Result);
        _service.Verify(s => s.SearchAsync(It.Is<PrivateMessageLogQueryDto>(q =>
            q.ParticipantUserId == 1 && q.OtherUserId == 2 && q.PageNumber == 2 && q.PageSize == 10), 5), Times.Once);
    }

    [Fact]
    public async Task Search_PluginKey_Passes_WithTheActingStaffMemberAsViewer()
    {
        var http = ServiceAuthTestHelper.Plugin(actingUserId: 7);
        Assert.Null(await Gates(nameof(PrivateMessageLogController.Search), http));

        _service.Setup(s => s.SearchAsync(It.IsAny<PrivateMessageLogQueryDto>(), 7))
            .ReturnsAsync(new PagedResultDto<PrivateMessageLogEntryDto>());
        await Controller(http).Search(1, null, null, null);

        _service.Verify(s => s.SearchAsync(It.IsAny<PrivateMessageLogQueryDto>(), 7), Times.Once);
    }

    [Fact]
    public async Task Search_BadQuery_Is400()
    {
        _service.Setup(s => s.SearchAsync(It.IsAny<PrivateMessageLogQueryDto>(), It.IsAny<int?>()))
            .ThrowsAsync(new ArgumentException("participantUserId is required."));

        var result = await Controller(ServiceAuthTestHelper.Plugin()).Search(0, null, null, null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}

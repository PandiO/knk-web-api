using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#180: what the world-task endpoints answer (409 with the current status, 422 for a refused result,
/// 400, 404), the creator taken from the JWT, and the draft's task list.
/// </summary>
public class WorldTasksControllerTests
{
    private readonly Mock<IWorldTaskService> _service = new();

    private WorldTasksController Controller(HttpContext? http = null) =>
        new(_service.Object) { ControllerContext = new ControllerContext { HttpContext = http ?? ServiceAuthTestHelper.Plugin() } };

    [Fact]
    public async Task Create_PassesTheLoggedInCallerAsCreator_AndTheGameServerAsNone()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<WorldTaskCreateDto>(), It.IsAny<int?>())).ReturnsAsync(new WorldTaskReadDto { Id = 1 });

        await Controller(ServiceAuthTestHelper.WebUser(7)).Create(new WorldTaskCreateDto());
        await Controller(ServiceAuthTestHelper.Plugin()).Create(new WorldTaskCreateDto());

        _service.Verify(s => s.CreateAsync(It.IsAny<WorldTaskCreateDto>(), 7), Times.Once);
        _service.Verify(s => s.CreateAsync(It.IsAny<WorldTaskCreateDto>(), null), Times.Once);
    }

    [Fact]
    public async Task Claim_LostRace_Is409WithTheCurrentStatus()
    {
        _service.Setup(s => s.ClaimAsync(4, It.IsAny<ClaimTaskDto>()))
            .ThrowsAsync(new WorldTaskConflictException("Task 4 is already claimed by Steve.", "InProgress"));

        var result = await Controller().ClaimTask(4, new ClaimTaskDto());

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("InProgress", System.Text.Json.JsonSerializer.Serialize(conflict.Value));
    }

    [Fact]
    public async Task Claim_WrongCode_Is400_AndUnknownTask404()
    {
        _service.Setup(s => s.ClaimAsync(4, It.IsAny<ClaimTaskDto>())).ThrowsAsync(new ArgumentException("The task code does not match this task."));
        _service.Setup(s => s.ClaimAsync(5, It.IsAny<ClaimTaskDto>())).ThrowsAsync(new KeyNotFoundException());

        Assert.IsType<BadRequestObjectResult>(await Controller().ClaimTask(4, new ClaimTaskDto()));
        Assert.IsType<NotFoundResult>(await Controller().ClaimTask(5, new ClaimTaskDto()));
    }

    [Fact]
    public async Task Complete_RefusedGateScanResult_Is422NotA500()
    {
        _service.Setup(s => s.CompleteAsync(4, It.IsAny<CompleteTaskDto>()))
            .ThrowsAsync(new WorldTaskRejectedException("Gate block scan result could not be applied: no door"));

        var result = await Controller().CompleteTask(4, new CompleteTaskDto { OutputJson = "{}" });

        Assert.IsType<UnprocessableEntityObjectResult>(result);
    }

    [Fact]
    public async Task CancelReleaseHeartbeatApplied_ReturnTheTask()
    {
        var task = new WorldTaskReadDto { Id = 4, Status = "Cancelled" };
        _service.Setup(s => s.CancelAsync(4, It.IsAny<CancelTaskDto?>())).ReturnsAsync(task);
        _service.Setup(s => s.ReleaseAsync(4, It.IsAny<ReleaseTaskDto?>())).ReturnsAsync(task);
        _service.Setup(s => s.HeartbeatAsync(4, It.IsAny<HeartbeatTaskDto?>())).ReturnsAsync(task);
        _service.Setup(s => s.MarkAppliedAsync(4)).ReturnsAsync(task);

        Assert.Same(task, Assert.IsType<OkObjectResult>(await Controller(ServiceAuthTestHelper.WebUser(7)).CancelTask(4)).Value);
        Assert.Same(task, Assert.IsType<OkObjectResult>(await Controller().ReleaseTask(4)).Value);
        Assert.Same(task, Assert.IsType<OkObjectResult>(await Controller().Heartbeat(4)).Value);
        Assert.Same(task, Assert.IsType<OkObjectResult>(await Controller(ServiceAuthTestHelper.WebUser(7)).MarkApplied(4)).Value);
    }

    [Fact]
    public async Task ListOpenForServer_NeedsAServerId()
    {
        Assert.IsType<BadRequestObjectResult>(await Controller().ListOpenForServer(" "));
        _service.Setup(s => s.ListOpenForServerAsync("survival-1")).ReturnsAsync(new List<WorldTaskReadDto>());
        Assert.IsType<OkObjectResult>(await Controller().ListOpenForServer("survival-1"));
    }

    [Fact]
    public async Task DraftWorldTasks_UnknownDraftIs404_ElseTheLatestPerField()
    {
        var drafts = new Mock<IFormSubmissionProgressService>();
        drafts.Setup(d => d.GetByIdAsync(40)).ReturnsAsync(new FormSubmissionProgressDto { Id = "40" });
        var latest = new List<WorldTaskReadDto> { new() { Id = 1, FieldName = "Location" } };
        _service.Setup(s => s.ListLatestForProgressAsync(40)).ReturnsAsync(latest);
        var controller = new FormSubmissionProgressController(drafts.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(7) }
        };

        Assert.IsType<NotFoundResult>(await controller.GetWorldTasks(41, _service.Object));
        Assert.Same(latest, Assert.IsType<OkObjectResult>(await controller.GetWorldTasks(40, _service.Object)).Value);
    }
}

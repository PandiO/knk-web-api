using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

// Covers the scan-completion routing in WorldTaskService.CompleteAsync described in
// docs/features/gate-structure-animation/ROTATION_GAP_FILL_DESIGN.md ("Backend" testing
// strategy): a GateOpenedBlockScan result must only ever touch GateOpenedBlockSnapshot rows,
// and a GateBlockScan result must only ever touch GateBlockSnapshot rows - never both, even
// though both task types share the same InputJson/OutputJson shape.
public class WorldTaskServiceGateScanTests
{
    private readonly Mock<IWorldTaskRepository> _taskRepo = new();
    private readonly Mock<IWorkflowRepository> _workflowRepo = new();
    private readonly Mock<IGateStructureService> _gateStructureService = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly WorldTaskService _service;

    public WorldTaskServiceGateScanTests()
    {
        _mapper.Setup(m => m.Map<WorldTaskReadDto>(It.IsAny<WorldTask>()))
            .Returns(new WorldTaskReadDto());

        _service = new WorldTaskService(
            _taskRepo.Object,
            _workflowRepo.Object,
            _gateStructureService.Object,
            _mapper.Object);
    }

    private static WorldTask MakeTask(string taskType, int gateStructureId) => new()
    {
        Id = 1,
        WorkflowSessionId = 1,
        StepKey = null, // no workflow step wiring needed for this test
        TaskType = taskType,
        Status = "InProgress",
        InputJson = $"{{\"gateStructureId\":{gateStructureId}}}"
    };

    private static string SuccessOutputJson(int blockCount) =>
        $"{{\"status\":\"Success\",\"blockCount\":{blockCount},\"snapshots\":[" +
        string.Join(",", Enumerable.Range(0, blockCount).Select(i =>
            $"{{\"relativeX\":{i},\"relativeY\":0,\"relativeZ\":0,\"worldX\":{i},\"worldY\":0,\"worldZ\":0,\"materialName\":\"STONE\",\"sortOrder\":{i}}}")) +
        "]}";

    [Fact]
    public async Task CompleteAsync_GateOpenedBlockScan_OnlyTouchesOpenedBlockSnapshots()
    {
        var task = MakeTask(WorldTaskTypes.GateOpenedBlockScan, gateStructureId: 42);
        _taskRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(task);

        await _service.CompleteAsync(1, new CompleteTaskDto { OutputJson = SuccessOutputJson(2) });

        _gateStructureService.Verify(s => s.ClearOpenedBlockSnapshotsAsync(42), Times.Once);
        _gateStructureService.Verify(
            s => s.AddOpenedBlockSnapshotsAsync(42, It.IsAny<IEnumerable<GateOpenedBlockSnapshotCreateDto>>()),
            Times.Once);

        _gateStructureService.Verify(s => s.ClearBlockSnapshotsAsync(It.IsAny<int>()), Times.Never);
        _gateStructureService.Verify(
            s => s.AddBlockSnapshotsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<GateBlockSnapshotCreateDto>>()),
            Times.Never);

        Assert.Equal("Completed", task.Status);
    }

    [Fact]
    public async Task CompleteAsync_GateBlockScan_OnlyTouchesBlockSnapshots()
    {
        var task = MakeTask(WorldTaskTypes.GateBlockScan, gateStructureId: 7);
        _taskRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(task);

        await _service.CompleteAsync(1, new CompleteTaskDto { OutputJson = SuccessOutputJson(3) });

        _gateStructureService.Verify(s => s.ClearBlockSnapshotsAsync(7), Times.Once);
        _gateStructureService.Verify(
            s => s.AddBlockSnapshotsAsync(7, It.IsAny<IEnumerable<GateBlockSnapshotCreateDto>>()),
            Times.Once);

        _gateStructureService.Verify(s => s.ClearOpenedBlockSnapshotsAsync(It.IsAny<int>()), Times.Never);
        _gateStructureService.Verify(
            s => s.AddOpenedBlockSnapshotsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<GateOpenedBlockSnapshotCreateDto>>()),
            Times.Never);

        Assert.Equal("Completed", task.Status);
    }
}

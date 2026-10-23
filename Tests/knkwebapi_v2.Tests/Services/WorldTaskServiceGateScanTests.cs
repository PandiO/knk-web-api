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
// though both task types share the same InputJson/OutputJson shape. Snapshots are now scoped to
// a GateDoor rather than a GateStructure (item 5's multi-door support) - see
// docs/features/gate-structure-animation/GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md.
public class WorldTaskServiceGateScanTests
{
    private readonly Mock<IWorldTaskRepository> _taskRepo = new();
    private readonly Mock<IWorkflowRepository> _workflowRepo = new();
    private readonly Mock<IGateDoorService> _gateDoorService = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly WorldTaskService _service;

    public WorldTaskServiceGateScanTests()
    {
        _mapper.Setup(m => m.Map<WorldTaskReadDto>(It.IsAny<WorldTask>()))
            .Returns(new WorldTaskReadDto());

        _service = new WorldTaskService(
            _taskRepo.Object,
            _workflowRepo.Object,
            _gateDoorService.Object,
            _mapper.Object);
    }

    private static WorldTask MakeTask(string taskType, int gateDoorId) => new()
    {
        Id = 1,
        WorkflowSessionId = 1,
        StepKey = null, // no workflow step wiring needed for this test
        TaskType = taskType,
        Status = "InProgress",
        InputJson = $"{{\"gateDoorId\":{gateDoorId}}}"
    };

    private static string SuccessOutputJson(int blockCount) =>
        $"{{\"status\":\"Success\",\"blockCount\":{blockCount},\"snapshots\":[" +
        string.Join(",", Enumerable.Range(0, blockCount).Select(i =>
            $"{{\"relativeX\":{i},\"relativeY\":0,\"relativeZ\":0,\"worldX\":{i},\"worldY\":0,\"worldZ\":0,\"materialName\":\"STONE\",\"sortOrder\":{i}}}")) +
        "]}";

    [Fact]
    public async Task CompleteAsync_GateOpenedBlockScan_OnlyTouchesOpenedBlockSnapshots()
    {
        var task = MakeTask(WorldTaskTypes.GateOpenedBlockScan, gateDoorId: 42);
        _taskRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(task);

        await _service.CompleteAsync(1, new CompleteTaskDto { OutputJson = SuccessOutputJson(2) });

        _gateDoorService.Verify(s => s.ClearOpenedBlockSnapshotsAsync(42), Times.Once);
        _gateDoorService.Verify(
            s => s.AddOpenedBlockSnapshotsAsync(42, It.IsAny<IEnumerable<GateOpenedBlockSnapshotCreateDto>>()),
            Times.Once);

        _gateDoorService.Verify(s => s.ClearBlockSnapshotsAsync(It.IsAny<int>()), Times.Never);
        _gateDoorService.Verify(
            s => s.AddBlockSnapshotsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<GateBlockSnapshotCreateDto>>()),
            Times.Never);

        Assert.Equal("Completed", task.Status);
    }

    [Fact]
    public async Task CompleteAsync_GateBlockScan_OnlyTouchesBlockSnapshots()
    {
        var task = MakeTask(WorldTaskTypes.GateBlockScan, gateDoorId: 7);
        _taskRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(task);

        await _service.CompleteAsync(1, new CompleteTaskDto { OutputJson = SuccessOutputJson(3) });

        _gateDoorService.Verify(s => s.ClearBlockSnapshotsAsync(7), Times.Once);
        _gateDoorService.Verify(
            s => s.AddBlockSnapshotsAsync(7, It.IsAny<IEnumerable<GateBlockSnapshotCreateDto>>()),
            Times.Once);

        _gateDoorService.Verify(s => s.ClearOpenedBlockSnapshotsAsync(It.IsAny<int>()), Times.Never);
        _gateDoorService.Verify(
            s => s.AddOpenedBlockSnapshotsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<GateOpenedBlockSnapshotCreateDto>>()),
            Times.Never);

        Assert.Equal("Completed", task.Status);
    }

    // KNG-268: a scan the plugin can't run is refused when the task is created.
    private static WorldTaskCreateDto ScanRequest(string taskType, int gateDoorId) => new()
    {
        WorkflowSessionId = 1,
        StepNumber = 0,
        StepKey = "block-scan",
        FieldName = taskType == WorldTaskTypes.GateOpenedBlockScan ? "OpenedBlockSnapshots" : "BlockSnapshots",
        TaskType = taskType,
        InputJson = $"{{\"gateDoorId\":{gateDoorId}}}"
    };

    private void GivenDoor(GateDoorDto door)
    {
        _gateDoorService.Setup(s => s.GetByIdAsync(door.Id ?? 0)).ReturnsAsync(door);
        _mapper.Setup(m => m.Map<WorldTask>(It.IsAny<WorldTaskCreateDto>())).Returns(new WorldTask());
    }

    [Theory]
    [InlineData(true, "opened", "/gatedoor capture 17 opened")]
    [InlineData(false, "closed", "/gatedoor capture 17 closed")]
    public async Task CreateAsync_RegionDoorWithoutItsRegion_IsRefusedWithTheCaptureCommand(bool openScan, string slot, string command)
    {
        GivenDoor(new GateDoorDto { Id = 17, Name = "Main gate", GeometryDefinitionMode = GeometryDefinitionMode.REGION, AnchorPointId = 104 });
        var taskType = openScan ? WorldTaskTypes.GateOpenedBlockScan : WorldTaskTypes.GateBlockScan;

        var ex = await Assert.ThrowsAsync<GateScanNotReadyException>(() => _service.CreateAsync(ScanRequest(taskType, 17)));

        Assert.Contains($"no {slot} region", ex.Message);
        Assert.Contains(command, ex.Message);
        _taskRepo.Verify(r => r.AddAsync(It.IsAny<WorldTask>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_FloodFillOpenScan_IsRefused()
    {
        GivenDoor(new GateDoorDto { Id = 18, Name = "Postern", GeometryDefinitionMode = GeometryDefinitionMode.FLOOD_FILL });

        var ex = await Assert.ThrowsAsync<GateScanNotReadyException>(
            () => _service.CreateAsync(ScanRequest(WorldTaskTypes.GateOpenedBlockScan, 18)));

        Assert.Contains("flood fill", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_PlaneGridDoorWithoutAnchor_IsRefused()
    {
        GivenDoor(new GateDoorDto { Id = 19, Name = "New door", GeometryDefinitionMode = GeometryDefinitionMode.PLANE_GRID });

        var ex = await Assert.ThrowsAsync<GateScanNotReadyException>(
            () => _service.CreateAsync(ScanRequest(WorldTaskTypes.GateBlockScan, 19)));

        Assert.Contains("no anchor", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_PlaneGridOpenScanWithoutOpenDoorAnchor_IsCreated()
    {
        // KNG-270: an empty open-door anchor means the open scan starts at the anchor.
        GivenDoor(new GateDoorDto { Id = 17, Name = "Main gate", GeometryDefinitionMode = GeometryDefinitionMode.PLANE_GRID, AnchorPointId = 104 });

        await _service.CreateAsync(ScanRequest(WorldTaskTypes.GateOpenedBlockScan, 17));

        _taskRepo.Verify(r => r.AddAsync(It.IsAny<WorldTask>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RegionDoorWithItsOpenedRegion_IsCreated()
    {
        GivenDoor(new GateDoorDto
        {
            Id = 17, Name = "Main gate", GeometryDefinitionMode = GeometryDefinitionMode.REGION, AnchorPointId = 104,
            OpenedRegionData = "{\"type\":\"CUBOID\"}"
        });

        await _service.CreateAsync(ScanRequest(WorldTaskTypes.GateOpenedBlockScan, 17));

        _taskRepo.Verify(r => r.AddAsync(It.IsAny<WorldTask>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_OtherTaskTypes_DontLookUpADoor()
    {
        _mapper.Setup(m => m.Map<WorldTask>(It.IsAny<WorldTaskCreateDto>())).Returns(new WorldTask());

        await _service.CreateAsync(new WorldTaskCreateDto
        {
            WorkflowSessionId = 1, StepKey = "anchor", FieldName = "AnchorPointId", TaskType = "LocationSelection",
            InputJson = "{\"gateDoorId\":17}"
        });

        _gateDoorService.Verify(s => s.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }
}

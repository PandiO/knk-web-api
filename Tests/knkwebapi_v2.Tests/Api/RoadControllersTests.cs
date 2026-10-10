using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Road navigation routes (docs/specs/navigation/IMPLEMENTATION_PLAN.md Phase 1.5): the tile
/// graph's ETag/304 handshake, the error mapping, the camelCase wire names the plugin mirrors,
/// and which routes the plugin key, a staff node or nobody may call.
/// </summary>
[Trait("Category", "API")]
public class RoadControllersTests
{
    private readonly Mock<IRoadNetworkService> _service = new();

    private RoadTilesController Tiles(string? ifNoneMatch = null)
    {
        var http = new DefaultHttpContext();
        if (ifNoneMatch != null) http.Request.Headers.IfNoneMatch = ifNoneMatch;
        return new RoadTilesController(_service.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static RoadTileGraphDto Graph(int version) => new()
    {
        Tile = new RoadTileDto { Id = 1, World = "world", TileX = 0, TileZ = 0, Version = version },
        Nodes = { new RoadNodeDto { Id = 5, World = "world", Kind = RoadNodeKind.Junction } }
    };

    [Fact]
    public async Task GetGraph_SetsTheVersionAsETag()
    {
        _service.Setup(s => s.GetTileGraphAsync("world", 0, 0)).ReturnsAsync(Graph(3));
        var controller = Tiles();

        var result = await controller.GetGraph("world", 0, 0);

        var body = Assert.IsType<RoadTileGraphDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(3, body.Tile.Version);
        Assert.Equal("\"3\"", controller.Response.Headers.ETag.ToString());
    }

    [Theory]
    [InlineData("\"3\"")]
    [InlineData("W/\"3\"")]
    [InlineData("\"1\", \"3\"")]
    public async Task GetGraph_MatchingIfNoneMatchIs304(string header)
    {
        _service.Setup(s => s.GetTileGraphAsync("world", 0, 0)).ReturnsAsync(Graph(3));
        var controller = Tiles(header);

        var result = await controller.GetGraph("world", 0, 0);

        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal("\"3\"", controller.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task GetGraph_StaleIfNoneMatchGetsTheBody()
    {
        _service.Setup(s => s.GetTileGraphAsync("world", 0, 0)).ReturnsAsync(Graph(4));

        var result = await Tiles("\"3\"").GetGraph("world", 0, 0);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetGraph_UnknownTileIs404()
    {
        _service.Setup(s => s.GetTileGraphAsync("world", 9, 9)).ReturnsAsync((RoadTileGraphDto?)null);

        var result = await Tiles().GetGraph("world", 9, 9);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Errors_MapToValidationNotFoundAndConflict()
    {
        _service.Setup(s => s.UpsertTileGraphAsync("world", 0, 0, It.IsAny<RoadTileGraphUpsertDto>())).ThrowsAsync(new ArgumentException("bad"));
        _service.Setup(s => s.UpdateNodeAsync(7, It.IsAny<RoadNodeUpdateDto>())).ThrowsAsync(new KeyNotFoundException("gone"));
        _service.Setup(s => s.CreateAnchorAsync(It.IsAny<RoadNodeAnchorDto>())).ThrowsAsync(new InvalidOperationException("taken"));
        var nodes = new RoadNodesController(_service.Object);

        var bad = Assert.IsType<BadRequestObjectResult>(await Tiles().PutGraph("world", 0, 0, new RoadTileGraphUpsertDto()));
        var notFound = Assert.IsType<NotFoundObjectResult>(await nodes.Update(7, new RoadNodeUpdateDto()));
        var conflict = Assert.IsType<ConflictObjectResult>(await nodes.CreateAnchor(new RoadNodeAnchorDto()));

        Assert.Contains("ValidationFailed", JsonSerializer.Serialize(bad.Value));
        Assert.Contains("NotFound", JsonSerializer.Serialize(notFound.Value));
        Assert.Contains("Conflict", JsonSerializer.Serialize(conflict.Value));
    }

    [Fact]
    public void Dtos_SerializeWithTheCamelCaseNamesThePluginMirrors()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = null, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var json = JsonSerializer.Serialize(new RoadTileGraphDto
        {
            Tile = new RoadTileDto { Id = 1, World = "world", Version = 2, Warnings = { "leak" } },
            Nodes = { new RoadNodeDto { Id = 5, Kind = RoadNodeKind.Boundary, Source = RoadNodeSource.Detected } },
            Edges = { new RoadEdgeDto { Id = 9, FromNodeId = 5, ToNodeId = 6, Geometry = new[] { new[] { 1, 64, 2 } }, Flags = { "NoGps" }, Source = RoadEdgeSource.Stitch, StreetSource = RoadStreetSource.Inferred } }
        }, options);

        foreach (var name in new[] { "\"tile\"", "\"tileX\"", "\"version\"", "\"warnings\"", "\"nodes\"", "\"kind\":\"Boundary\"", "\"edges\"",
                     "\"fromNodeId\"", "\"geometry\":[[1,64,2]]", "\"flags\":[\"NoGps\"]", "\"source\":\"Stitch\"", "\"streetSource\":\"Inferred\"", "\"gateDoorIds\"", "\"regionIds\"" })
        {
            Assert.Contains(name, json);
        }
        Assert.DoesNotContain("TileX", json);
    }

    [Fact]
    public async Task GetProposal_UnknownIs404()
    {
        _service.Setup(s => s.GetProposalAsync("world", 2, -2)).ReturnsAsync((RoadTileProposalDto?)null);

        Assert.IsType<NotFoundObjectResult>(await Tiles().GetProposal("world", 2, -2));
    }

    [Fact]
    public void CuratedTileDtos_SerializeStateConfirmedAndTheProposalNames()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = null, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var tile = JsonSerializer.Serialize(new RoadTileDto { World = "world", State = RoadTileState.Curated }, options);
        var edge = JsonSerializer.Serialize(new RoadEdgeDto { World = "world", Confirmed = true }, options);
        using var items = JsonDocument.Parse("[{\"n\":1}]");
        var proposal = JsonSerializer.Serialize(new RoadTileProposalDto { World = "world", Items = items.RootElement.Clone(), AddedCount = 1 }, options);

        Assert.Contains("\"state\":\"Curated\"", tile);
        Assert.Contains("\"curatedAt\"", tile);
        Assert.Contains("\"confirmed\":true", edge);
        foreach (var name in new[] { "\"items\":[{\"n\":1}]", "\"rejected\"", "\"baseVersion\"", "\"tileVersion\"", "\"addedCount\":1", "\"movedCount\"", "\"rejectedCount\"" })
        {
            Assert.Contains(name, proposal);
        }
    }

    // ------------------------------------------------------------- Gates

    [Theory]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.PutGraph))]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.MarkDirty))]
    [InlineData(typeof(RoadSurveysController), nameof(RoadSurveysController.Create))]
    public async Task PluginOnlyRoutes_RefuseAnonymousAndWebUsers(Type controller, string action)
    {
        Assert.Equal(401, await ServiceAuthTestHelper.RunGates(controller, action, ServiceAuthTestHelper.Anonymous()));
        Assert.Equal(403, await ServiceAuthTestHelper.RunGates(controller, action, ServiceAuthTestHelper.WebUser(7)));
        Assert.Null(await ServiceAuthTestHelper.RunGates(controller, action, ServiceAuthTestHelper.Plugin()));
    }

    [Theory]
    [InlineData(typeof(RoadProfilesController), nameof(RoadProfilesController.Create))]
    [InlineData(typeof(RoadProfilesController), nameof(RoadProfilesController.Update))]
    [InlineData(typeof(RoadProfilesController), nameof(RoadProfilesController.Delete))]
    [InlineData(typeof(RoadSeedsController), nameof(RoadSeedsController.Create))]
    [InlineData(typeof(RoadSeedsController), nameof(RoadSeedsController.Delete))]
    [InlineData(typeof(RoadEdgesController), nameof(RoadEdgesController.CreateRecorded))]
    [InlineData(typeof(RoadEdgesController), nameof(RoadEdgesController.Update))]
    [InlineData(typeof(RoadEdgesController), nameof(RoadEdgesController.Delete))]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.PutState))]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.PutProposal))]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.DeleteProposal))]
    public async Task StaffRoutes_NeedThePluginKeyOrTheRoadNode(Type controller, string action)
    {
        var granted = new Mock<IPermissionResolutionService>();
        granted.Setup(p => p.CheckAsync(7, StaffPermissions.RoadManage)).ReturnsAsync(new PermissionCheckResponseDto { UserId = 7, Node = StaffPermissions.RoadManage, Result = PermissionResolutionResult.Granted });

        Assert.Equal(401, await ServiceAuthTestHelper.RunGates(controller, action, ServiceAuthTestHelper.Anonymous()));
        Assert.Equal(403, await ServiceAuthTestHelper.RunGates(controller, action, ServiceAuthTestHelper.WebUser(7)));
        Assert.Null(await ServiceAuthTestHelper.RunGates(controller, action, ServiceAuthTestHelper.WebUser(7), granted.Object));
        Assert.Null(await ServiceAuthTestHelper.RunGates(controller, action, ServiceAuthTestHelper.Plugin()));
    }

    [Fact]
    public void NodeRoutes_CarryTheRoadNodeOnTheController()
    {
        var gate = Assert.Single(typeof(RoadNodesController).GetCustomAttributes(typeof(RequireServiceOrPermissionAttribute), false));
        Assert.Equal(StaffPermissions.RoadManage, ((RequireServiceOrPermissionAttribute)gate).Node);
        Assert.Equal("knk.admin.road", StaffPermissions.RoadManage);
    }

    [Theory]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.List))]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.GetGraph))]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.GetProposal))]
    [InlineData(typeof(RoadTilesController), nameof(RoadTilesController.ListProposals))]
    [InlineData(typeof(RoadNetworkController), nameof(RoadNetworkController.GetMeta))]
    [InlineData(typeof(RoadNetworkController), nameof(RoadNetworkController.GetSeedLocations))]
    [InlineData(typeof(RoadProfilesController), nameof(RoadProfilesController.List))]
    [InlineData(typeof(RoadEdgesController), nameof(RoadEdgesController.Search))]
    public async Task ReadRoutes_AreAnonymous(Type controller, string action)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceAuthTestHelper.RunGates(controller, action, ServiceAuthTestHelper.Anonymous()));
        Assert.Empty(controller.GetCustomAttributes(typeof(RequireServiceOrPermissionAttribute), false));
        Assert.Empty(controller.GetCustomAttributes(typeof(RequirePermissionAttribute), false));
    }
}

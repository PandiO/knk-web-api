using KnKWebAPI.Controllers;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#181 (WT2, DESIGN §8.3/§8.5): GET api/presence/me, GET api/presence/online-staff and
/// GET api/WorldTasks/active-for-me.
/// </summary>
public class PresenceControllerTests
{
    private const string Alice = "0f6c2a1e-7d0b-4c52-9a51-3c1e6f0a9b11";
    private const string Bob = "aa6c2a1e-7d0b-4c52-9a51-3c1e6f0a9b22";
    private const string Carl = "bb6c2a1e-7d0b-4c52-9a51-3c1e6f0a9b33";

    private readonly KnKDbContext _db = new(new DbContextOptionsBuilder<KnKDbContext>()
        .UseInMemoryDatabase("presence-" + Guid.NewGuid()).Options);
    private readonly PresenceRegistry _presence = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    public PresenceControllerTests()
    {
        _db.Users.Add(new User { Id = 5, Username = "Alice", Uuid = Alice });
        _db.Users.Add(new User { Id = 9, Username = "Bob", Uuid = Bob });
        _db.Users.Add(new User { Id = 10, Username = "Carl", Uuid = Carl });
        _db.Users.Add(new User { Id = 11, Username = "WebOnly" });
        _db.SaveChanges();
        _permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((int id, string node) => new PermissionCheckResponseDto
            {
                UserId = id, Node = node,
                Result = id is 5 or 9 ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });
    }

    private PresenceController Controller(int? userId) => new(_presence, _db, _permissions.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = userId is { } id ? ServiceAuthTestHelper.WebUser(id) : ServiceAuthTestHelper.Plugin()
        }
    };

    [Fact]
    public async Task Me_IsOnline_OnlyOnAConnectedServer()
    {
        Assert.False(((await Controller(5).Me()).Result as OkObjectResult)!.Value is PresenceMeDto { Online: true });

        _presence.ServerConnected("survival-1", "c1");
        _presence.PlayerJoined("survival-1", new RosterEntry(Alice, "Alice", "world", 5));
        var me = (PresenceMeDto)((OkObjectResult)(await Controller(5).Me()).Result!).Value!;
        Assert.True(me.Online);
        Assert.Equal("survival-1", me.ServerId);

        var webOnly = (PresenceMeDto)((OkObjectResult)(await Controller(11).Me()).Result!).Value!;
        Assert.False(webOnly.HasMinecraftAccount);
        Assert.IsType<UnauthorizedObjectResult>((await Controller(null).Me()).Result);
    }

    [Fact]
    public async Task OnlineStaff_ListsOnlinePlayersHoldingTheNode()
    {
        _presence.ServerConnected("survival-1", "c1");
        foreach (var (uuid, name) in new[] { (Alice, "Alice"), (Bob, "Bob"), (Carl, "Carl") })
        {
            _presence.PlayerJoined("survival-1", new RosterEntry(uuid.ToUpperInvariant(), name, "world", null));
        }

        var list = (List<OnlineStaffDto>)((OkObjectResult)(await Controller(5).OnlineStaff()).Result!).Value!;

        Assert.Equal(new[] { "Alice", "Bob" }, list.Select(s => s.Username)); // Carl lacks knk.tasks
        Assert.All(list, s => Assert.Equal("survival-1", s.ServerId));
        _permissions.Verify(p => p.CheckAsync(9, "knk.tasks"), Times.Once);
    }

    [Fact]
    public async Task OnlineStaff_RefusesOtherNodes_AndIsEmptyWithoutAConnectedServer()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller(5).OnlineStaff("knk.admin.*")).Result);
        var empty = (List<OnlineStaffDto>)((OkObjectResult)(await Controller(5).OnlineStaff("knk.admin.world")).Result!).Value!;
        Assert.Empty(empty);
    }

    [Fact]
    public async Task ActiveForMe_AsksForTheCaller_AndNeedsALogin()
    {
        var service = new Mock<IWorldTaskService>();
        service.Setup(s => s.GetActiveForUserAsync(7)).ReturnsAsync(new WorldTaskActiveForMeDto { FormName = "New town", Queued = 2 });
        var controller = new WorldTasksController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(7) }
        };

        var active = (WorldTaskActiveForMeDto)((OkObjectResult)(await controller.GetActiveForMe()).Result!).Value!;
        Assert.Equal(("New town", 2), (active.FormName, active.Queued));

        controller.ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.Plugin() };
        Assert.IsType<UnauthorizedObjectResult>((await controller.GetActiveForMe()).Result);
    }
}

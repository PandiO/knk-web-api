using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMapper;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// UserService.RecordTeleportAuditAsync (docs/specs/teleport/DESIGN.md §3.10, Phase 2): one
/// PlayerTeleported entry per staff teleport, with the body validated as if the caller were hostile.
/// </summary>
public class UserServiceTeleportAuditTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly UserService _service;
    private string? _details;

    public UserServiceTeleportAuditTests()
    {
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "Alice" });
        _users.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(new User { Id = 2, Username = "Bob" });
        _audit.Setup(a => a.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()))
            .Callback<int?, int, AuditAction, string?>((_, _, _, details) => _details = details)
            .Returns(Task.CompletedTask);

        _service = new UserService(
            _users.Object,
            new Mock<IMapper>().Object,
            new Mock<IPasswordService>().Object,
            new Mock<ILinkCodeService>().Object,
            new Mock<ITitleService>().Object,
            new Mock<IUserPermissionGroupService>().Object,
            _audit.Object,
            new Mock<IPermissionGroupRepository>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserService>.Instance);
    }

    private static TeleportAuditDto Body(int subject = 1, int? visited = 2) => new()
    {
        Kind = "STAFF",
        SubjectUserId = subject,
        VisitedUserId = visited,
        From = new TeleportAuditPointDto { World = "world", X = 0.5, Y = 64, Z = -3.5 },
        To = new TeleportAuditPointDto { World = "world_nether", X = 100.5, Y = 70, Z = 20.5 },
        Silent = true
    };

    [Fact]
    public async Task RecordsPlayerTeleportedUnderTheTargetWithTheActor()
    {
        // "/tp a b": Alice (1) moved to Bob (2) by staff member 9 - filed under the moved player.
        await _service.RecordTeleportAuditAsync(1, Body(), actorUserId: 9);

        _audit.Verify(a => a.RecordAsync(9, 1, AuditAction.PlayerTeleported, It.IsAny<string?>()), Times.Once);
        using var json = JsonDocument.Parse(_details!);
        var root = json.RootElement;
        Assert.Equal("STAFF", root.GetProperty("kind").GetString());
        Assert.Equal("Alice", root.GetProperty("subjectUsername").GetString());
        Assert.Equal(2, root.GetProperty("visitedUserId").GetInt32());
        Assert.Equal("Bob", root.GetProperty("visitedUsername").GetString());
        Assert.Equal("world", root.GetProperty("from").GetProperty("world").GetString());
        Assert.Equal(-3.5, root.GetProperty("from").GetProperty("z").GetDouble());
        Assert.Equal("world_nether", root.GetProperty("to").GetProperty("world").GetString());
        Assert.True(root.GetProperty("silent").GetBoolean());
        Assert.Equal("command", root.GetProperty("via").GetString());
    }

    [Fact]
    public async Task VisitedPlayerMayBeTheTarget()
    {
        // "/tp Bob" by Alice: the entry belongs on Bob's profile.
        await _service.RecordTeleportAuditAsync(2, Body(), actorUserId: 1);

        _audit.Verify(a => a.RecordAsync(1, 2, AuditAction.PlayerTeleported, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CoordinateTeleportWithoutVisitedPlayerAndConsoleActor()
    {
        var body = Body(visited: null);
        body.Kind = "staff";
        body.Via = "console";

        await _service.RecordTeleportAuditAsync(1, body);

        _audit.Verify(a => a.RecordAsync(null, 1, AuditAction.PlayerTeleported, It.IsAny<string?>()), Times.Once);
        using var json = JsonDocument.Parse(_details!);
        Assert.Equal("STAFF", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("visitedUsername").ValueKind);
        Assert.Equal("console", json.RootElement.GetProperty("via").GetString());
    }

    [Fact]
    public async Task UnknownTargetIsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.RecordTeleportAuditAsync(404, Body(subject: 404)));
        _audit.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> InvalidBodies()
    {
        yield return new object[] { "target is neither moved nor visited", (Action<TeleportAuditDto>)(b => b.VisitedUserId = null), 2 };
        yield return new object[] { "unknown kind", (Action<TeleportAuditDto>)(b => b.Kind = "TELEPORTER"), 1 };
        yield return new object[] { "missing kind", (Action<TeleportAuditDto>)(b => b.Kind = null!), 1 };
        yield return new object[] { "unknown visited user", (Action<TeleportAuditDto>)(b => b.VisitedUserId = 77), 1 };
        yield return new object[] { "unknown subject user", (Action<TeleportAuditDto>)(b => b.SubjectUserId = 77), 2 };
        yield return new object[] { "missing from", (Action<TeleportAuditDto>)(b => b.From = null!), 1 };
        yield return new object[] { "blank world", (Action<TeleportAuditDto>)(b => b.To.World = " "), 1 };
        yield return new object[] { "overlong world", (Action<TeleportAuditDto>)(b => b.To.World = new string('w', 65)), 1 };
        yield return new object[] { "NaN coordinate", (Action<TeleportAuditDto>)(b => b.To.Y = double.NaN), 1 };
        yield return new object[] { "coordinate out of the world", (Action<TeleportAuditDto>)(b => b.From.X = 40_000_000), 1 };
        yield return new object[] { "overlong reason", (Action<TeleportAuditDto>)(b => b.Reason = new string('r', 257)), 1 };
        yield return new object[] { "unknown via", (Action<TeleportAuditDto>)(b => b.Via = "web"), 1 };
        yield return new object[] { "bad domain id", (Action<TeleportAuditDto>)(b => b.DomainId = 0), 1 };
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task InvalidBodyIsRejectedWithoutRecording(string because, Action<TeleportAuditDto> spoil, int target)
    {
        var body = Body();
        spoil(body);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RecordTeleportAuditAsync(target, body, 9));
        Assert.True(_details == null, because);
    }

    [Fact]
    public async Task MissingBodyIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RecordTeleportAuditAsync(1, null!));
    }
}

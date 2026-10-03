using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Services.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// GDPR deletion for players and staff (developer decisions 2026-10-03): signed-in players request,
/// confirm by the emailed link (anonymous), see and cancel their own request without staff notes;
/// staff with knk.admin.privacy.request file and cancel for a player.
/// </summary>
public class DataDeletionControllerTests : IDisposable
{
    private readonly TelemetryTestDb _db = new();
    private string? _token;

    public DataDeletionControllerTests()
    {
        _db.Email.Setup(e => e.SendConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .Callback<string, string, string, DateTime>((_, _, url, _) => _token = Uri.UnescapeDataString(url[(url.IndexOf("token=", StringComparison.Ordinal) + 6)..]))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    private DataDeletionController As(HttpContext http) =>
        new(_db.Privacy()) { ControllerContext = new ControllerContext { HttpContext = http } };

    private static int? Status<T>(ActionResult<T> result) => result.Result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => null
    };

    private static T Body<T>(ActionResult<T> result) => (T)((ObjectResult)result.Result!).Value!;

    [Theory]
    [InlineData(nameof(DataDeletionController.GetMine))]
    [InlineData(nameof(DataDeletionController.RequestMine))]
    [InlineData(nameof(DataDeletionController.CancelMine))]
    public void PlayerRoutes_NeedASignedInUser(string action)
    {
        Assert.NotNull(typeof(DataDeletionController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Confirm_IsAnonymous_TheLinkIsTheProof()
    {
        Assert.NotNull(typeof(DataDeletionController).GetMethod(nameof(DataDeletionController.Confirm))!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(DataDeletionController.GetForPlayer))]
    [InlineData(nameof(DataDeletionController.FileForPlayer))]
    [InlineData(nameof(DataDeletionController.CancelForPlayer))]
    public async Task StaffRoutes_NeedTheRequestNode(string action)
    {
        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAsync(7, StaffPermissions.RequestDataDeletion))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });

        Assert.Equal(StatusCodes.Status401Unauthorized, await ServiceAuthTestHelper.RunGates(typeof(DataDeletionController), action, ServiceAuthTestHelper.Anonymous(), permissions.Object));
        Assert.Equal(StatusCodes.Status403Forbidden, await ServiceAuthTestHelper.RunGates(typeof(DataDeletionController), action, ServiceAuthTestHelper.WebUser(1), permissions.Object));
        Assert.Null(await ServiceAuthTestHelper.RunGates(typeof(DataDeletionController), action, ServiceAuthTestHelper.WebUser(7), permissions.Object));
        Assert.Equal("knk.admin.privacy.request", StaffPermissions.RequestDataDeletion);
    }

    [Fact]
    public async Task Player_RequestsConfirmsAndCancels()
    {
        var alice = As(ServiceAuthTestHelper.WebUser(1));
        Assert.Equal(204, Status(await alice.GetMine(default)));

        var requested = await alice.RequestMine(default);
        Assert.Equal(202, Status(requested));
        Assert.Equal(PrivacyRequestStatus.AwaitingConfirmation, Body(requested).Status);

        Assert.Equal(400, Status(await As(ServiceAuthTestHelper.Anonymous()).Confirm(new PrivacyDeletionConfirmDto { Token = "nope" }, default)));
        var confirmed = await As(ServiceAuthTestHelper.Anonymous()).Confirm(new PrivacyDeletionConfirmDto { Token = _token }, default);
        Assert.Equal(200, Status(confirmed));
        Assert.Equal((PrivacyRequestStatus.Pending, (DateTime?)TelemetryTestDb.Now.AddDays(5)), (Body(confirmed).Status, Body(confirmed).ScheduledAt));

        Assert.Equal(409, Status(await alice.RequestMine(default))); // AlreadyScheduled
        Assert.Equal(PrivacyRequestStatus.Pending, Body(await alice.GetMine(default)).Status);
        Assert.Equal(PrivacyRequestStatus.Cancelled, Body(await alice.CancelMine(default)).Status);
        Assert.Equal(404, Status(await alice.CancelMine(default)));
    }

    [Fact]
    public async Task Player_WithoutEmail_OrSignedOut_IsRefused()
    {
        Assert.Equal(409, Status(await As(ServiceAuthTestHelper.WebUser(2)).RequestMine(default))); // EmailRequired
        Assert.IsType<UnauthorizedResult>((await As(ServiceAuthTestHelper.Anonymous()).RequestMine(default)).Result);
    }

    [Fact]
    public async Task Staff_FilesForAPlayer_ThePlayerSeesItWithoutTheNote_AndStaffCanCancel()
    {
        var staff = As(ServiceAuthTestHelper.WebUser(7));

        Assert.Equal(400, Status(await staff.FileForPlayer(1, new PrivacyDeletionRequestCreateDto { Note = new string('x', 501) }, default)));
        Assert.Equal(404, Status(await staff.FileForPlayer(99, null, default)));
        var filed = await staff.FileForPlayer(1, new PrivacyDeletionRequestCreateDto { Note = "asked on Discord" }, default);
        Assert.Equal(201, Status(filed));
        Assert.Equal((PrivacyRequestSource.Staff, PrivacyRequestStatus.Pending, 7, "asked on Discord"),
            (Body(filed).Source, Body(filed).Status, Body(filed).RequestedByUserId, Body(filed).Note));
        Assert.Equal(409, Status(await staff.FileForPlayer(1, null, default)));

        var seenByPlayer = Body(await As(ServiceAuthTestHelper.WebUser(1)).GetMine(default));
        Assert.Null(seenByPlayer.Note);
        Assert.Equal("asked on Discord", Body(await staff.GetForPlayer(1, default)).Note);

        Assert.Equal(PrivacyRequestStatus.Cancelled, Body(await staff.CancelForPlayer(1, default)).Status);
        Assert.Equal(204, Status(await staff.GetForPlayer(1, default)));
        Assert.Equal(404, Status(await staff.CancelForPlayer(1, default)));
    }
}

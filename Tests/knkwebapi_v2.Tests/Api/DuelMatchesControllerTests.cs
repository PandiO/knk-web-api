using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Duel;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Api;

/// <summary>Arena duels (KNG-213): refusal codes map to HTTP statuses, and lock-in's Idempotency-Key must name the token.</summary>
public class DuelMatchesControllerTests
{
    private readonly Mock<IDuelMatchService> _service = new();

    private DuelMatchesController Controller(string? idempotencyKey = null)
    {
        var http = new DefaultHttpContext();
        if (idempotencyKey != null) http.Request.Headers["Idempotency-Key"] = idempotencyKey;
        return new DuelMatchesController(_service.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Theory]
    [InlineData(DuelErrorCode.MatchNotFound, 404)]
    [InlineData(DuelErrorCode.WrongState, 409)]
    [InlineData(DuelErrorCode.TokenReused, 409)]
    [InlineData(DuelErrorCode.InvalidRequest, 400)]
    [InlineData(DuelErrorCode.PairCooldown, 422)]
    [InlineData(DuelErrorCode.ReviewHoldOpen, 422)]
    public async Task Refusals_MapToStatuses(DuelErrorCode code, int status)
    {
        _service.Setup(s => s.CompleteAsync(1, It.IsAny<DuelMatchCompleteDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuelException(code, "no"));

        var result = await Controller().Complete(1, new DuelMatchCompleteDto());

        Assert.Equal(status, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task Create_201ForANewMatch_200ForAReplay_400ForAKeyThatIsntTheToken()
    {
        var token = Guid.NewGuid().ToString();
        var dto = new DuelMatchCreateDto { PublicToken = token };
        _service.SetupSequence(s => s.CreateAsync(dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new DuelMatchDto { Id = 9 }, true))
            .ReturnsAsync((new DuelMatchDto { Id = 9 }, false));

        Assert.IsType<CreatedResult>(await Controller($"duel-lock:{token}").Create(dto));
        Assert.IsType<OkObjectResult>(await Controller().Create(dto));
        Assert.Equal(400, ((ObjectResult)await Controller("duel-lock:something-else").Create(dto)).StatusCode);
        _service.Verify(s => s.CreateAsync(dto, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ===== Review data is staff-only (developer, 2026-10-21) =====

    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private static DuelMatchDto Flagged() => new()
    {
        Id = 9,
        Review = DuelReviewState.Flagged,
        ReviewTier = 1,
        ReviewSignals = new List<string> { "S1", "S4" },
        ReviewDetails = JsonDocument.Parse("{\"durationSeconds\":4}").RootElement.Clone(),
        WinnerUserId = 1
    };

    private DuelMatchesController As(HttpContext http, bool staff)
    {
        _permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), StaffPermissions.DuelReview))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = staff ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared });
        return new DuelMatchesController(_service.Object, _permissions.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Fact]
    public async Task APlayer_SeesTheReviewStateAndResult_ButNotTheReviewData()
    {
        _service.Setup(s => s.GetByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(Flagged());
        _service.Setup(s => s.QueryAsync(null, null, null, null, 50, It.IsAny<CancellationToken>())).ReturnsAsync(new List<DuelMatchDto> { Flagged() });
        _service.Setup(s => s.GetByTokenAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(Flagged());

        var player = As(ServiceAuthTestHelper.WebUser(5), staff: false);
        var byId = (DuelMatchDto)((OkObjectResult)await player.GetById(9)).Value!;
        var list = (List<DuelMatchDto>)((OkObjectResult)await player.Query(null, null, null, null)).Value!;
        var byToken = (DuelMatchDto)((OkObjectResult)await player.GetByToken("t")).Value!;

        foreach (var match in new[] { byId, list.Single(), byToken })
        {
            Assert.Equal((DuelReviewState.Flagged, 1), (match.Review, match.WinnerUserId!.Value));
            Assert.Null(match.ReviewTier);
            Assert.Empty(match.ReviewSignals);
            Assert.Null(match.ReviewDetails);
        }
    }

    [Fact]
    public async Task StaffWithTheReviewNode_AndThePlugin_SeeTheReviewData()
    {
        _service.Setup(s => s.GetByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(() => Flagged());
        _service.Setup(s => s.ReleaseAsync(9, It.IsAny<int?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new DuelResultDto { Match = Flagged() });

        var staff = (DuelMatchDto)((OkObjectResult)await As(ServiceAuthTestHelper.WebUser(900), staff: true).GetById(9)).Value!;
        var plugin = (DuelMatchDto)((OkObjectResult)await As(ServiceAuthTestHelper.Plugin(), staff: false).GetById(9)).Value!;
        var released = (DuelResultDto)((OkObjectResult)await As(ServiceAuthTestHelper.WebUser(900), staff: true).Release(9, null)).Value!;

        Assert.Equal(new[] { "S1", "S4" }, staff.ReviewSignals);
        Assert.Equal(1, plugin.ReviewTier);
        Assert.NotNull(plugin.ReviewDetails);
        Assert.Equal(1, released.Match.ReviewTier);
    }
}

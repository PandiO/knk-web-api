using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
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
}

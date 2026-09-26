using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>KNG-18 Phase 2: status codes of api/users/{id}/ignores (DESIGN.md §3.2).</summary>
[Trait("Category", "API")]
public class UserIgnoresControllerTests
{
    private readonly Mock<IUserIgnoreService> _service = new();
    private UserIgnoresController Controller => new(_service.Object);

    [Fact]
    public async Task Get_ReturnsTheList()
    {
        var list = new List<UserIgnoreDto> { new() { IgnoredUserId = 2, IgnoredUsername = "bob", CreatedAt = DateTime.UtcNow } };
        _service.Setup(s => s.GetAsync(1)).ReturnsAsync(list);

        var result = await Controller.Get(1);

        Assert.Same(list, Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task Get_UnknownUser_Is404()
    {
        _service.Setup(s => s.GetAsync(99)).ReturnsAsync((List<UserIgnoreDto>?)null);

        Assert.IsType<NotFoundObjectResult>((await Controller.Get(99)).Result);
    }

    [Theory]
    [InlineData(UserIgnoreAddResult.Ignored, 204, null)]
    [InlineData(UserIgnoreAddResult.SelfIgnore, 400, "SelfIgnore")]
    [InlineData(UserIgnoreAddResult.CannotIgnoreStaff, 400, "CannotIgnoreStaff")]
    [InlineData(UserIgnoreAddResult.IgnoreLimitReached, 409, "IgnoreLimitReached")]
    [InlineData(UserIgnoreAddResult.UserNotFound, 404, "UserNotFound")]
    public async Task Put_MapsEachOutcome(UserIgnoreAddResult outcome, int status, string? error)
    {
        _service.Setup(s => s.AddAsync(1, 2)).ReturnsAsync(outcome);

        var result = await Controller.Add(1, 2);

        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
        if (error != null)
        {
            var body = Assert.IsAssignableFrom<ObjectResult>(result).Value!;
            Assert.Equal(error, body.GetType().GetProperty("error")!.GetValue(body));
        }
    }

    [Fact]
    public async Task Delete_IsAlways204()
    {
        Assert.IsType<NoContentResult>(await Controller.Remove(1, 2));
        _service.Verify(s => s.RemoveAsync(1, 2), Times.Once);
    }
}

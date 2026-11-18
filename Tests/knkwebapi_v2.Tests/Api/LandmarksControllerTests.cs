using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>Off-road #708: the landmark controller maps the service's outcomes to status codes.</summary>
public class LandmarksControllerTests
{
    private readonly Mock<ILandmarkService> _service = new();
    private LandmarksController Controller() => new(_service.Object);

    [Fact]
    public async Task GetAll_PassesTheWorld()
    {
        _service.Setup(s => s.GetAllAsync("world")).ReturnsAsync(new List<LandmarkReadDto> { new() { Id = 1, Name = "Hall" } });

        var result = await Controller().GetAll("world");

        var list = Assert.IsAssignableFrom<IReadOnlyList<LandmarkReadDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Hall", Assert.Single(list).Name);
    }

    [Fact]
    public async Task GetById_Missing_Is404()
    {
        Assert.IsType<NotFoundResult>(await Controller().GetById(3));
    }

    [Fact]
    public async Task Create_Is201_OrA400WithTheReason()
    {
        _service.Setup(s => s.CreateAsync(It.Is<LandmarkCreateDto>(d => d.Name == "Hall"))).ReturnsAsync(new LandmarkReadDto { Id = 4, Name = "Hall" });
        _service.Setup(s => s.CreateAsync(It.Is<LandmarkCreateDto>(d => d.Name == "Taken"))).ThrowsAsync(new ArgumentException("already exists"));

        var created = Assert.IsType<CreatedAtRouteResult>(await Controller().Create(new LandmarkCreateDto { Name = "Hall" }));
        Assert.Equal("GetLandmarkById", created.RouteName);
        Assert.Equal("already exists", Assert.IsType<BadRequestObjectResult>(await Controller().Create(new LandmarkCreateDto { Name = "Taken" })).Value);
    }

    [Fact]
    public async Task UpdateAndDelete_Missing_Are404()
    {
        _service.Setup(s => s.UpdateAsync(9, It.IsAny<LandmarkUpdateDto>())).ThrowsAsync(new KeyNotFoundException());
        _service.Setup(s => s.DeleteAsync(9)).ThrowsAsync(new KeyNotFoundException());

        Assert.IsType<NotFoundResult>(await Controller().Update(9, new LandmarkUpdateDto { Name = "x" }));
        Assert.IsType<NotFoundResult>(await Controller().Delete(9));
    }

    [Fact]
    public async Task Update_ReturnsTheSavedLandmark()
    {
        _service.Setup(s => s.UpdateAsync(2, It.IsAny<LandmarkUpdateDto>())).ReturnsAsync(new LandmarkReadDto { Id = 2, Name = "New" });

        var ok = Assert.IsType<OkObjectResult>(await Controller().Update(2, new LandmarkUpdateDto { Name = "New" }));
        Assert.Equal("New", Assert.IsType<LandmarkReadDto>(ok.Value).Name);
    }
}

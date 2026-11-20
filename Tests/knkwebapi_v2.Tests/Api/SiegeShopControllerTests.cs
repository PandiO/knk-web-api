using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Interfaces;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>GET api/SiegeShop/catalogue (siege-shop DESIGN § 6.2.2, PandiO/knk-workspace#867): status codes.</summary>
public class SiegeShopControllerTests
{
    [Fact]
    public async Task WithoutMode_IsBadRequest()
    {
        var controller = new SiegeShopController(Mock.Of<ISiegeShopService>());
        Assert.IsType<BadRequestObjectResult>((await controller.GetCatalogue(null, null)).Result);
    }

    [Fact]
    public async Task UnknownScenario_IsNotFound()
    {
        var service = new Mock<ISiegeShopService>();
        service.Setup(s => s.GetCatalogueAsync(SiegeGameMode.Siege, 9)).ThrowsAsync(new KeyNotFoundException());
        var controller = new SiegeShopController(service.Object);
        Assert.IsType<NotFoundResult>((await controller.GetCatalogue(SiegeGameMode.Siege, 9)).Result);
    }

    [Fact]
    public async Task ReturnsTheServicesCatalogue()
    {
        var catalogue = new SiegeShopCatalogueDto { GameMode = SiegeGameMode.SiegeSurvival };
        var service = new Mock<ISiegeShopService>();
        service.Setup(s => s.GetCatalogueAsync(SiegeGameMode.SiegeSurvival, null)).ReturnsAsync(catalogue);
        var controller = new SiegeShopController(service.Object);
        var ok = Assert.IsType<OkObjectResult>((await controller.GetCatalogue(SiegeGameMode.SiegeSurvival, null)).Result);
        Assert.Same(catalogue, ok.Value);
    }
}

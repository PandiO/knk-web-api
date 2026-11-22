using AutoMapper;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Landmark-domain A06 (LD19, PandiO/knk-workspace#983): <c>PUT</c> and <c>DELETE api/Domains/{id}</c> on a landmark answer
/// 403 unless the caller holds <c>knk.admin.world.landmark</c> (the game server's key passes), and with it run the
/// landmark rules through <see cref="ILandmarkService"/>. Every other domain type goes through <see cref="IDomainService"/>
/// as before, and a plain domain write without a region is still a 400 (LD11).
/// </summary>
public class DomainsControllerLandmarkTests
{
    private const int DomainStaff = 5;
    private const int LandmarkStaff = 6;

    private readonly Mock<IDomainService> _domains = new();
    private readonly Mock<ILandmarkService> _landmarks = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    public DomainsControllerLandmarkTests()
    {
        _domains.Setup(s => s.GetByIdAsync(10)).ReturnsAsync(new Landmark { Id = 10, Name = "Throne room", Description = "" });
        _domains.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(new Town { Id = 1, Name = "Cinix", Description = "", WgRegionId = "domain_1" });
        _permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((int user, string node) => new PermissionCheckResponseDto
            {
                UserId = user, Node = node,
                Result = user == LandmarkStaff && node == StaffPermissions.LandmarkManage
                    ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });
        _landmarks.Setup(s => s.UpdateFromDomainAsync(10, It.IsAny<Domain>())).ReturnsAsync(new LandmarkReadDto { Id = 10 });
    }

    private DomainsController Controller(HttpContext http) => new(_domains.Object, new Mock<IMapper>().Object,
        new Mock<IDomainWorldResolver>().Object, new Mock<IDomainWorldBackfill>().Object, _landmarks.Object, _permissions.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = http }
    };

    private static Domain Rename(string name) => new() { Name = name, Description = "" };

    private static int? Status(IActionResult result) => (result as IStatusCodeActionResult)?.StatusCode;

    [Fact]
    public async Task UpdateAndDelete_OnALandmark_WithoutTheLandmarkNode_Are403_AndChangeNothing()
    {
        var update = await Controller(ServiceAuthTestHelper.WebUser(DomainStaff)).Update(10, Rename("Hall"));
        var delete = await Controller(ServiceAuthTestHelper.WebUser(DomainStaff)).Delete(10);

        Assert.Equal((403, 403), (Status(update), Status(delete)));
        _landmarks.Verify(s => s.UpdateFromDomainAsync(It.IsAny<int>(), It.IsAny<Domain>()), Times.Never);
        _landmarks.Verify(s => s.DeleteAsync(It.IsAny<int>()), Times.Never);
        _domains.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<Domain>()), Times.Never);
        _domains.Verify(s => s.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAndDelete_OnALandmark_WithTheLandmarkNode_RunTheLandmarkRules()
    {
        var update = await Controller(ServiceAuthTestHelper.WebUser(LandmarkStaff)).Update(10, Rename("Hall"));
        var delete = await Controller(ServiceAuthTestHelper.WebUser(LandmarkStaff)).Delete(10);

        // The landmark DTO, not the tracked entity (whose loaded sub-regions point back at it).
        Assert.Equal(10, Assert.IsType<LandmarkReadDto>(Assert.IsType<OkObjectResult>(update).Value).Id);
        Assert.IsType<NoContentResult>(delete);
        _landmarks.Verify(s => s.UpdateFromDomainAsync(10, It.Is<Domain>(d => d.Name == "Hall")), Times.Once);
        _landmarks.Verify(s => s.DeleteAsync(10), Times.Once);
        _domains.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<Domain>()), Times.Never);
    }

    [Fact]
    public async Task Update_OnALandmark_ALandmarkRuleRefusal_IsA400WithTheReason()
    {
        _landmarks.Setup(s => s.UpdateFromDomainAsync(10, It.IsAny<Domain>()))
            .ThrowsAsync(new ArgumentException("Name can be at most 100 characters."));

        var result = await Controller(ServiceAuthTestHelper.WebUser(LandmarkStaff)).Update(10, Rename(new string('x', 101)));

        Assert.Equal("Name can be at most 100 characters.", Assert.IsType<BadRequestObjectResult>(result).Value);
    }

    [Fact]
    public async Task TheGameServersKey_PassesTheLandmarkCheck()
    {
        Assert.IsType<NoContentResult>(await Controller(ServiceAuthTestHelper.Plugin()).Delete(10));
        _permissions.Verify(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OtherDomainTypes_NeedNoLandmarkNode_AndGoThroughTheDomainService()
    {
        var update = await Controller(ServiceAuthTestHelper.WebUser(DomainStaff)).Update(1,
            new Domain { Name = "Cinix", Description = "", WgRegionId = "domain_1" });
        var delete = await Controller(ServiceAuthTestHelper.WebUser(DomainStaff)).Delete(1);

        Assert.IsType<OkObjectResult>(update);
        Assert.IsType<NoContentResult>(delete);
        _domains.Verify(s => s.UpdateAsync(1, It.IsAny<Domain>()), Times.Once);
        _domains.Verify(s => s.DeleteAsync(1), Times.Once);
        _landmarks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task APlainDomainWrite_WithoutARegion_IsStillA400()
    {
        var create = await Controller(ServiceAuthTestHelper.WebUser(DomainStaff)).Create(new Domain { Name = "Plain", Description = "" });
        var update = await Controller(ServiceAuthTestHelper.WebUser(DomainStaff)).Update(1, new Domain { Name = "Cinix", Description = "", WgRegionId = " " });

        Assert.Equal(DomainsController.RegionRequired, Assert.IsType<BadRequestObjectResult>(create).Value);
        Assert.Equal(DomainsController.RegionRequired, Assert.IsType<BadRequestObjectResult>(update).Value);
        _domains.Verify(s => s.CreateAsync(It.IsAny<Domain>()), Times.Never);
        _domains.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<Domain>()), Times.Never);
    }
}

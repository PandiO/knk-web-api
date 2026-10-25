using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// GET api/Auth/onboarding-info (2026-10-25, invite-only onboarding): the sign-up, link and login
/// pages learn the registration mode, whether the Cloudflare step applies and the public base URL,
/// without a login and without anything else.
/// </summary>
[Trait("Category", "API")]
public class AuthOnboardingInfoTests
{
    private static AuthController Controller(RegistrationMode mode, bool syncEnabled = false, string? publicBaseUrl = null)
    {
        var security = new SecuritySettings();
        security.Registration.Mode = mode;
        var cloudflare = new CloudflareAccessSettings { SyncEnabled = syncEnabled };
        var app = new AppSettings { PublicBaseUrl = publicBaseUrl ?? string.Empty };
        return new AuthController(
            Mock.Of<IAuthService>(),
            Mock.Of<ITokenService>(),
            Mock.Of<IWebHostEnvironment>(),
            NullLogger<AuthController>.Instance,
            Mock.Of<IUserSessionStateCache>(),
            Options.Create(security),
            Options.Create(cloudflare),
            Options.Create(app));
    }

    private static OnboardingInfoDto Body(AuthController controller) =>
        Assert.IsType<OnboardingInfoDto>(Assert.IsType<OkObjectResult>(controller.OnboardingInfo()).Value);

    [Theory]
    [InlineData(RegistrationMode.LinkCode, "LinkCode")]
    [InlineData(RegistrationMode.Invite, "Invite")]
    [InlineData(RegistrationMode.Open, "Open")]
    public void OnboardingInfo_AnswersTheConfiguredMode(RegistrationMode mode, string expected)
    {
        Assert.Equal(expected, Body(Controller(mode)).RegistrationMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnboardingInfo_SaysWhetherTheCloudflareStepApplies(bool syncEnabled)
    {
        Assert.Equal(syncEnabled, Body(Controller(RegistrationMode.Invite, syncEnabled)).AccessGate);
    }

    [Fact]
    public void OnboardingInfo_CarriesThePublicBaseUrl_WithoutATrailingSlash()
    {
        Assert.Equal("https://app.knightsandkings.net", Body(Controller(RegistrationMode.Invite, publicBaseUrl: "https://app.knightsandkings.net/")).PublicBaseUrl);
        // The reset-mail setting is the fallback, as for every link in a mail.
        Assert.Equal("http://localhost:3000", Body(Controller(RegistrationMode.Invite)).PublicBaseUrl);
    }

    [Fact]
    public void OnboardingInfo_CarriesOnlyThoseThreeValues()
    {
        // The invitable groups, limits and sync status stay on the staff-only Invitations/options.
        Assert.Equal(3, typeof(OnboardingInfoDto).GetProperties().Length);
    }

    [Fact]
    public void OnboardingInfo_WorksWithoutTheOptionalSettings()
    {
        var controller = new AuthController(Mock.Of<IAuthService>(), Mock.Of<ITokenService>(), Mock.Of<IWebHostEnvironment>(),
            NullLogger<AuthController>.Instance, Mock.Of<IUserSessionStateCache>(), Options.Create(new SecuritySettings()));

        var body = Body(controller);
        Assert.Equal("LinkCode", body.RegistrationMode);
        Assert.False(body.AccessGate);
    }
}

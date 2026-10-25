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
/// GET api/Auth/registration-info (2026-10-25, the invite-only register page): the sign-up and
/// login pages learn Security:Registration:Mode without a login, and nothing else.
/// </summary>
[Trait("Category", "API")]
public class AuthRegistrationInfoTests
{
    private static AuthController Controller(RegistrationMode mode)
    {
        var settings = new SecuritySettings();
        settings.Registration.Mode = mode;
        return new AuthController(
            Mock.Of<IAuthService>(),
            Mock.Of<ITokenService>(),
            Mock.Of<IWebHostEnvironment>(),
            NullLogger<AuthController>.Instance,
            Mock.Of<IUserSessionStateCache>(),
            Options.Create(settings));
    }

    [Theory]
    [InlineData(RegistrationMode.LinkCode, "LinkCode")]
    [InlineData(RegistrationMode.Invite, "Invite")]
    [InlineData(RegistrationMode.Open, "Open")]
    public void RegistrationInfo_AnswersTheConfiguredMode(RegistrationMode mode, string expected)
    {
        var ok = Assert.IsType<OkObjectResult>(Controller(mode).RegistrationInfo());
        var dto = Assert.IsType<RegistrationInfoDto>(ok.Value);
        Assert.Equal(expected, dto.Mode);
    }

    [Fact]
    public void RegistrationInfo_CarriesOnlyTheMode()
    {
        // The invitable groups, limits and sync status stay on the staff-only Invitations/options.
        Assert.Single(typeof(RegistrationInfoDto).GetProperties());
    }
}

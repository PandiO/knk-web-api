using AutoMapper;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Mapping;

/// <summary>
/// PandiO/knk-workspace#484: the link code shown in game (<c>formattedCode</c>, the plugin's <c>{code}</c>) reads
/// XXXX-XXXX, the same groups as the claim code and the web app's code field, not XXX-XXXXX.
/// </summary>
public class LinkCodeFormatTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<UserMappingProfile>()).CreateMapper();

    private LinkCodeResponseDto Map(string code) =>
        _mapper.Map<LinkCodeResponseDto>(new LinkCode { Code = code, ExpiresAt = DateTime.UtcNow.AddMinutes(20) });

    [Theory]
    [InlineData("7KLMZU0T", "7KLM-ZU0T")]
    [InlineData("ABCD2345", "ABCD-2345")]
    public void FormattedCode_IsTwoGroupsOfFour(string code, string expected)
    {
        var dto = Map(code);

        Assert.Equal(expected, dto.FormattedCode);
        Assert.Equal(code, dto.Code);
    }

    [Fact]
    public void FormattedCode_UsesTheSameFormatterAsTheClaimCode()
    {
        foreach (var code in new[] { LinkCodeService.GenerateCode(LinkCodeService.ValidChars), "7KLMZU0T" })
        {
            Assert.Equal(LinkCodeService.FormatClaimCode(code), Map(code).FormattedCode);
        }
    }

    [Fact]
    public void FormattedCode_ReadsBackAsTheStoredCode()
    {
        // The web app and ValidateLinkCodeAsync drop the dash, so what the game shows is what the API stores.
        var code = LinkCodeService.GenerateCode(LinkCodeService.ValidChars);

        Assert.Equal(code, Map(code).FormattedCode.Replace("-", ""));
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("")]
    public void ACodeOfAnotherLength_IsShownAsIs(string code)
    {
        Assert.Equal(code, Map(code).FormattedCode);
    }

    [Fact]
    public void FormatClaimCode_LeavesNullAlone()
    {
        Assert.Null(LinkCodeService.FormatClaimCode(null!));
    }
}

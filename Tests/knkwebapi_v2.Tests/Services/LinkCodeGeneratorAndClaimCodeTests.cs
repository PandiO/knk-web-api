using AutoMapper;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-208: the link-code generator no longer favours any character, and the admission claim code
/// (KNG-214) is generated from the Crockford alphabet, reused while active and normalised on input.
/// </summary>
public class LinkCodeGeneratorAndClaimCodeTests
{
    private readonly Mock<ILinkCodeRepository> _repo = new();
    private readonly LinkCodeService _service;

    public LinkCodeGeneratorAndClaimCodeTests()
    {
        _repo.Setup(r => r.GetLinkCodeByCodeAsync(It.IsAny<string>())).ReturnsAsync((LinkCode?)null);
        _repo.Setup(r => r.CreateAsync(It.IsAny<LinkCode>())).ReturnsAsync((LinkCode c) => { c.Id = 1; return c; });
        _service = new LinkCodeService(_repo.Object, new Mock<IUserRepository>().Object, new Mock<IMapper>().Object,
            Options.Create(new SecuritySettings { LinkCodeExpirationMinutes = 20 }));
    }

    [Theory]
    [InlineData(LinkCodeService.ValidChars)]
    [InlineData(LinkCodeService.ClaimChars)]
    public void EveryCharacterOfTheAlphabetGetsAnEqualShare(string alphabet)
    {
        // byte % 36 gave A-D 8/256 each against 7/256 for the rest (+14%). With 2,000 draws per
        // character the share of each lands within ±10% of 1/n with overwhelming probability,
        // while the old bias would put A-D at about +14%.
        const int perCharacter = 2_000;
        var draws = perCharacter * alphabet.Length;
        var counts = new Dictionary<char, int>();
        for (var i = 0; i < draws / LinkCodeService.CodeLength; i++)
        {
            foreach (var c in LinkCodeService.GenerateCode(alphabet))
            {
                counts[c] = counts.GetValueOrDefault(c) + 1;
            }
        }

        Assert.Equal(alphabet.Length, counts.Count);
        foreach (var c in alphabet)
        {
            Assert.InRange(counts[c], (int)(perCharacter * 0.9), (int)(perCharacter * 1.1));
        }
    }

    [Fact]
    public async Task GenerateCodeAsync_UsesTheLinkAlphabet()
    {
        var code = await _service.GenerateCodeAsync();
        Assert.Equal(8, code.Length);
        Assert.All(code, c => Assert.Contains(c, LinkCodeService.ValidChars));
    }

    [Fact]
    public async Task CreateClaimCode_IsEightCrockfordCharactersBoundToTheUuid()
    {
        var before = DateTime.UtcNow;
        var code = await _service.CreateClaimCodeAsync("069A79F4-44E9-4726-A5BE-FCA90E38AAF5", "Steve", TimeSpan.FromMinutes(15));

        Assert.Equal(8, code.Code.Length);
        Assert.All(code.Code, c => Assert.Contains(c, LinkCodeService.ClaimChars));
        Assert.Equal(LinkCodePurpose.Claim, code.Purpose);
        Assert.Null(code.UserId);
        Assert.Equal("069a79f4-44e9-4726-a5be-fca90e38aaf5", code.MinecraftUuid);
        Assert.Equal("Steve", code.MinecraftName);
        Assert.Equal(LinkCodeStatus.Active, code.Status);
        Assert.InRange(code.ExpiresAt, before.AddMinutes(15), DateTime.UtcNow.AddMinutes(15).AddSeconds(1));
        _repo.Verify(r => r.CreateAsync(It.IsAny<LinkCode>()), Times.Once);
    }

    [Fact]
    public async Task CreateClaimCode_ReturnsTheActiveCodeOfTheUuidInsteadOfANewOne()
    {
        var existing = new LinkCode { Id = 9, Code = "7Q2HM4XA", Purpose = LinkCodePurpose.Claim, MinecraftUuid = "uuid-1", ExpiresAt = DateTime.UtcNow.AddMinutes(5) };
        _repo.Setup(r => r.GetActiveClaimCodeByUuidAsync("uuid-1")).ReturnsAsync(existing);

        var code = await _service.CreateClaimCodeAsync("uuid-1", "Steve", TimeSpan.FromMinutes(15));

        Assert.Same(existing, code);
        _repo.Verify(r => r.CreateAsync(It.IsAny<LinkCode>()), Times.Never);
    }

    [Fact]
    public async Task CreateClaimCode_RetriesOnACollision()
    {
        var calls = 0;
        _repo.Setup(r => r.GetLinkCodeByCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(() => ++calls == 1 ? new LinkCode { Code = "TAKEN" } : null);

        await _service.CreateClaimCodeAsync("uuid-1", "Steve", TimeSpan.FromMinutes(15));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CreateClaimCode_RejectsAMissingUuidOrLifetime()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClaimCodeAsync(" ", "Steve", TimeSpan.FromMinutes(15)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.CreateClaimCodeAsync("uuid-1", "Steve", TimeSpan.Zero));
    }

    [Theory]
    [InlineData("7Q2H-M4XA", "7Q2HM4XA")]
    [InlineData("7q2h m4xa", "7Q2HM4XA")]
    [InlineData("OIL0-1lo9", "01101109")]
    [InlineData("  k7qh-m2xa ", "K7QHM2XA")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeClaimCode_UppercasesStripsSeparatorsAndFoldsLookAlikes(string? input, string expected)
    {
        Assert.Equal(expected, LinkCodeService.NormalizeClaimCode(input!));
    }

    [Fact]
    public void ANormalisedClaimCodeOnlyHoldsAlphabetCharacters()
    {
        var normalised = LinkCodeService.NormalizeClaimCode("oiluOILU");
        // U has no look-alike mapping and isn't in the alphabet: it stays, and the lookup fails.
        Assert.Equal("011U011U", normalised);
    }

    [Fact]
    public void FormatClaimCode_SplitsIntoTwoGroupsOfFour()
    {
        Assert.Equal("7Q2H-M4XA", LinkCodeService.FormatClaimCode("7Q2HM4XA"));
        Assert.Equal("ABC", LinkCodeService.FormatClaimCode("ABC"));
    }

    [Fact]
    public void TheClaimAlphabetIsCrockfordBase32()
    {
        Assert.Equal(32, LinkCodeService.ClaimChars.Length);
        Assert.DoesNotContain('I', LinkCodeService.ClaimChars);
        Assert.DoesNotContain('L', LinkCodeService.ClaimChars);
        Assert.DoesNotContain('O', LinkCodeService.ClaimChars);
        Assert.DoesNotContain('U', LinkCodeService.ClaimChars);
    }
}

using knkwebapi_v2.Services.Invitations;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Invitations;

/// <summary>Code formats (DESIGN.md § 3.3): 43-character base64url tokens, KNK-XXXXX-XXXXX Crockford codes
/// read leniently, and SHA-256 hex hashes.</summary>
public class InvitationCodesTests
{
    [Fact]
    public void PersonalTokens_Are43Base64UrlCharacters_AndDiffer()
    {
        var a = InvitationCodes.NewPersonalToken();
        var b = InvitationCodes.NewPersonalToken();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, InvitationCodes.NormalizePersonal($" {a} "));
        Assert.Null(InvitationCodes.NormalizePersonal(a[..42]));
        Assert.Null(InvitationCodes.NormalizePersonal(a[..42] + "+"));
    }

    [Theory]
    [InlineData("KNK-7QH4M-2XA9P", "7QH4M2XA9P")]
    [InlineData("knk-7qh4m-2xa9p", "7QH4M2XA9P")]
    [InlineData("7QH4M 2XA9P", "7QH4M2XA9P")]
    [InlineData("KNK-OIL00-00000", "01100" + "00000")]
    public void SharedCodes_AreNormalised(string typed, string expected)
    {
        Assert.Equal(expected, InvitationCodes.NormalizeShared(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("KNK-7QH4M-2XA9")]
    [InlineData("KNK-7QH4M-2XA9PP")]
    [InlineData("KNK-7QH4M-2XA9U")] // U is not in the alphabet
    public void SharedCodes_ThatCantBeOne_AreNull(string? typed)
    {
        Assert.Null(InvitationCodes.NormalizeShared(typed));
    }

    [Fact]
    public void NewSharedCodes_HaveTheDisplayForm_AndRoundTrip()
    {
        var code = InvitationCodes.NewSharedCode();

        Assert.Matches("^KNK-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", code);
        Assert.Equal(code.Replace("-", "")[3..], InvitationCodes.NormalizeShared(code));
    }

    [Fact]
    public void Hash_IsLowerHexSha256()
    {
        Assert.Equal("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", InvitationCodes.Hash("test"));
    }

    [Fact]
    public void ShellUsernames_CantBeMinecraftNames()
    {
        var name = InvitationCodes.NewShellUsername();

        Assert.Matches("^web-[0-9a-hjkmnp-tv-z]{10}$", name);
        Assert.DoesNotMatch("^[A-Za-z0-9_]{3,16}$", name);
    }
}

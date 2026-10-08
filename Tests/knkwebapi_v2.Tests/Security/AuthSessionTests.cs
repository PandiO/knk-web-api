using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using knkwebapi_v2.Dtos;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// Closed-alpha hardening WP4: opaque rotated refresh tokens revoked by family, and the "tv"
/// session generation checked on every request.
/// </summary>
public class AuthSessionTests : IDisposable
{
    private readonly AuthTestHarness _h = new(s => s.Jwt.RefreshReuseGraceSeconds = 0);

    public void Dispose() => _h.Dispose();

    private async Task<AuthLoginResponseDto> LoginAsync(bool rememberMe = false)
    {
        var (ok, result, error) = await _h.CreateAuthService().LoginAsync("steve@example.com", AuthTestHarness.Password, rememberMe);
        Assert.True(ok, error);
        return result!;
    }

    [Fact]
    public async Task Login_IssuesAnOpaqueRefreshToken_StoredOnlyAsAHash()
    {
        await _h.AddUserAsync();
        var login = await LoginAsync();

        Assert.Null(login.RefreshToken); // never in the JSON body
        Assert.NotNull(login.Session);
        Assert.False(_h.Tokens.ExtractExpirationAsync(login.Session!.Token).Result.HasValue); // not a JWT
        var stored = await _h.Db.RefreshTokens.SingleAsync();
        Assert.Equal(_h.Tokens.HashRefreshToken(login.Session.Token), stored.TokenHash);
        Assert.NotEqual(login.Session.Token, stored.TokenHash);
        Assert.Null(await _h.BearerProblemAsync(login.AccessToken));
    }

    [Fact]
    public async Task SessionLifetime_DependsOnRememberMe()
    {
        await _h.AddUserAsync();
        var session = (await LoginAsync(rememberMe: false)).Session!;
        Assert.False(session.RememberMe);
        Assert.InRange(session.ExpiresAt, DateTime.UtcNow.AddHours(11), DateTime.UtcNow.AddHours(13));

        var remembered = (await LoginAsync(rememberMe: true)).Session!;
        Assert.True(remembered.RememberMe);
        Assert.InRange(remembered.ExpiresAt, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));
    }

    [Fact]
    public async Task Refresh_Rotates_AndReusingTheOldToken_RevokesTheWholeFamily()
    {
        await _h.AddUserAsync();
        var auth = _h.CreateAuthService();
        var first = (await LoginAsync()).Session!.Token;

        var (ok, rotated, _) = await auth.RefreshAsync(first);
        Assert.True(ok);
        var second = rotated!.Session!.Token;
        Assert.NotEqual(first, second);
        Assert.Null(rotated.RefreshToken);

        // The old token again: reuse → 401 and the family (incl. the new token) is revoked.
        var (reuseOk, _, reuseError) = await auth.RefreshAsync(first);
        Assert.False(reuseOk);
        Assert.Equal("Invalid or expired refresh token.", reuseError);
        var (secondOk, _, _) = await auth.RefreshAsync(second);
        Assert.False(secondOk);
        Assert.All(await _h.Db.RefreshTokens.ToListAsync(), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task ConcurrentRefresh_WithinTheGraceWindow_DoesNotEndTheSession()
    {
        using var h = new AuthTestHarness(s => s.Jwt.RefreshReuseGraceSeconds = 30);
        await h.AddUserAsync();
        var auth = h.CreateAuthService();
        var (_, login, _) = await auth.LoginAsync("steve@example.com", AuthTestHarness.Password, false);
        var first = login!.Session!.Token;

        var (_, rotated, _) = await auth.RefreshAsync(first);
        var (loserOk, _, _) = await auth.RefreshAsync(first); // the other tab, a moment later
        Assert.False(loserOk);
        var (winnerOk, _, _) = await auth.RefreshAsync(rotated!.Session!.Token);
        Assert.True(winnerOk);
    }

    [Fact]
    public async Task Refresh_WithAnUnknownToken_Fails()
    {
        var (ok, result, error) = await _h.CreateAuthService().RefreshAsync("not-a-token");
        Assert.False(ok);
        Assert.Null(result);
        Assert.Equal("Invalid or expired refresh token.", error);
    }

    [Fact]
    public async Task Logout_RevokesTheSession()
    {
        await _h.AddUserAsync();
        var auth = _h.CreateAuthService();
        var token = (await LoginAsync()).Session!.Token;

        await auth.LogoutAsync(token);

        var (ok, _, _) = await auth.RefreshAsync(token);
        Assert.False(ok);
    }

    [Fact]
    public async Task RefreshJwtFromBeforeTheChange_IsNotAcceptedAsBearer()
    {
        var user = await _h.AddUserAsync();
        // What the old TokenService.GenerateRefreshTokenAsync issued: same key/issuer/audience, token_type=refresh.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestHarness.JwtSecret));
        var legacy = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("knk-api", "knk-app", new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("uid", user.Id.ToString()),
            new Claim("token_type", "refresh"),
            new Claim("tv", "0"),
        }, expires: DateTime.UtcNow.AddDays(7), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)));

        Assert.NotNull(await _h.Tokens.ValidateAccessTokenAsync(legacy)); // the signature is fine...
        Assert.NotNull(await _h.BearerProblemAsync(legacy));                // ...but it is refused as a bearer token
        // And it is no refresh token either.
        var (ok, _, _) = await _h.CreateAuthService().RefreshAsync(legacy);
        Assert.False(ok);
    }

    [Fact]
    public async Task AccessTokenWithoutTokenVersion_IsRefused()
    {
        var user = await _h.AddUserAsync();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTestHarness.JwtSecret));
        var old = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("knk-api", "knk-app",
            new[] { new Claim("uid", user.Id.ToString()) }, expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)));

        Assert.NotNull(await _h.BearerProblemAsync(old));
    }

    [Fact]
    public async Task PasswordChange_EndsOtherSessions_AndKeepsThisTabLoggedIn()
    {
        await _h.AddUserAsync();
        var auth = _h.CreateAuthService();
        var other = await LoginAsync();              // browser A
        var mine = await LoginAsync(rememberMe: true); // browser B
        Assert.Null(await _h.BearerProblemAsync(other.AccessToken));

        var (ok, result, error) = await auth.UpdateUserAsync(other.User.Id, new AuthUpdateRequestDto
        {
            CurrentPassword = AuthTestHarness.Password,
            NewPassword = "Another-Horse-7"
        }, mine.Session!.Token);
        Assert.True(ok, error);

        Assert.NotNull(await _h.BearerProblemAsync(other.AccessToken)); // A's access token: 401
        Assert.NotNull(await _h.BearerProblemAsync(mine.AccessToken));
        Assert.False((await auth.RefreshAsync(other.Session!.Token)).Ok);
        Assert.False((await auth.RefreshAsync(mine.Session!.Token)).Ok);
        Assert.Null(await _h.BearerProblemAsync(result!.AccessToken!)); // B's fresh one works
        Assert.True(result.Session!.RememberMe);
        Assert.True((await auth.RefreshAsync(result.Session.Token)).Ok);
    }

    [Fact]
    public async Task DeactivatedUser_OldAccessToken_IsRefused()
    {
        var user = await _h.AddUserAsync();
        var login = await LoginAsync();
        Assert.Null(await _h.BearerProblemAsync(login.AccessToken));

        user.IsActive = false;
        await _h.Db.SaveChangesAsync();
        await _h.Revocation.RevokeAllSessionsAsync(user.Id, "account deactivated");

        Assert.NotNull(await _h.BearerProblemAsync(login.AccessToken));
        Assert.False((await _h.CreateAuthService().RefreshAsync(login.Session!.Token)).Ok);
    }

    [Fact]
    public async Task LogoutEverywhere_EndsEverySession()
    {
        var user = await _h.AddUserAsync();
        var a = await LoginAsync();
        var b = await LoginAsync();

        await _h.CreateAuthService().RevokeAllSessionsAsync(user.Id, "sign out everywhere");

        Assert.NotNull(await _h.BearerProblemAsync(a.AccessToken));
        Assert.NotNull(await _h.BearerProblemAsync(b.AccessToken));
        Assert.Equal(1, (await _h.Db.Users.SingleAsync()).TokenVersion);
    }

    [Fact]
    public async Task ResetPassword_EndsEverySession()
    {
        var user = await _h.AddUserAsync();
        var login = await LoginAsync();
        _h.Db.LinkCodes.Add(new knkwebapi_v2.Models.LinkCode
        {
            UserId = user.Id,
            Code = knkwebapi_v2.Services.TokenService.Sha256Hex("reset-token"),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            Status = knkwebapi_v2.Models.LinkCodeStatus.Active
        });
        await _h.Db.SaveChangesAsync();

        var (ok, error) = await _h.CreateAuthService().ResetPasswordAsync(new AuthResetPasswordRequestDto
        {
            Token = "reset-token",
            NewPassword = "Another-Horse-7",
            PasswordConfirmation = "Another-Horse-7"
        });

        Assert.True(ok, error);
        Assert.NotNull(await _h.BearerProblemAsync(login.AccessToken));
    }
}

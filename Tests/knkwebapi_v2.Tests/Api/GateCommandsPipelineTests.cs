using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Map;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Overview map L7 (PandiO/knk-workspace#741, spec § 5 rule 11, A19; tier:3): <c>POST api/GateStructures/{id}/commands</c>
/// through the real routing, JWT bearer, default-deny filter and <c>RequirePermission(knk.gate.admin)</c>. Without a
/// login the answer is 401, without the node 403, and in both cases the command service is never reached (nothing is
/// sent, nothing audited).
/// </summary>
public sealed class GateCommandsPipelineTests
{
    private const string JwtSecret = "gate-commands-test-jwt-secret-0123456789abcdef";
    private const int GateAdmin = 1;
    private const int WorldStaff = 2;

    private static string Jwt(int userId)
    {
        var token = new JwtSecurityToken(
            claims: new[] { new Claim("uid", userId.ToString()) },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<(WebApplication App, HttpClient Client, Mock<IGateCommandService> Commands)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                ValidateIssuer = false,
                ValidateAudience = false,
            };
        });
        builder.Services.AddControllers(o => o.Filters.Add<DefaultCallerRequiredFilter>())
            .AddApplicationPart(typeof(GateCommandsController).Assembly);
        var permissions = new Mock<IPermissionResolutionService>();
        // The gate admin holds knk.gate.admin; world staff hold knk.admin.world (not enough).
        permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((int id, string node) => new PermissionCheckResponseDto
            {
                UserId = id,
                Node = node,
                Result = (id == GateAdmin && node == "knk.gate.admin") || (id == WorldStaff && node == "knk.admin.world")
                    ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared,
            });
        builder.Services.AddSingleton(permissions.Object);
        var commands = new Mock<IGateCommandService>(MockBehavior.Strict);
        commands.Setup(c => c.SendAsync(12, It.IsAny<GateCommandRequestDto>(), GateAdmin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GateCommandResponseDto { CommandId = "c1", GateId = 12, Action = "open", Result = "done" });
        builder.Services.AddSingleton(commands.Object);
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return (app, app.GetTestClient(), commands);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, int? userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/GateStructures/12/commands")
        {
            Content = JsonContent.Create(new { action = "open", doorId = 21 }),
        };
        if (userId is { } id) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt(id));
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Anonymous_Is401_AndNothingRuns()
    {
        var (app, client, commands) = await StartAsync();
        await using var _ = app;

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, null)).StatusCode);
        commands.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WithoutKnkGateAdmin_Is403_EvenWithKnkAdminWorld_AndNothingRuns() // A19, O17
    {
        var (app, client, commands) = await StartAsync();
        await using var _ = app;

        var response = await Post(client, WorldStaff);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("knk.gate.admin", await response.Content.ReadAsStringAsync());
        commands.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AGateAdmin_ReachesTheCommand()
    {
        var (app, client, commands) = await StartAsync();
        await using var _ = app;

        var response = await Post(client, GateAdmin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"result\":\"done\"", await response.Content.ReadAsStringAsync());
        commands.Verify(c => c.SendAsync(12, It.Is<GateCommandRequestDto>(r => r.Action == "open" && r.DoorId == 21), GateAdmin,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

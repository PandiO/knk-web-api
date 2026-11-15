using System.Reflection;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Services.Statistics;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#565 (the API part of #119): the unfreeze reason in the audit log, the frozen details in the
/// profile summary, the game sessions read for the timeline and the moderation search's filters, sort keys and list
/// fields.
/// </summary>
public class PlayerProfileApiTests
{
    // ===== 1. unfreeze reason =====

    private static UserService UserService(IUserRepository repo, IAuditLogService audit) =>
        new(repo, new Mock<IMapper>().Object, new Mock<IPasswordService>().Object, new Mock<ILinkCodeService>().Object,
            new Mock<ITitleService>().Object, new Mock<IUserPermissionGroupService>().Object, audit,
            new Mock<IPermissionGroupRepository>().Object, NullLogger<UserService>.Instance, new Mock<ICurrencyService>().Object,
            new Mock<ITitleProgressionService>().Object);

    [Theory]
    [InlineData("  Appeal accepted  ", "{\"reason\":\"Appeal accepted\"}")]
    [InlineData(null, "{\"reason\":null}")]
    [InlineData("   ", "{\"reason\":null}")]
    public async Task Unfreeze_WritesTheOptionalReason_IntoThePlayerUnfrozenEntry(string? reason, string details)
    {
        var user = new User { Id = 5, Username = "p", IsFrozen = true, FrozenReason = "griefing", FrozenByUserId = 9, FrozenAt = DateTime.UtcNow };
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(user);
        var audit = new Mock<IAuditLogService>();

        await UserService(repo.Object, audit.Object).SetFrozenAsync(5, false, reason, actorUserId: 9);

        audit.Verify(a => a.RecordAsync(9, 5, AuditAction.PlayerUnfrozen, details), Times.Once);
        Assert.False(user.IsFrozen);
        Assert.Null(user.FrozenReason); // the reason is only audited
    }

    [Fact]
    public async Task Freeze_AuditStaysAsBefore()
    {
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new User { Id = 5, Username = "p" });
        var audit = new Mock<IAuditLogService>();

        await UserService(repo.Object, audit.Object).SetFrozenAsync(5, true, "griefing", actorUserId: 9);

        audit.Verify(a => a.RecordAsync(9, 5, AuditAction.PlayerFrozen, "{\"reason\":\"griefing\"}"), Times.Once);
    }

    private static UsersController UsersController(Mock<IUserService> service) =>
        new(service.Object, new Mock<IMapper>().Object, new Mock<IPermissionResolutionService>().Object, new Mock<ISalaryService>().Object,
            new Mock<IUserProfileSummaryService>().Object, new Mock<IUserPermissionGroupService>().Object,
            new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(9) }
        };

    [Fact]
    public async Task UnfreezeEndpoint_PassesTheBodyReason_AndStillWorksWithoutABody()
    {
        var service = new Mock<IUserService>();
        var controller = UsersController(service);

        Assert.IsType<NoContentResult>(await controller.Unfreeze(5, new UnfreezePlayerDto { Reason = "Appeal accepted" }));
        Assert.IsType<NoContentResult>(await controller.Unfreeze(5));

        service.Verify(s => s.SetFrozenAsync(5, false, "Appeal accepted", 9), Times.Once);
        service.Verify(s => s.SetFrozenAsync(5, false, null, 9), Times.Once);
        var body = typeof(UsersController).GetMethod(nameof(Controllers.UsersController.Unfreeze))!.GetParameters()[1];
        Assert.Equal(Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow, body.GetCustomAttribute<FromBodyAttribute>()!.EmptyBodyBehavior);
    }

    // ===== 2. frozen details in the profile summary =====

    private static UserProfileSummaryService Summary(UserDto account, Mock<IUserRepository> users)
    {
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var titles = new Mock<ITitleService>();
        titles.Setup(t => t.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto());
        var groups = new Mock<IUserPermissionGroupService>();
        groups.Setup(g => g.GetByUserAsync(account.Id)).ReturnsAsync(new List<UserPermissionGroupDto>());
        var salaryConfig = new Mock<ISalaryConfigurationService>();
        salaryConfig.Setup(s => s.GetAsync()).ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 1m });
        return new UserProfileSummaryService(userService.Object, new Mock<IPermissionResolutionService>().Object, groups.Object,
            titles.Object, new Mock<ISalaryService>().Object, salaryConfig.Object, users.Object);
    }

    [Fact]
    public async Task ProfileSummary_CarriesWhenAndByWhomThePlayerWasFrozen()
    {
        var frozenAt = new DateTime(2026, 11, 8, 7, 30, 0, DateTimeKind.Unspecified);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(3)).ReturnsAsync(new User
        {
            Id = 3, Username = "Steve", IsFrozen = true, FrozenReason = "Griefing spawn", FrozenByUserId = 9, FrozenAt = frozenAt
        });
        users.Setup(u => u.GetByIdAsync(9)).ReturnsAsync(new User { Id = 9, Username = "Mod" });

        var result = await Summary(new UserDto { Id = 3, Username = "Steve", IsFrozen = true, FrozenReason = "Griefing spawn" }, users).GetAsync(3);

        var account = result!.Account;
        Assert.True(account.IsFrozen);
        Assert.Equal("Griefing spawn", account.FrozenReason);
        Assert.Equal(DateTime.SpecifyKind(frozenAt, DateTimeKind.Utc), account.FrozenAt);
        Assert.Equal(DateTimeKind.Utc, account.FrozenAt!.Value.Kind);
        Assert.Equal(9, account.FrozenByUserId);
        Assert.Equal("Mod", account.FrozenByUsername);
        Assert.Equal("Steve", account.Username); // the rest of UserDto is copied
    }

    [Fact]
    public async Task ProfileSummary_NotFrozen_WritesTheFreezeFieldsAsNull()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(3)).ReturnsAsync(new User { Id = 3, Username = "Steve" });

        var result = await Summary(new UserDto { Id = 3, Username = "Steve" }, users).GetAsync(3);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"isFrozen\":false", json);
        Assert.Contains("\"frozenAt\":null", json);
        Assert.Contains("\"frozenByUserId\":null", json);
        Assert.Contains("\"frozenByUsername\":null", json);
    }

    [Fact]
    public void ProfileAccountDto_CopiesEveryUserDtoProperty()
    {
        var source = new UserDto { Id = 1, Username = "a", Email = "a@b.c", Coins = 5, IsFrozen = true, FrozenReason = "r", PersonalSalaryMultiplier = 2m };
        var copy = ProfileAccountDto.From(source);
        foreach (var property in typeof(UserDto).GetProperties().Where(p => p.CanRead && p.GetSetMethod() != null && p.GetIndexParameters().Length == 0))
        {
            Assert.Equal(property.GetValue(source), property.GetValue(copy));
        }
    }

    // ===== 3. game sessions =====

    [Fact]
    public void SessionsEndpoint_IsGatedByTheProfileNode()
    {
        var method = typeof(StatisticsController).GetMethod(nameof(StatisticsController.GetSessions))!;
        var rule = Assert.Single(method.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.Equal(StaffPermissions.ManageUsers, Assert.Single(rule.Arguments!));
        Assert.Equal("users/{userId:int}/sessions", method.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Null(method.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
    }

    private static PlayerStatSession Session(int userId, int day, PlayerSessionEndReason? reason, string server = "survival", int active = 600) => new()
    {
        SessionKey = Guid.NewGuid(), UserId = userId, StartedAt = new DateTime(2026, 11, day, 10, 0, 0),
        LastHeartbeatAt = new DateTime(2026, 11, day, 10, 10, 0), EndedAt = reason == null ? null : new DateTime(2026, 11, day, 11, 0, 0),
        EndReason = reason, ActiveSeconds = active, ServerName = server
    };

    [Fact]
    public async Task Sessions_NewestFirst_Paged_WithTheContractFields()
    {
        using var db = new StatisticsTestDb();
        db.Context.PlayerStatSessions.AddRange(
            Session(1, 1, PlayerSessionEndReason.Quit), Session(1, 3, PlayerSessionEndReason.Kick, "lobby", 120),
            Session(1, 2, PlayerSessionEndReason.ServerStop), Session(1, 4, null), Session(2, 5, PlayerSessionEndReason.Quit));
        db.Context.SaveChanges();

        var first = await db.Query().GetSessionsAsync(1, 1, 2);
        Assert.Equal(4, first!.TotalCount);
        Assert.Equal(new[] { 4, 3 }, first.Items.Select(s => s.StartedAt.Day));
        Assert.Equal((1, 2), (first.PageNumber, first.PageSize));
        var open = first.Items[0];
        Assert.Null(open.EndedAt);
        Assert.Null(open.EndReason);
        var kicked = first.Items[1];
        Assert.Equal(("Kick", "lobby", 120), (kicked.EndReason, kicked.ServerId, kicked.ActiveSeconds));
        Assert.Equal(DateTimeKind.Utc, kicked.StartedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, kicked.EndedAt!.Value.Kind);
        Assert.True(Guid.TryParse(kicked.SessionKey, out _));

        var second = await db.Query().GetSessionsAsync(1, 2, 2);
        Assert.Equal(new[] { 2, 1 }, second!.Items.Select(s => s.StartedAt.Day));
        Assert.Equal(new[] { "ServerStop", "Quit" }, second.Items.Select(s => s.EndReason));
    }

    [Fact]
    public async Task Sessions_UnknownUser_IsNull_AndPageSizeIsCapped()
    {
        using var db = new StatisticsTestDb();
        Assert.Null(await db.Query().GetSessionsAsync(999, 1, 20));
        var page = await db.Query().GetSessionsAsync(1, 0, 1000);
        Assert.Equal((1, 100, 0), (page!.PageNumber, page.PageSize, page.TotalCount));
    }

    [Fact]
    public async Task SessionsEndpoint_404ForAnUnknownUser()
    {
        using var db = new StatisticsTestDb();
        var controller = new StatisticsController(new Mock<IStatisticsIngestionService>().Object, db.Query(),
            new Mock<IStatisticsVisibilityService>().Object, new Mock<knkwebapi_v2.Services.Statistics.IStatisticsViewerResolver>().Object);
        Assert.IsType<NotFoundObjectResult>((await controller.GetSessions(999)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetSessions(1)).Result);
    }

    // ===== 4. moderation search =====

    private static KnKDbContext SearchDb()
    {
        var ctx = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase("search-" + Guid.NewGuid()).Options);
        ctx.Users.AddRange(
            new User { Id = 1, Username = "alice", Uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5", IsOnline = true, ExperiencePoints = 500,
                LastSeenAt = new DateTime(2026, 11, 14), CreatedAt = new DateTime(2026, 9, 1) },
            new User { Id = 2, Username = "bob", Uuid = "11111111-2222-3333-4444-555555555555", IsFrozen = true, FrozenReason = "x",
                ExperiencePoints = 50, LastSeenAt = new DateTime(2026, 11, 15), CreatedAt = new DateTime(2026, 9, 2) },
            new User { Id = 3, Username = "carol", TransferLockedAt = new DateTime(2026, 11, 1), ExperiencePoints = 900, CreatedAt = new DateTime(2026, 9, 3) });
        ctx.SaveChanges();
        return ctx;
    }

    private static async Task<List<string>> Search(Dictionary<string, string>? filters = null, string? sortBy = null, bool desc = false,
        string? term = null)
    {
        using var ctx = SearchDb();
        var result = await new UserRepository(ctx).SearchAsync(new PagedQuery
        {
            PageSize = 50, Filters = filters, SortBy = sortBy, SortDescending = desc, SearchTerm = term
        });
        return result.Items.Select(u => u.Username).ToList();
    }

    [Theory]
    [InlineData("online", "true", new[] { "alice" })]
    [InlineData("online", "false", new[] { "bob", "carol" })]
    [InlineData("frozen", "true", new[] { "bob" })]
    [InlineData("frozen", "false", new[] { "alice", "carol" })]
    [InlineData("paymentsLocked", "true", new[] { "carol" })]
    [InlineData("paymentsLocked", "false", new[] { "alice", "bob" })]
    [InlineData("frozen", "maybe", new[] { "alice", "bob", "carol" })] // not a flag: ignored
    public async Task Search_StateFilters(string key, string value, string[] expected)
    {
        Assert.Equal(expected, await Search(new Dictionary<string, string> { [key] = value }));
    }

    [Fact]
    public async Task Search_FiltersCombine()
    {
        Assert.Empty(await Search(new Dictionary<string, string> { ["online"] = "true", ["frozen"] = "true" }));
    }

    [Theory]
    [InlineData("lastseenat", false, new[] { "carol", "alice", "bob" })] // never seen first ascending
    [InlineData("lastseenat", true, new[] { "bob", "alice", "carol" })]
    [InlineData("experiencepoints", true, new[] { "carol", "alice", "bob" })]
    [InlineData("experiencePoints", false, new[] { "bob", "alice", "carol" })]
    [InlineData("lastSeenAt", true, new[] { "bob", "alice", "carol" })]
    public async Task Search_NewSortKeys(string sortBy, bool desc, string[] expected)
    {
        Assert.Equal(expected, await Search(sortBy: sortBy, desc: desc));
    }

    [Theory]
    [InlineData("069a79f444e94726a5befca90e38aaf5")]
    [InlineData("069a79f4-44e9-4726-a5be-fca90e38aaf5")]
    [InlineData("069A79F444E9")]
    public async Task Search_UuidFilterAndTerm_MatchWithOrWithoutDashes(string uuid)
    {
        Assert.Equal(new[] { "alice" }, await Search(new Dictionary<string, string> { ["uuid"] = uuid }));
        Assert.Equal(new[] { "alice" }, await Search(term: uuid));
    }

    [Fact]
    public void ListDto_CarriesFrozenPaymentsLockedAndCreatedAt()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<UserMappingProfile>()).CreateMapper();
        var dto = mapper.Map<UserListDto>(new User
        {
            Id = 3, Username = "carol", IsFrozen = true, TransferLockedAt = new DateTime(2026, 11, 1), CreatedAt = new DateTime(2026, 9, 3, 8, 0, 0)
        });
        Assert.True(dto.IsFrozen);
        Assert.True(dto.PaymentsLocked);
        Assert.Equal(new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc), dto.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, dto.CreatedAt.Kind);
        Assert.False(mapper.Map<UserListDto>(new User { Username = "x" }).PaymentsLocked);

        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.Contains("\"isFrozen\":true", json);
        Assert.Contains("\"paymentsLocked\":true", json);
        Assert.Contains("\"createdAt\":", json);
    }
}

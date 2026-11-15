using KnKWebAPI.Controllers;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#50: the API side of the web app's group Permissions panel. The blast radius
/// (<c>GET api/PermissionGroups/{id}/member-count</c>: members of the group and of every group inheriting from it, each
/// player once) and the History list's audit search by subject (<c>GET api/audit-log?subjectType=&amp;subjectId=</c>).
/// </summary>
[Trait("Category", "API")]
public class GroupGrantsPanelApiTests
{
    private const int UserId = 12;

    // ===== member count: the repository

    private static KnKDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>Default(1) ← Knight(2) ← Lord(3); Squire(4) ← Default too; Other(5) unrelated.</summary>
    private static async Task<string> SeedTreeAsync()
    {
        var name = $"MemberCount_{Guid.NewGuid()}";
        await using var db = NewContext(name);
        db.PermissionGroups.AddRange(
            new PermissionGroup { Id = 1, Name = "Default" },
            new PermissionGroup { Id = 2, Name = "Knight", ParentGroupId = 1 },
            new PermissionGroup { Id = 3, Name = "Lord", ParentGroupId = 2 },
            new PermissionGroup { Id = 4, Name = "Squire", ParentGroupId = 1 },
            new PermissionGroup { Id = 5, Name = "Other" });
        for (var id = 100; id <= 106; id++) db.Users.Add(new User { Id = id, Username = $"p{id}" });
        var now = DateTime.UtcNow;
        db.UserPermissionGroups.AddRange(
            new UserPermissionGroup { UserId = 100, PermissionGroupId = 1 },
            new UserPermissionGroup { UserId = 101, PermissionGroupId = 1 },
            new UserPermissionGroup { UserId = 101, PermissionGroupId = 2 },   // in Default and Knight: counted once
            new UserPermissionGroup { UserId = 102, PermissionGroupId = 3 },   // Lord: reaches Knight and Default
            new UserPermissionGroup { UserId = 103, PermissionGroupId = 4 },   // Squire: reaches Default only
            new UserPermissionGroup { UserId = 104, PermissionGroupId = 2, ExpiresAt = now.AddDays(-1) }, // expired: not counted
            new UserPermissionGroup { UserId = 105, PermissionGroupId = 2, ExpiresAt = now.AddDays(5) },  // expires later: counted
            new UserPermissionGroup { UserId = 106, PermissionGroupId = 5 });
        await db.SaveChangesAsync();
        return name;
    }

    [Theory]
    [InlineData(1, 2, 5, 3)] // Default: 100, 101 + Knight's 105, Lord's 102, Squire's 103 (101 once)
    [InlineData(2, 2, 3, 1)] // Knight: 101, 105 + Lord's 102; Lord inherits
    [InlineData(3, 1, 1, 0)] // Lord: a leaf
    [InlineData(5, 1, 1, 0)] // Other: unrelated tree
    public async Task CountMembers_IncludesInheritingGroups_EachPlayerOnce_ActiveOnly(int groupId, int direct, int total, int inheriting)
    {
        var name = await SeedTreeAsync();
        await using var db = NewContext(name);

        var counts = await new PermissionGroupRepository(db).CountMembersAsync(groupId, DateTime.UtcNow);

        Assert.Equal((direct, total, inheriting), counts);
    }

    [Fact]
    public async Task CountMembers_SurvivesACycleInTheData()
    {
        var name = $"MemberCountCycle_{Guid.NewGuid()}";
        await using (var db = NewContext(name))
        {
            db.PermissionGroups.AddRange(new PermissionGroup { Id = 1, Name = "A", ParentGroupId = 2 },
                new PermissionGroup { Id = 2, Name = "B", ParentGroupId = 1 });
            db.Users.Add(new User { Id = 100, Username = "p" });
            db.UserPermissionGroups.Add(new UserPermissionGroup { UserId = 100, PermissionGroupId = 2 });
            await db.SaveChangesAsync();
        }
        await using var check = NewContext(name);

        Assert.Equal((0, 1, 1), await new PermissionGroupRepository(check).CountMembersAsync(1, DateTime.UtcNow));
    }

    // ===== member count: the endpoint

    private static PermissionGroupsController GroupsController(IPermissionGroupService service) =>
        new(service, new Mock<IPermissionEscalationGuard>().Object, new Mock<IPermissionResolutionService>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.Plugin() }
        };

    [Fact]
    public async Task MemberCount_ReturnsTheCounts_Or404ForAnUnknownGroup()
    {
        var service = new Mock<IPermissionGroupService>();
        var dto = new PermissionGroupMemberCountDto { GroupId = 2, DirectMembers = 2, TotalMembers = 3, InheritingGroups = 1 };
        service.Setup(s => s.GetMemberCountAsync(2)).ReturnsAsync(dto);
        service.Setup(s => s.GetMemberCountAsync(9)).ReturnsAsync((PermissionGroupMemberCountDto?)null);
        var controller = GroupsController(service.Object);

        Assert.Same(dto, Assert.IsType<OkObjectResult>((await controller.GetMemberCount(2)).Result).Value);
        Assert.IsType<NotFoundResult>((await controller.GetMemberCount(9)).Result);
    }

    [Fact]
    public async Task MemberCountService_IsNullForAnUnknownGroup()
    {
        var name = await SeedTreeAsync();
        await using var db = NewContext(name);
        var service = new PermissionGroupService(new PermissionGroupRepository(db),
            new AutoMapper.MapperConfiguration(c => c.AddProfile<knkwebapi_v2.Mapping.PermissionMappingProfile>()).CreateMapper());

        Assert.Null(await service.GetMemberCountAsync(99));
        var knight = await service.GetMemberCountAsync(2);
        Assert.Equal((2, 2, 3, 1), (knight!.GroupId, knight.DirectMembers, knight.TotalMembers, knight.InheritingGroups));
    }

    [Fact]
    public async Task MemberCount_IsARead_OnTheTypesReadNode()
    {
        Assert.Null(await ServiceAuthTestHelper.RunGates(typeof(PermissionGroupsController), "GetMemberCount",
            ServiceAuthTestHelper.WebUser(UserId), Holding(StaffPermissions.ManageUsers)));
        Assert.Equal(StatusCodes.Status403Forbidden, await ServiceAuthTestHelper.RunGates(typeof(PermissionGroupsController),
            "GetMemberCount", ServiceAuthTestHelper.WebUser(UserId), Holding(StaffPermissions.UserPermissions)));
        Assert.Equal(StatusCodes.Status401Unauthorized, await ServiceAuthTestHelper.RunGates(typeof(PermissionGroupsController),
            "GetMemberCount", ServiceAuthTestHelper.Anonymous()));
        Assert.Null(await ServiceAuthTestHelper.RunGates(typeof(PermissionGroupsController), "GetMemberCount", ServiceAuthTestHelper.Plugin()));
    }

    // ===== audit search by subject

    private static (AuditLogController Controller, Mock<IAuditLogService> Service) AuditController()
    {
        var service = new Mock<IAuditLogService>();
        var empty = new PagedResultDto<AuditLogEntryDto> { Items = new List<AuditLogEntryDto>(), PageNumber = 1, PageSize = 20 };
        service.Setup(s => s.SearchAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<AuditAction?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(empty);
        service.Setup(s => s.SearchAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<AuditAction?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(empty);
        return (new AuditLogController(service.Object), service);
    }

    [Fact]
    public async Task AuditLog_PassesTheSubjectFilter()
    {
        var (controller, service) = AuditController();

        Assert.IsType<OkObjectResult>(await controller.Get(null, null, null, null, "PermissionGroup", 50, 2, 10));

        service.Verify(s => s.SearchAsync(null, null, null, null, "PermissionGroup", 50, 2, 10), Times.Once);
    }

    [Fact]
    public async Task AuditLog_WithoutASubject_SearchesAsBefore()
    {
        var (controller, service) = AuditController();

        await controller.Get(10, null, AuditAction.PlayerFrozen, null, null, null, 1, 20);

        service.Verify(s => s.SearchAsync(10, null, AuditAction.PlayerFrozen, null, 1, 20), Times.Once);
        service.Verify(s => s.SearchAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<AuditAction?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("AVeryLongSubjectTypeNameThatGoesOnAndOnPastTheSixtyFourCharacterColumnLimit")]
    public async Task AuditLog_AnInvalidSubjectType_Is400(string subjectType)
    {
        var (controller, _) = AuditController();

        Assert.IsType<BadRequestObjectResult>(await controller.Get(null, null, null, null, subjectType, 50, 1, 20));
    }

    [Fact]
    public async Task AuditLogRepository_FiltersOnTypeAndId_NewestFirst()
    {
        await using var db = NewContext($"AuditSubject_{Guid.NewGuid()}");
        var t = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
        db.AuditLogEntries.AddRange(
            new AuditLogEntry { Id = 1, Timestamp = t, SubjectType = "PermissionGroup", SubjectId = 50, Action = AuditAction.GrantAdded },
            new AuditLogEntry { Id = 2, Timestamp = t.AddMinutes(1), SubjectType = "PermissionGroup", SubjectId = 51, Action = AuditAction.GrantAdded },
            new AuditLogEntry { Id = 3, Timestamp = t.AddMinutes(2), TargetUserId = 50, Action = AuditAction.GrantAdded },
            new AuditLogEntry { Id = 4, Timestamp = t.AddMinutes(3), SubjectType = "Clan", SubjectId = 50, Action = AuditAction.GrantAdded },
            new AuditLogEntry { Id = 5, Timestamp = t.AddMinutes(4), SubjectType = "PermissionGroup", SubjectId = 50, Action = AuditAction.GrantRemoved },
            new AuditLogEntry { Id = 6, Timestamp = t.AddMinutes(4), SubjectType = "PermissionGroup", SubjectId = 50, Action = AuditAction.GrantUpdated });
        await db.SaveChangesAsync();
        var repo = new AuditLogRepository(db);

        var group = await repo.SearchAsync(null, null, null, null, "PermissionGroup", 50, 1, 20);
        Assert.Equal(new[] { 6, 5, 1 }, group.Items.Select(e => e.Id));

        var allGroups = await repo.SearchAsync(null, null, null, null, "PermissionGroup", null, 1, 20);
        Assert.Equal(4, allGroups.TotalCount);

        var removals = await repo.SearchAsync(null, null, AuditAction.GrantRemoved, null, "PermissionGroup", 50, 1, 20);
        Assert.Equal(new[] { 5 }, removals.Items.Select(e => e.Id));
    }

    private static IPermissionResolutionService Holding(string node)
    {
        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAsync(UserId, It.IsAny<string>()))
            .ReturnsAsync((int userId, string asked) => new PermissionCheckResponseDto
            {
                UserId = userId,
                Node = asked,
                Result = asked == node ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });
        return permissions.Object;
    }
}

using Xunit;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for AuditLogService (docs/specs/user-management/DESIGN.md §4,
/// IMPLEMENTATION_PLAN.md Phase 2).
/// </summary>
public class AuditLogServiceTests
{
    private readonly Mock<IAuditLogRepository> _mockRepo = new();
    private readonly Mock<IUserRepository> _mockUserRepo = new();
    private readonly AuditLogService _service;

    public AuditLogServiceTests()
    {
        _service = new AuditLogService(_mockRepo.Object, _mockUserRepo.Object);
    }

    [Fact]
    public async Task RecordAsync_WritesEntryWithGivenFields()
    {
        AuditLogEntry? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<AuditLogEntry>()))
            .Callback<AuditLogEntry>(e => captured = e)
            .Returns(Task.CompletedTask);

        await _service.RecordAsync(actorUserId: 5, targetUserId: 10, AuditAction.GroupAssigned, "{\"x\":1}");

        Assert.NotNull(captured);
        Assert.Equal(5, captured!.ActorUserId);
        Assert.Equal(10, captured.TargetUserId);
        Assert.Equal(AuditAction.GroupAssigned, captured.Action);
        Assert.Equal("{\"x\":1}", captured.Details);
    }

    [Fact]
    public async Task RecordAsync_NullActor_IsAllowed()
    {
        AuditLogEntry? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<AuditLogEntry>()))
            .Callback<AuditLogEntry>(e => captured = e)
            .Returns(Task.CompletedTask);

        await _service.RecordAsync(actorUserId: null, targetUserId: 10, AuditAction.SalaryPayout);

        Assert.Null(captured!.ActorUserId);
    }

    [Fact]
    public async Task RecordAsync_InvalidTargetUserId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RecordAsync(null, 0, AuditAction.SalaryPayout));
    }

    [Fact]
    public async Task SearchAsync_ResolvesActorAndTargetUsernames()
    {
        _mockRepo.Setup(r => r.SearchAsync(10, null, null, null, 1, 20)).ReturnsAsync(new PagedResult<AuditLogEntry>
        {
            Items = new List<AuditLogEntry>
            {
                new() { Id = 1, Timestamp = DateTime.UtcNow, ActorUserId = 5, TargetUserId = 10, Action = AuditAction.GroupAssigned }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        });
        _mockUserRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new User { Id = 5, Username = "admin1" });
        _mockUserRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new User { Id = 10, Username = "player1" });

        var result = await _service.SearchAsync(targetUserId: 10, actorUserId: null, pageNumber: 1, pageSize: 20);

        Assert.Single(result.Items);
        Assert.Equal("admin1", result.Items[0].ActorUsername);
        Assert.Equal("player1", result.Items[0].TargetUsername);
        Assert.Equal("GroupAssigned", result.Items[0].Action);
    }

    [Fact]
    public async Task SearchAsync_WithActionAndDirection_PassesThroughToRepository()
    {
        _mockRepo.Setup(r => r.SearchAsync(null, null, AuditAction.TitleChanged, "demotion", 1, 20))
            .ReturnsAsync(new PagedResult<AuditLogEntry>
            {
                Items = new List<AuditLogEntry>
                {
                    new() { Id = 2, Timestamp = DateTime.UtcNow, ActorUserId = null, TargetUserId = 7, Action = AuditAction.TitleChanged, Details = "{\"direction\":\"demotion\"}" }
                },
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 20
            });
        _mockUserRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new User { Id = 7, Username = "demoted1" });

        var result = await _service.SearchAsync(targetUserId: null, actorUserId: null, AuditAction.TitleChanged, "demotion", pageNumber: 1, pageSize: 20);

        Assert.Single(result.Items);
        Assert.Equal("demoted1", result.Items[0].TargetUsername);
        Assert.Equal("TitleChanged", result.Items[0].Action);
    }
}

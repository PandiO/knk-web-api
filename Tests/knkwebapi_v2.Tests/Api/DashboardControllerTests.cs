using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// POST api/Dashboard/bulk-delete and /export (PandiO/knk-workspace#280, DESIGN.md § 4.7, § 4.8): each missing node
/// answers 403, users and groups 400, more than 100 ids 400, per-row refusals with their reason, the 10 000-row cap,
/// one audit entry per call.
/// </summary>
public class DashboardControllerTests
{
    private const int Staff = 7;

    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly Mock<IDashboardActionService> _actions = new();

    public DashboardControllerTests()
    {
        _permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((int userId, string node) => new PermissionCheckResponseDto
            {
                UserId = userId,
                Node = node,
                Result = _held.Contains(node) ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });
        _actions.Setup(a => a.BulkDeleteAsync(It.IsAny<DashboardTypeAccess>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>()))
            .ReturnsAsync(new DashboardBulkDeleteResponseDto());
        _actions.Setup(a => a.ExportAsync(It.IsAny<DashboardTypeAccess>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<DashboardExportQueryDto?>(), It.IsAny<int>()))
            .ReturnsAsync(new DashboardExportResponseDto());
    }

    private DashboardController Controller() => new(_actions.Object, _permissions.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(Staff) }
    };

    private Task<int?> Gates(string action, HttpContext http) =>
        ServiceAuthTestHelper.RunGates(typeof(DashboardController), action, http, _permissions.Object);

    private static int Status(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        _ => -1
    };

    // ===== gates =====

    [Fact]
    public async Task BulkDelete_NeedsBothActionNodes_AndAWebLogin()
    {
        Assert.Equal(401, await Gates(nameof(DashboardController.BulkDelete), ServiceAuthTestHelper.Anonymous()));
        Assert.Equal(401, await Gates(nameof(DashboardController.BulkDelete), ServiceAuthTestHelper.Plugin())); // web users only
        Assert.Equal(403, await Gates(nameof(DashboardController.BulkDelete), ServiceAuthTestHelper.WebUser(Staff)));
        _held.Add(StaffPermissions.Delete);
        Assert.Equal(403, await Gates(nameof(DashboardController.BulkDelete), ServiceAuthTestHelper.WebUser(Staff)));
        _held.Remove(StaffPermissions.Delete);
        _held.Add(StaffPermissions.DeleteBulk);
        Assert.Equal(403, await Gates(nameof(DashboardController.BulkDelete), ServiceAuthTestHelper.WebUser(Staff)));
        _held.Add(StaffPermissions.Delete);
        Assert.Null(await Gates(nameof(DashboardController.BulkDelete), ServiceAuthTestHelper.WebUser(Staff)));
    }

    [Fact]
    public async Task Export_NeedsTheExportNode_AndAWebLogin()
    {
        Assert.Equal(401, await Gates(nameof(DashboardController.Export), ServiceAuthTestHelper.Plugin()));
        Assert.Equal(403, await Gates(nameof(DashboardController.Export), ServiceAuthTestHelper.WebUser(Staff)));
        _held.Add(StaffPermissions.Export);
        Assert.Null(await Gates(nameof(DashboardController.Export), ServiceAuthTestHelper.WebUser(Staff)));
    }

    // ===== bulk delete =====

    [Fact]
    public async Task BulkDelete_WithoutTheTypesAreaNode_Is403_AndDeletesNothing()
    {
        _held.Add(StaffPermissions.ManageContent);
        var result = await Controller().BulkDelete(new DashboardBulkDeleteRequestDto { EntityType = "Town", Ids = new() { 1 } });

        Assert.Equal(403, Status(result));
        Assert.Contains("knk.admin.world", ((ObjectResult)result).Value!.ToString());
        _actions.Verify(a => a.BulkDeleteAsync(It.IsAny<DashboardTypeAccess>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("PermissionGroup")]
    [InlineData("CategoryTag")] // view-only
    public async Task BulkDelete_OfUsersGroupsAndViewOnlyTypes_Is400(string type)
    {
        _held.Add(StaffPermissions.ManageUsers);
        _held.Add(StaffPermissions.ManageContent);
        var result = await Controller().BulkDelete(new DashboardBulkDeleteRequestDto { EntityType = type, Ids = new() { 1 } });
        Assert.Equal(400, Status(result));
        Assert.Contains("BulkDeleteNotAllowed", ((ObjectResult)result).Value!.ToString());
    }

    [Fact]
    public async Task BulkDelete_OfAnUnknownType_Is404()
    {
        var result = await Controller().BulkDelete(new DashboardBulkDeleteRequestDto { EntityType = "FormConfiguration", Ids = new() { 1 } });
        Assert.Equal(404, Status(result));
    }

    [Fact]
    public async Task BulkDelete_TakesAtMost100DistinctIds()
    {
        _held.Add(StaffPermissions.ManageWorld);
        var tooMany = await Controller().BulkDelete(new DashboardBulkDeleteRequestDto { EntityType = "Town", Ids = Enumerable.Range(1, 101).ToList() });
        Assert.Equal(400, Status(tooMany));
        Assert.Contains("TooManyIds", ((ObjectResult)tooMany).Value!.ToString());

        // Duplicates count once: 100 distinct ids sent with repeats pass.
        var ids = Enumerable.Range(1, 100).Concat(new[] { 1, 2, 3 }).ToList();
        var ok = await Controller().BulkDelete(new DashboardBulkDeleteRequestDto { EntityType = "town", Ids = ids });
        Assert.Equal(200, Status(ok));
        _actions.Verify(a => a.BulkDeleteAsync(It.Is<DashboardTypeAccess>(t => t.TypeName == "Town"),
            It.Is<IReadOnlyList<int>>(l => l.Count == 100), Staff), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_WithoutIds_Is400()
    {
        _held.Add(StaffPermissions.ManageWorld);
        Assert.Equal(400, Status(await Controller().BulkDelete(new DashboardBulkDeleteRequestDto { EntityType = "Town" })));
        Assert.Equal(400, Status(await Controller().BulkDelete(new DashboardBulkDeleteRequestDto { EntityType = "Town", Ids = new() { 0 } })));
        Assert.Equal(400, Status(await Controller().BulkDelete(new DashboardBulkDeleteRequestDto { Ids = new() { 1 } })));
    }

    // ===== export =====

    [Fact]
    public async Task Export_WithoutTheTypesAreaNode_Is403()
    {
        _held.Add(StaffPermissions.ManageWorld);
        var result = await Controller().Export(new DashboardExportRequestDto { EntityType = "ItemBlueprint" });
        Assert.Equal(403, Status(result));
    }

    [Fact]
    public async Task Export_OfATypeWithoutAList_Is400()
    {
        _held.Add(StaffPermissions.RoadManage);
        var result = await Controller().Export(new DashboardExportRequestDto { EntityType = "RoadProfile" });
        Assert.Equal(400, Status(result));
    }

    [Fact]
    public async Task Export_TakesAtMost10000Ids_AndPassesTheQuery()
    {
        _held.Add(StaffPermissions.ManageKits);
        var tooMany = await Controller().Export(new DashboardExportRequestDto { EntityType = "Kit", Ids = Enumerable.Range(1, 10_001).ToList() });
        Assert.Equal(400, Status(tooMany));

        var query = new DashboardExportQueryDto { SearchTerm = "starter", SortBy = "name" };
        Assert.Equal(200, Status(await Controller().Export(new DashboardExportRequestDto { EntityType = "Kit", Query = query })));
        _actions.Verify(a => a.ExportAsync(It.Is<DashboardTypeAccess>(t => t.TypeName == "Kit"), null, query, Staff), Times.Once);
    }
}

/// <summary>The bulk delete and export run through each type's own services (DashboardActionService).</summary>
public class DashboardActionServiceTests
{
    private const int Actor = 7;

    private readonly Mock<ICategoryService> _categories = new();
    private readonly Mock<ITownService> _towns = new();
    private readonly Mock<IEntityReferenceService> _references = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly DashboardActionService _service;

    public DashboardActionServiceTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _categories.Object);
        services.AddScoped(_ => _towns.Object);
        services.AddScoped(_ => _references.Object);
        var provider = services.BuildServiceProvider();
        _service = new DashboardActionService(provider.GetRequiredService<IServiceScopeFactory>(), _audit.Object,
            NullLogger<DashboardActionService>.Instance);
    }

    private static DashboardTypeAccess Type(string name) => DashboardAccess.Find(name)!;

    [Fact]
    public async Task BulkDelete_DeletesEachRow_AndGivesEveryRefusalItsReason()
    {
        _categories.Setup(c => c.DeleteAsync(2)).ThrowsAsync(new KeyNotFoundException());
        _categories.Setup(c => c.DeleteAsync(3)).ThrowsAsync(new InvalidOperationException("Category has child categories."));
        _categories.Setup(c => c.DeleteAsync(4)).ThrowsAsync(new DbUpdateException("fk", new Exception("FK")));
        _categories.Setup(c => c.DeleteAsync(5)).ThrowsAsync(new TimeoutException("db at 10.0.0.5 timed out"));
        _references.Setup(r => r.GetReferencesAsync("Category", "4", null, true)).ReturnsAsync(new EntityReferencesDto
        {
            EntityTypeName = "Category", Id = "4", InUse = true,
            References = new() { new EntityReferenceCountDto { ReferencedBy = "ItemBlueprint.CategoryId", Count = 2 } }
        });

        var result = await _service.BulkDeleteAsync(Type("Category"), new[] { 1, 2, 3, 4, 5, 6 }, Actor);

        Assert.Equal(new[] { 1, 6 }, result.Deleted);
        Assert.Equal(new[]
        {
            (2, DashboardActionService.NotFoundReason),
            (3, "Category has child categories."),
            (4, "This category can't be deleted: it is still used by 2 item blueprints. Remove or change those first."),
            (5, DashboardActionService.FailedReason), // an unexpected error's text is never shown
        }, result.Refused.Select(r => (r.Id, r.Reason)));
        // AM3: the code says which refusal has rows behind it ("Show what uses it").
        Assert.Equal(new[] { "NotFound", "Refused", "InUse", "Failed" }, result.Refused.Select(r => r.Code));
        _categories.Verify(c => c.DeleteAsync(It.IsAny<int>()), Times.Exactly(6));
        _audit.Verify(a => a.RecordAsync(Actor, Actor, AuditAction.DashboardBulkDelete,
            It.Is<string>(d => d.Contains("\"entityType\":\"Category\"") && d.Contains("\"ids\":[1,6]") && d.Contains("\"refused\":4"))), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_OfAViewOnlyType_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.BulkDeleteAsync(Type("CategoryTag"), new[] { 1 }, Actor));
    }

    private void Towns(int total)
    {
        _towns.Setup(t => t.SearchAsync(It.IsAny<PagedQueryDto>())).ReturnsAsync((PagedQueryDto q) => new PagedResultDto<TownListDto>
        {
            Items = Enumerable.Range((q.PageNumber - 1) * q.PageSize + 1, Math.Max(0, Math.Min(q.PageSize, total - (q.PageNumber - 1) * q.PageSize)))
                .Select(i => new TownListDto { id = i, name = $"Town {i}" }).ToList(),
            TotalCount = total,
            PageNumber = q.PageNumber,
            PageSize = q.PageSize,
        });
    }

    [Fact]
    public async Task Export_OfASearch_ReadsEveryPageInPagesOf500_KeepingTheQuery()
    {
        Towns(1203);
        var query = new DashboardExportQueryDto { SearchTerm = "a", SortBy = "name", SortDescending = true };

        var result = await _service.ExportAsync(Type("Town"), null, query, Actor);

        Assert.Equal(1203, result.Items.Count);
        Assert.Equal(1203, result.Total);
        Assert.False(result.Truncated);
        _towns.Verify(t => t.SearchAsync(It.Is<PagedQueryDto>(q => q.PageSize == 500 && q.SearchTerm == "a" && q.SortBy == "name" && q.SortDescending)),
            Times.Exactly(3));
        _audit.Verify(a => a.RecordAsync(Actor, Actor, AuditAction.DashboardExport,
            It.Is<string>(d => d.Contains("\"scope\":\"search\"") && d.Contains("\"count\":1203"))), Times.Once);
    }

    [Fact]
    public async Task Export_StopsAt10000Rows_AndSaysItIsTruncated()
    {
        Towns(12_345);
        var result = await _service.ExportAsync(Type("Town"), null, null, Actor);

        Assert.Equal(DashboardActionService.MaxExportRows, result.Items.Count);
        Assert.Equal(12_345, result.Total);
        Assert.True(result.Truncated);
        _towns.Verify(t => t.SearchAsync(It.IsAny<PagedQueryDto>()), Times.Exactly(20));
    }

    [Fact]
    public async Task Export_OfASelection_ReturnsTheSelectedRowsInTheSearchOrder()
    {
        Towns(1200);
        var result = await _service.ExportAsync(Type("Town"), new[] { 900, 3, 77777 }, null, Actor);

        Assert.Equal(new int?[] { 3, 900 }, result.Items.Cast<TownListDto>().Select(t => t.id));
        Assert.Equal(2, result.Total);
        Assert.False(result.Truncated); // 77777 doesn't exist: the whole list was read
        _audit.Verify(a => a.RecordAsync(Actor, Actor, AuditAction.DashboardExport, It.Is<string>(d => d.Contains("\"scope\":\"selection\""))), Times.Once);
    }

    [Fact]
    public async Task Export_OfASelection_StopsReadingOnceEveryIdIsFound()
    {
        Towns(5000);
        await _service.ExportAsync(Type("Town"), new[] { 2, 499 }, null, Actor);
        _towns.Verify(t => t.SearchAsync(It.IsAny<PagedQueryDto>()), Times.Once);
    }

    [Theory]
    [InlineData(typeof(CategoryListDto))]
    [InlineData(typeof(TownListDto))]
    [InlineData(typeof(LocationDto))]
    [InlineData(typeof(KitDto))]
    [InlineData(typeof(LootboxTypeDto))]
    [InlineData(typeof(SiegeTeamReadDto))]
    [InlineData(typeof(UserListDto))]
    [InlineData(typeof(PermissionHolderListDto))]
    [InlineData(typeof(GateDoorListDto))]
    [InlineData(typeof(MinecraftEnchantmentRefListDto))]
    public void ListRows_HaveAnId(Type dto)
    {
        var row = Activator.CreateInstance(dto)!;
        dto.GetProperties().First(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)).SetValue(row, 42);
        Assert.Equal(42, DashboardActionService.IdOf(row));
    }
}

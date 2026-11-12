using System.Reflection;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#44 under the #280 model: "Show details" of a refused delete lists the rows that still use a record.
/// The route needs one of the record type's read nodes (web users only); each group's rows are named only as far as the
/// caller may see them: full read → labels and <c>readable</c>, any area node → labels (as api/Lookup), otherwise counts.
/// </summary>
public class EntityReferencesControllerTests
{
    private const int Staff = 7;
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private readonly Mock<IPermissionReadCache> _cache = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly Mock<IEntityReferenceService> _references = new();
    private readonly Mock<ILookupService> _lookups = new();

    public EntityReferencesControllerTests()
    {
        _cache.Setup(c => c.HoldsAnyAsync(It.IsAny<IPermissionResolutionService>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<HttpContext?>()))
            .ReturnsAsync((IPermissionResolutionService _, int _, IReadOnlyList<string> nodes, HttpContext? _) =>
                nodes.Any(_held.Contains));
        _lookups.Setup(l => l.GetAsync(It.IsAny<DashboardTypeAccess>(), It.IsAny<int>()))
            .ReturnsAsync((DashboardTypeAccess type, int id) => new LookupItemDto { Id = id, Label = $"{type.TypeName} {id}", Detail = "d" });
    }

    private async Task<IActionResult?> Gate(HttpContext http, string type)
    {
        var routeData = new RouteData();
        routeData.Values[ReferenceReadFilter.TypeRouteKey] = type;
        var context = new AuthorizationFilterContext(new ActionContext(http, routeData, new ActionDescriptor()), new List<IFilterMetadata>());
        await new ReferenceReadFilter(_permissions.Object, _cache.Object).OnAuthorizationAsync(context);
        return context.Result;
    }

    private static int Status(IActionResult? result) => result switch
    {
        null => 200,
        UnauthorizedObjectResult => 401,
        ObjectResult o => o.StatusCode ?? 200,
        _ => -1
    };

    private EntityReferenceRowsService Service() => new(_references.Object, _lookups.Object, _permissions.Object, _cache.Object);

    private void StreetIsUsedBy(params ReferencingRows[] groups) =>
        _references.Setup(r => r.GetReferencingRowsAsync("Street", "12", true)).ReturnsAsync(groups);

    // ===== The rule =====

    [Fact]
    public void GetRows_CarriesItsOwnRule_AndIsReadCategoryU()
    {
        var method = typeof(EntityReferencesController).GetMethod(nameof(EntityReferencesController.GetRows))!;
        Assert.NotNull(method.GetCustomAttribute<RequireReferenceReadAttribute>());
        Assert.True(typeof(IKnkAccessRule).IsAssignableFrom(typeof(RequireReferenceReadAttribute)));
        Assert.False(typeof(IKnkReadRule).IsAssignableFrom(typeof(RequireReferenceReadAttribute)));
        Assert.Equal(ReadCategory.Stricter, ReadAccess.Of(typeof(EntityReferencesController), method).Category);
        Assert.NotEmpty(method.GetCustomAttributes<HttpGetAttribute>());
    }

    [Fact]
    public async Task Anonymous_Is401_ThePluginKeyAlone_Is403()
    {
        Assert.Equal(401, Status(await Gate(ServiceAuthTestHelper.Anonymous(), "Street")));
        Assert.Equal(403, Status(await Gate(ServiceAuthTestHelper.Plugin(), "Street")));
    }

    [Fact]
    public async Task WithoutAReadNodeOfTheType_Is403NoReadAccess_WithTheNodes()
    {
        _held.Add(StaffPermissions.ManageContent);
        var refused = await Gate(ServiceAuthTestHelper.WebUser(Staff), "Street");

        Assert.Equal(403, Status(refused));
        var json = JsonSerializer.Serialize(((ObjectResult)refused!).Value);
        Assert.Contains("\"code\":\"NoReadAccess\"", json);
        Assert.Contains(StaffPermissions.ManageWorld, json);
    }

    [Theory]
    [InlineData(StaffPermissions.ManageWorld)]
    [InlineData(StaffPermissions.RoadManage)] // a cross-area read node of streets (DESIGN § 4.6.2)
    public async Task AnyReadNodeOfTheType_Passes(string node)
    {
        _held.Add(node);
        Assert.Equal(200, Status(await Gate(ServiceAuthTestHelper.WebUser(Staff), "street")));
    }

    [Fact]
    public async Task ATypeOutsideTheRegistry_Is404()
    {
        _held.Add("*");
        Assert.Equal(404, Status(await Gate(ServiceAuthTestHelper.WebUser(Staff), "FormConfiguration")));
    }

    // ===== What the rows show =====

    [Fact]
    public async Task RowsOfAReadableType_AreLabelledAndReadable_SortedById()
    {
        _held.Add(StaffPermissions.ManageWorld);
        StreetIsUsedBy(new ReferencingRows("Structure.StreetId", "Structure", 2, new[] { "9", "3" }));

        var result = await Service().GetAsync(DashboardAccess.Find("Street")!, 12, Staff, null, 20);

        var group = Assert.Single(result!.Groups);
        Assert.Equal("Structure", group.EntityType);
        Assert.True(group.Listed);
        Assert.True(group.Readable);
        Assert.Equal(2, group.Count);
        Assert.Equal(new[] { 3, 9 }, group.Rows.Select(r => r.Id));
        Assert.Equal("Structure 3", group.Rows[0].Label);
        Assert.False(group.More);
        Assert.Null(group.Reason);
    }

    [Fact]
    public async Task RowsOfAnotherArea_WithAnAreaNode_AreNamedButNotReadable()
    {
        // A world builder deleting a town that a clan uses: they may name the clan (api/Lookup), not open it.
        _held.Add(StaffPermissions.ManageWorld);
        StreetIsUsedBy(new ReferencingRows("Kit.SomeStreetId", "Kit", 1, new[] { "4" }));

        var group = Assert.Single((await Service().GetAsync(DashboardAccess.Find("Street")!, 12, Staff, null, 20))!.Groups);

        Assert.True(group.Listed);
        Assert.False(group.Readable);
        Assert.Equal("Kit 4", Assert.Single(group.Rows).Label);
        Assert.Contains(StaffPermissions.ManageKits, group.Nodes);
    }

    [Fact]
    public async Task WithoutAnyAreaNode_OtherTypesAreOnlyCounted()
    {
        // A discovery admin holds a page node but no area node: no lookup labels either (LAM2-4).
        _held.Add(StaffPermissions.ManageDiscovery);
        StreetIsUsedBy(new ReferencingRows("Structure.StreetId", "Structure", 3, new[] { "1", "2", "3" }));

        var group = Assert.Single((await Service().GetAsync(DashboardAccess.Find("Street")!, 12, Staff, null, 20))!.Groups);

        Assert.False(group.Listed);
        Assert.Empty(group.Rows);
        Assert.Equal(3, group.Count);
        Assert.Contains("requires one of", group.Reason);
        _lookups.Verify(l => l.GetAsync(It.IsAny<DashboardTypeAccess>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RowsThatAreNoDashboardType_AreOnlyCounted()
    {
        _held.Add("*");
        StreetIsUsedBy(new ReferencingRows("FormSubmissionProgress.StreetId", "FormSubmissionProgress", 2, new[] { "1", "2" }),
            new ReferencingRows("Thing (link)", null, 1, Array.Empty<string>()));

        var groups = (await Service().GetAsync(DashboardAccess.Find("Street")!, 12, Staff, null, 20))!.Groups;

        Assert.All(groups, g => Assert.False(g.Listed));
        Assert.All(groups, g => Assert.Empty(g.Rows));
        Assert.Equal(EntityReferenceRowsService.NotADashboardType, groups[0].Reason);
    }

    [Fact]
    public async Task ReferencedBy_NarrowsToOneGroup_AndTakeSetsMore()
    {
        _held.Add(StaffPermissions.ManageWorld);
        StreetIsUsedBy(new ReferencingRows("District (link)", "District", 1, new[] { "5" }),
            new ReferencingRows("Structure.StreetId", "Structure", 3, new[] { "1", "2", "3" }));

        var result = await Service().GetAsync(DashboardAccess.Find("Street")!, 12, Staff, "structure.streetid", 2);

        var group = Assert.Single(result!.Groups);
        Assert.Equal("Structure.StreetId", group.ReferencedBy);
        Assert.Equal(2, group.Rows.Count);
        Assert.True(group.More);
        Assert.Equal(3, group.Count);
    }

    [Fact]
    public async Task TakeIsCappedAt50_AndALabelThatFails_KeepsTheId()
    {
        _held.Add(StaffPermissions.ManageWorld);
        StreetIsUsedBy(new ReferencingRows("Structure.StreetId", "Structure", 60, Enumerable.Range(1, 60).Select(i => i.ToString()).ToArray()));
        _lookups.Setup(l => l.GetAsync(It.IsAny<DashboardTypeAccess>(), 1)).ThrowsAsync(new InvalidOperationException("boom"));

        var group = Assert.Single((await Service().GetAsync(DashboardAccess.Find("Street")!, 12, Staff, null, 500))!.Groups);

        Assert.Equal(EntityReferenceRowsService.MaxTake, group.Rows.Count);
        Assert.Equal("Structure #1", group.Rows[0].Label);
        Assert.True(group.More);
    }

    [Fact]
    public async Task OnlyBlockingReferences_AreAskedFor()
    {
        _held.Add(StaffPermissions.ManageWorld);
        StreetIsUsedBy();

        var result = await Service().GetAsync(DashboardAccess.Find("Street")!, 12, Staff, null, 20);

        Assert.Empty(result!.Groups);
        _references.Verify(r => r.GetReferencingRowsAsync("Street", "12", true), Times.Once);
    }
}

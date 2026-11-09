using System.Reflection;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#188 under the #280 model: link changes are edits (the type's write node, and the related type's
/// when the related row changes), web users only, never knk.admin.delete; the controller turns refusals into readable
/// answers.
/// </summary>
public class RelationshipsControllerTests
{
    private const int Staff = 7;
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly KnKDbContext _db = new(new DbContextOptionsBuilder<KnKDbContext>()
        .UseInMemoryDatabase("relationships-api-" + Guid.NewGuid()).Options);

    public RelationshipsControllerTests()
    {
        _permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((int userId, string node) => new PermissionCheckResponseDto
            {
                UserId = userId,
                Node = node,
                Result = _held.Contains(node) ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });
    }

    private async Task<IActionResult?> Gate(HttpContext http, string type, string property)
    {
        var routeData = new RouteData();
        routeData.Values[RelationshipWriteFilter.TypeRouteKey] = type;
        routeData.Values[RelationshipWriteFilter.PropertyRouteKey] = property;
        var context = new AuthorizationFilterContext(new ActionContext(http, routeData, new ActionDescriptor()), new List<IFilterMetadata>());
        await new RelationshipWriteFilter(_permissions.Object, _db).OnAuthorizationAsync(context);
        return context.Result;
    }

    private static int Status(IActionResult? result) => result switch
    {
        null => 200,
        UnauthorizedObjectResult => 401,
        ObjectResult o => o.StatusCode ?? 200,
        _ => -1
    };

    [Fact]
    public void BothActions_CarryTheRelationshipRule_AndNoDeleteNode()
    {
        Assert.NotNull(typeof(RelationshipsController).GetCustomAttribute<RequireRelationshipWriteAttribute>());
        Assert.True(typeof(IKnkAccessRule).IsAssignableFrom(typeof(RequireRelationshipWriteAttribute)));
        foreach (var name in new[] { nameof(RelationshipsController.Link), nameof(RelationshipsController.Unlink) })
        {
            var method = typeof(RelationshipsController).GetMethod(name)!;
            Assert.DoesNotContain(method.GetCustomAttributes<RequirePermissionAttribute>(), a => Equals(a.Arguments?[0], StaffPermissions.Delete));
            // Writes only: no new read for ReadAccessRulesTests to pin (labels and pickers use api/Lookup).
            Assert.Empty(method.GetCustomAttributes<HttpGetAttribute>());
            Assert.NotEmpty(method.GetCustomAttributes<HttpPostAttribute>());
        }
    }

    [Fact]
    public async Task Anonymous_Is401_ThePluginKeyAlone_Is403()
    {
        Assert.Equal(401, Status(await Gate(ServiceAuthTestHelper.Anonymous(), "Town", "streets")));
        Assert.Equal(403, Status(await Gate(ServiceAuthTestHelper.Plugin(), "Town", "streets")));
    }

    [Fact]
    public async Task WithoutTheWriteNode_Is403_WithTheNodes()
    {
        _held.Add(StaffPermissions.ManageContent);
        var refused = await Gate(ServiceAuthTestHelper.WebUser(Staff), "Town", "streets");

        Assert.Equal(403, Status(refused));
        var json = JsonSerializer.Serialize(((ObjectResult)refused!).Value);
        Assert.Contains("\"code\":\"NoWriteAccess\"", json);
        Assert.Contains("\"nodes\":[\"knk.admin.world\"]", json);
    }

    [Fact]
    public async Task WithTheWriteNode_Passes_WithoutAnyDeleteNode()
    {
        _held.Add(StaffPermissions.ManageWorld);
        Assert.Equal(200, Status(await Gate(ServiceAuthTestHelper.WebUser(Staff), "town", "Streets")));
    }

    [Fact]
    public async Task ChildrenOfAnotherType_NeedBothWriteNodes()
    {
        // The grant row's column would change: the user's area node alone isn't enough.
        _held.Add(StaffPermissions.ManageUsers);
        Assert.Equal(403, Status(await Gate(ServiceAuthTestHelper.WebUser(Staff), "User", "grants")));
        _held.Add(StaffPermissions.UserPermissions);
        Assert.Equal(200, Status(await Gate(ServiceAuthTestHelper.WebUser(Staff), "User", "grants")));
    }

    [Fact]
    public async Task UnknownTypesAndRelationships_Are404()
    {
        _held.Add("*");
        Assert.Equal(404, Status(await Gate(ServiceAuthTestHelper.WebUser(Staff), "Nope", "streets")));
        Assert.Equal(404, Status(await Gate(ServiceAuthTestHelper.WebUser(Staff), "Town", "nope")));
    }

    [Fact]
    public async Task Controller_AnswersRefusalsReadably_AndAlreadyLinked_WithTheCurrentParent()
    {
        _db.Categories.AddRange(
            new Category { Id = 1, Name = "Weapons" }, new Category { Id = 2, Name = "Tools" },
            new Category { Id = 3, Name = "Axes", ParentCategoryId = 2 });
        await _db.SaveChangesAsync();
        var controller = new RelationshipsController(new RelationshipService(_db));

        var asked = (ObjectResult)await controller.Link("Category", 1, "childCategories", new RelationshipLinkRequestDto { RelatedId = 3 });
        Assert.Equal(409, asked.StatusCode);
        Assert.Equal("{\"error\":\"AlreadyLinked\",\"message\":\"This Category already belongs to Category #2. Move it here?\",\"currentId\":2}",
            JsonSerializer.Serialize(asked.Value));

        var moved = Assert.IsType<OkObjectResult>(await controller.Link("Category", 1, "childCategories",
            new RelationshipLinkRequestDto { RelatedId = 3, Move = true }));
        Assert.True(((RelationshipChangeResultDto)moved.Value!).Changed);

        var required = (ObjectResult)await controller.Unlink("Town", 1, "districts", new RelationshipUnlinkRequestDto { RelatedId = 2 });
        Assert.Equal(400, required.StatusCode);
        Assert.Contains("always belongs to one Town", JsonSerializer.Serialize(required.Value));
    }
}

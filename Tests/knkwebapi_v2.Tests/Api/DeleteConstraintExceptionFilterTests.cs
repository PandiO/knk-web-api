using AutoMapper;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// KNG-162: a DELETE refused by a database constraint answers 409, not 500, with a plain-words
/// reason naming what still uses the item.
/// </summary>
public class DeleteConstraintExceptionFilterTests
{
    private static ExceptionContext Context(string method, Exception exception, string? controller = null, string? id = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        var routeData = new RouteData();
        if (id != null) routeData.Values["id"] = id;
        ActionDescriptor descriptor = controller == null
            ? new ActionDescriptor()
            : new ControllerActionDescriptor { ControllerName = controller };
        var action = new ActionContext(http, routeData, descriptor);
        return new ExceptionContext(action, new List<IFilterMetadata>()) { Exception = exception };
    }

    private sealed class NoReferences : IEntityReferenceService
    {
        public Task<EntityReferencesDto?> GetReferencesAsync(string entityTypeName, string id, IEnumerable<string>? ignore = null) =>
            Task.FromResult<EntityReferencesDto?>(null);
    }

    private static readonly DeleteConstraintExceptionFilter Filter = new(NullLogger<DeleteConstraintExceptionFilter>.Instance, new NoReferences());

    private static string Message(ExceptionContext context)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(context.Result);
        return (string)conflict.Value!.GetType().GetProperty("message")!.GetValue(conflict.Value)!;
    }

    [Fact]
    public async Task DeleteWithDbUpdateException_Answers409()
    {
        var context = Context("DELETE", new DbUpdateException("FK", new Exception("Cannot delete or update a parent row")));

        await Filter.OnExceptionAsync(context);

        Assert.True(context.ExceptionHandled);
        var conflict = Assert.IsType<ConflictObjectResult>(context.Result);
        Assert.Contains("DbConstraint", conflict.Value!.ToString());
        Assert.Equal("This item is still in use and can't be deleted.", Message(context));
    }

    [Fact]
    public async Task WrappedDbUpdateException_IsRecognised()
    {
        var context = Context("DELETE", new InvalidOperationException("wrapped", new DbUpdateException("FK")));

        await Filter.OnExceptionAsync(context);

        Assert.IsType<ConflictObjectResult>(context.Result);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task OtherVerbs_AreLeftAlone(string method)
    {
        var context = Context(method, new DbUpdateException("FK"));

        await Filter.OnExceptionAsync(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task DeletingAStreetAStructureStillUses_Answers409WithTheReason()
    {
        // StreetService loads the Street with its Structures first, so EF refuses the delete in its
        // change tracker (a severed required relationship) before any SQL runs: no DbUpdateException.
        using var db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Towns.Add(new Town { Id = 1, Name = "Rivia", Description = "", WgRegionId = "town_rivia" });
        db.Districts.Add(new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "d", TownId = 1 });
        db.Streets.Add(new Street { Id = 4, Name = "Main" });
        db.Structures.Add(new Structure { Id = 3, Name = "Smithy", Description = "", WgRegionId = "s", StreetId = 4, DistrictId = 2 });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<StreetMappingProfile>()).CreateMapper();
        var service = new StreetService(new StreetRepository(db), new DistrictRepository(db), new RoadNetworkRepository(db), mapper);
        var filter = new DeleteConstraintExceptionFilter(NullLogger<DeleteConstraintExceptionFilter>.Instance, new EntityReferenceService(db));

        var thrown = await Record.ExceptionAsync(() => service.DeleteAsync(4));
        var context = Context("DELETE", thrown!, controller: "Streets", id: "4");
        await filter.OnExceptionAsync(context);

        Assert.Equal("This street can't be deleted: it is still used by 1 structure. Remove or change those first.", Message(context));
    }

    [Fact]
    public void Reasons_ArePluralAndFriendly()
    {
        var references = new EntityReferencesDto
        {
            EntityTypeName = "FormConfiguration",
            Id = "9",
            InUse = true,
            References =
            {
                new EntityReferenceCountDto { ReferencedBy = "FormField.SubConfigurationId", Count = 1 },
                new EntityReferenceCountDto { ReferencedBy = "WorkflowSession.FormConfigurationId", Count = 3 },
                new EntityReferenceCountDto { ReferencedBy = "Category (link)", Count = 2 }
            }
        };

        Assert.Equal(
            "This form configuration can't be deleted: it is still used by 1 form field that opens it as a sub-form, "
            + "3 form sessions started with it and 2 categories. Remove or change those first.",
            DeleteConstraintExceptionFilter.DescribeRefusal(references));
    }

    [Fact]
    public async Task OtherExceptions_AreLeftAlone()
    {
        var context = Context("DELETE", new InvalidOperationException("boom"));

        await Filter.OnExceptionAsync(context);

        Assert.False(context.ExceptionHandled);
    }
}

using AutoMapper;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>KNG-162: a DELETE refused by a database constraint answers 409, not 500.</summary>
public class DeleteConstraintExceptionFilterTests
{
    private static ExceptionContext Context(string method, Exception exception)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ExceptionContext(action, new List<IFilterMetadata>()) { Exception = exception };
    }

    private static readonly DeleteConstraintExceptionFilter Filter = new(NullLogger<DeleteConstraintExceptionFilter>.Instance);

    [Fact]
    public void DeleteWithDbUpdateException_Answers409()
    {
        var context = Context("DELETE", new DbUpdateException("FK", new Exception("Cannot delete or update a parent row")));

        Filter.OnException(context);

        Assert.True(context.ExceptionHandled);
        var conflict = Assert.IsType<ConflictObjectResult>(context.Result);
        Assert.Contains("DbConstraint", conflict.Value!.ToString());
    }

    [Fact]
    public void WrappedDbUpdateException_IsRecognised()
    {
        var context = Context("DELETE", new InvalidOperationException("wrapped", new DbUpdateException("FK")));

        Filter.OnException(context);

        Assert.IsType<ConflictObjectResult>(context.Result);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public void OtherVerbs_AreLeftAlone(string method)
    {
        var context = Context(method, new DbUpdateException("FK"));

        Filter.OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task DeletingAStreetAStructureStillUses_Answers409()
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

        var thrown = await Record.ExceptionAsync(() => service.DeleteAsync(4));
        var context = Context("DELETE", thrown!);
        Filter.OnException(context);

        Assert.IsType<ConflictObjectResult>(context.Result);
    }

    [Fact]
    public void OtherExceptions_AreLeftAlone()
    {
        var context = Context("DELETE", new InvalidOperationException("boom"));

        Filter.OnException(context);

        Assert.False(context.ExceptionHandled);
    }
}

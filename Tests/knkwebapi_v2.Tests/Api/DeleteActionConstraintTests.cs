using KnKWebAPI.Controllers;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// KNG-169: DELETE actions no longer answer a refused delete with their own vague text (EF's
/// "An error occurred while saving the entity changes..." or "still in use"); they let it through
/// to DeleteConstraintExceptionFilter, which names what blocks the delete. Their own business-rule
/// answers stay.
/// </summary>
public class DeleteActionConstraintTests
{
    [Fact]
    public async Task ADatabaseRefusal_ReachesTheFilter()
    {
        var service = new Mock<IDistrictService>();
        service.Setup(s => s.DeleteAsync(2)).ThrowsAsync(new DbUpdateException("An error occurred while saving the entity changes."));

        var thrown = await Record.ExceptionAsync(() => new DistrictsController(service.Object).Delete(2));

        Assert.IsType<DbUpdateException>(thrown);
    }

    [Fact]
    public async Task EfsSeveredRelationship_ReachesTheFilter_NotTheBusinessRuleAnswer()
    {
        var severed = new InvalidOperationException("The association between entity types 'Category' and 'ItemBlueprint' has been severed.")
        {
            Source = "Microsoft.EntityFrameworkCore"
        };
        var service = new Mock<ICategoryService>();
        service.Setup(s => s.DeleteAsync(5)).ThrowsAsync(severed);

        var thrown = await Record.ExceptionAsync(() => new CategoriesController(service.Object).Delete(5));

        Assert.Same(severed, thrown);
    }

    [Fact]
    public async Task ABusinessRule_KeepsItsOwnAnswer()
    {
        var service = new Mock<ICategoryService>();
        service.Setup(s => s.DeleteAsync(5)).ThrowsAsync(new InvalidOperationException("This category has subcategories."));

        var result = await new CategoriesController(service.Object).Delete(5);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("This category has subcategories.", conflict.Value!.ToString());
    }
}

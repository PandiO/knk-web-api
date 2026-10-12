using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// KNG-162: a DELETE that a database constraint refuses (the row is still referenced) answers
    /// 409 {code: "DbConstraint"} instead of a 500 (see IsConstraintFailure). Most delete actions already catch
    /// DbUpdateException themselves and answer the same way; this covers the ones that don't (e.g.
    /// Streets), so the web app can report "skipped: in use" for any entity type. Registered
    /// globally in Program.cs; other verbs and other exceptions are left alone.
    /// </summary>
    public class DeleteConstraintExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<DeleteConstraintExceptionFilter> _logger;

        public DeleteConstraintExceptionFilter(ILogger<DeleteConstraintExceptionFilter> logger)
        {
            _logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            if (context.ExceptionHandled || !HttpMethods.IsDelete(context.HttpContext.Request.Method)) return;
            if (!IsConstraintFailure(context.Exception)) return;

            _logger.LogWarning(context.Exception, "DELETE {Path} hit a database constraint", context.HttpContext.Request.Path);
            context.Result = new ConflictObjectResult(new
            {
                code = "DbConstraint",
                message = "This item is still in use and can't be deleted."
            });
            context.ExceptionHandled = true;
        }

        /// <summary>
        /// The database refused the delete (DbUpdateException), or EF did before sending it: a service
        /// that loaded the entity with its dependents (StreetService loads a Street's Structures) gets
        /// EF's "association ... has been severed" InvalidOperationException for a required,
        /// non-cascading relationship instead.
        /// </summary>
        internal static bool IsConstraintFailure(Exception? exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is DbUpdateException || IsSeveredRequiredRelationship(current)) return true;
            }
            return false;
        }

        private static bool IsSeveredRequiredRelationship(Exception exception) =>
            exception is InvalidOperationException
            && exception.Source == "Microsoft.EntityFrameworkCore"
            && exception.Message.Contains("has been severed", StringComparison.Ordinal);
    }
}

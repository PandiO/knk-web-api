using System.Text.RegularExpressions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// KNG-162: a DELETE that a database constraint refuses (the row is still referenced) answers
    /// 409 {code: "DbConstraint", message} instead of a 500 (see IsConstraintFailure). The message
    /// says in plain words what still uses the item ("This street can't be deleted: it is still used
    /// by 1 structure."), from the same reference check the web app uses before deleting. Most delete
    /// actions already catch DbUpdateException themselves and answer the same way; this covers the
    /// ones that don't (e.g. Streets, FormConfigurations). Registered globally in Program.cs; other
    /// verbs and other exceptions are left alone.
    /// </summary>
    public class DeleteConstraintExceptionFilter : IAsyncExceptionFilter
    {
        internal const string GenericMessage = "This item is still in use and can't be deleted.";

        // What a reference is called when its type name alone doesn't explain it to an admin.
        private static readonly Dictionary<string, (string One, string Many)> FriendlyReferences = new(StringComparer.Ordinal)
        {
            ["FormField.SubConfigurationId"] = ("form field that opens it as a sub-form", "form fields that open it as a sub-form"),
            ["FormStep.SubConfigurationId"] = ("form step that uses it as a sub-form", "form steps that use it as a sub-form"),
            ["WorkflowSession.FormConfigurationId"] = ("form session started with it", "form sessions started with it"),
            ["FormSubmissionProgress.FormConfigurationId"] = ("saved draft", "saved drafts"),
        };

        private readonly ILogger<DeleteConstraintExceptionFilter> _logger;
        private readonly IEntityReferenceService _references;

        public DeleteConstraintExceptionFilter(ILogger<DeleteConstraintExceptionFilter> logger, IEntityReferenceService references)
        {
            _logger = logger;
            _references = references;
        }

        public async Task OnExceptionAsync(ExceptionContext context)
        {
            if (context.ExceptionHandled || !HttpMethods.IsDelete(context.HttpContext.Request.Method)) return;
            if (!IsConstraintFailure(context.Exception)) return;

            _logger.LogWarning(context.Exception, "DELETE {Path} hit a database constraint", context.HttpContext.Request.Path);
            var references = await FindReferencesAsync(context);
            context.Result = new ConflictObjectResult(new
            {
                code = "DbConstraint",
                message = DescribeRefusal(references),
                references = references?.References
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

        /// <summary>
        /// The entity is named by the controller ("Streets" → Street, "Categories" → Category) and
        /// the route's id. Null when neither fits a model type; the reason then stays generic.
        /// </summary>
        private async Task<EntityReferencesDto?> FindReferencesAsync(ExceptionContext context)
        {
            if (context.ActionDescriptor is not ControllerActionDescriptor action) return null;
            if (context.RouteData.Values.TryGetValue("id", out var routeId) is false || routeId == null) return null;
            var id = Convert.ToString(routeId, System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(id)) return null;

            try
            {
                foreach (var typeName in SingularCandidates(action.ControllerName))
                {
                    var found = await _references.GetReferencesAsync(typeName, id);
                    if (found != null) return found;
                }
            }
            catch (Exception ex)
            {
                // The reason is a courtesy: never let it turn the 409 back into a 500.
                _logger.LogWarning(ex, "Could not list what still uses {Controller} {Id}", action.ControllerName, id);
            }
            return null;
        }

        private static IEnumerable<string> SingularCandidates(string controllerName)
        {
            yield return controllerName;
            if (controllerName.EndsWith("ies", StringComparison.Ordinal)) yield return controllerName[..^3] + "y";
            if (controllerName.EndsWith("es", StringComparison.Ordinal)) yield return controllerName[..^2];
            if (controllerName.EndsWith("s", StringComparison.Ordinal)) yield return controllerName[..^1];
        }

        /// <summary>"This street can't be deleted: it is still used by 1 structure and 2 towns."</summary>
        public static string DescribeRefusal(EntityReferencesDto? references)
        {
            if (references == null || references.References.Count == 0) return GenericMessage;
            var uses = references.References.Select(r => $"{r.Count} {DescribeReference(r.ReferencedBy, r.Count)}").ToList();
            var list = uses.Count == 1 ? uses[0] : $"{string.Join(", ", uses.Take(uses.Count - 1))} and {uses[^1]}";
            return $"This {Words(references.EntityTypeName)} can't be deleted: it is still used by {list}. Remove or change those first.";
        }

        /// <summary>"Structure.DistrictId" → "structure(s)", "Street (link)" → "street(s)", or a friendly name.</summary>
        private static string DescribeReference(string referencedBy, int count)
        {
            if (FriendlyReferences.TryGetValue(referencedBy, out var friendly)) return count == 1 ? friendly.One : friendly.Many;
            var typeName = referencedBy.Split(new[] { '.', ' ' }, 2)[0];
            var words = Words(typeName);
            return count == 1 ? words : Plural(words);
        }

        private static string Words(string pascalCase) =>
            Regex.Replace(pascalCase, "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();

        private static string Plural(string words)
        {
            if (Regex.IsMatch(words, "[^aeiou]y$")) return words[..^1] + "ies";
            if (Regex.IsMatch(words, "(s|x|ch|sh)$")) return words + "es";
            return words + "s";
        }
    }
}

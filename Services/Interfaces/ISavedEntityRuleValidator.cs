using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Runs the field-validation rules (FieldValidationRule, the FormConfigBuilder's "Cross-Field
    /// Validation") of an entity type's default FormConfiguration against a saved entity, the same
    /// way the FormWizard runs them while that entity is edited. Lets a server-side check (e.g. siege
    /// scenario readiness) enforce exactly what an admin configured instead of hard-coding rules.
    /// </summary>
    public interface ISavedEntityRuleValidator
    {
        /// <param name="entityTypeName">Entity type whose default FormConfiguration holds the rules.</param>
        /// <param name="entity">The saved entity, with the navigations its form fields point at loaded.</param>
        /// <param name="parentContext">
        /// The <see cref="SavedEntityRuleValidation.Context"/> of the entity this one is owned by (null for a
        /// root entity). A rule's dependency field is looked up by name in the entity's own values first,
        /// then in the parents' - the FormWizard does the same for a child form opened from a parent form.
        /// </param>
        Task<SavedEntityRuleValidation> ValidateAsync(
            string entityTypeName,
            object entity,
            IReadOnlyDictionary<string, object?>? parentContext = null);
    }

    public class SavedEntityRuleValidation
    {
        /// <summary>The form values this entity's rules ran with: the parent context overlaid with the entity's own fields.</summary>
        public Dictionary<string, object?> Context { get; init; } = new();

        /// <summary>One result per field that has rules and a value (empty fields are left to the field's Required flag).</summary>
        public List<SavedFieldRuleResult> Results { get; init; } = new();
    }

    public class SavedFieldRuleResult
    {
        public int FormFieldId { get; init; }
        public string FieldName { get; init; } = string.Empty;
        public string FieldLabel { get; init; } = string.Empty;
        public ValidationResultDto Result { get; init; } = null!;

        /// <summary>The result's message with its {placeholders} filled in.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>The rule couldn't reach a verdict (e.g. the Minecraft plugin is unreachable).</summary>
        public bool CouldNotRun => Result.Metadata?.FailureReason != null;
    }
}
